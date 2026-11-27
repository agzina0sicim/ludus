using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Ludus.Application.Indexers;
using Ludus.Application.Metadata;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Downloads;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Metadata;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ludus.Api.Features;

public sealed record GameRequest([MaxLength(512)] string? Title, [Range(1, long.MaxValue)] long PlatformId,
    [Range(1, long.MaxValue)] long RootFolderId, [Range(1, long.MaxValue)] long QualityProfileId, bool Monitored,
    MetadataProviderKind? Provider, [MaxLength(128)] string? ExternalId, [RegularExpression("^[0-9A-Fa-f]{16}$")] string? BaseTitleId);
public sealed record GameUpdate([Required, MaxLength(512)] string Title, [Range(1, long.MaxValue)] long RootFolderId,
    [Range(1, long.MaxValue)] long QualityProfileId, bool Monitored);
public sealed record GrabRequest([Required, MaxLength(64)] string Ticket);
public sealed record MetadataAssignment(MetadataProviderKind Provider, [Required, MaxLength(128)] string ExternalId);

public sealed class ReleaseTickets : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    public string Store(long gameId, IndexerRelease release)
    {
        var ticket = Guid.NewGuid().ToString("N");
        _cache.Set(ticket, (gameId, release), new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return ticket;
    }
    public IndexerRelease Read(long gameId, string ticket) => _cache.TryGetValue<(long GameId, IndexerRelease Release)>(ticket, out var item) && item.GameId == gameId
        ? item.Release : throw new KeyNotFoundException("Search ticket expired.");
    public void Dispose() => _cache.Dispose();
}

[ApiController, Route("api/v1/games")]
public sealed class GamesController(AppDbContext database, MetadataService metadata, IInteractiveSearch search, ReleaseTickets tickets, DownloadCoordinator downloads) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? query, [FromQuery] long? platformId, [FromQuery] long? rootFolderId,
        [FromQuery] string? status, [FromQuery] VerificationStatus? verification, [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 50, [FromQuery] string sort = "title",
        [FromQuery] bool descending = false, CancellationToken cancellationToken = default)
    {
        var games = database.Games.AsNoTracking().Where(x => (query == null || x.Title.Contains(query)) && (platformId == null || x.PlatformId == platformId)
            && (rootFolderId == null || x.RootFolderId == rootFolderId));
        games = status switch
        {
            null or "all" => games, "missing" => games.Where(x => x.Status == GameStatus.Missing),
            "downloading" => games.Where(x => x.Status == GameStatus.Downloading), "available" => games.Where(x => x.Status == GameStatus.Available),
            "monitored" => games.Where(x => x.Monitored), "unmonitored" => games.Where(x => !x.Monitored),
            "cutoffUnmet" => games.Where(x => x.Files.Any(f => !f.Missing && f.ContentKind == ContentKind.Base && f.Score < x.QualityProfile.UpgradeUntilScore)),
            _ => throw new ArgumentException("Invalid library status.")
        };
        if (verification is not null) games = games.Where(x => x.Files.Any(f => !f.Missing && f.VerificationStatus == verification));
        var metadataLanguage = (await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken)).MetadataLanguage;
        var total = await games.CountAsync(cancellationToken);
        IOrderedQueryable<Game> OrderBy<TKey>(Expression<Func<Game, TKey>> field) =>
            descending ? games.OrderByDescending(field) : games.OrderBy(field);
        var ordered = sort switch
        {
            "title" => OrderBy(x => x.SortTitle),
            "platform" => OrderBy(x => x.Platform.Name),
            "year" => OrderBy(x => x.ReleaseYear),
            "status" => OrderBy(x => x.Status),
            "files" => OrderBy(x => x.Files.Count(f => !f.Missing)),
            _ => throw new ArgumentException("Invalid library sort field.")
        };
        var items = await ordered.ThenBy(x => x.Id).Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new { x.Id,
                title = x.MetadataTranslations.Where(t => t.Language == metadataLanguage).OrderBy(t => t.Provider == x.Platform.PrimaryMetadataProvider ? 0 : 1)
                    .Select(t => t.Title).FirstOrDefault() ?? x.Title,
                x.ReleaseYear, x.ReleaseDateUtc, x.ReleaseDatePrecision, x.PlatformId, platform = x.Platform.Name, x.RootFolderId, x.QualityProfileId, x.Monitored,
                x.Status, x.CoverRelativePath,
                description = x.MetadataTranslations.Where(t => t.Language == metadataLanguage).OrderBy(t => t.Provider == x.Platform.PrimaryMetadataProvider ? 0 : 1)
                    .Select(t => t.Description).FirstOrDefault() ?? x.Description,
                fileCount = x.Files.Count(f => !f.Missing), x.Version }).ToArrayAsync(cancellationToken);
        return Ok(new { page, pageSize, total, items });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, [FromQuery, MaxLength(16)] string? language, CancellationToken cancellationToken)
    {
        var game = await database.Games.AsNoTracking().Include(x => x.Files).Include(x => x.MetadataReferences).Include(x => x.MetadataTranslations).Include(x => x.Platform)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        language ??= (await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken)).MetadataLanguage;
        var localized = game.MetadataTranslations.Where(x => x.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Provider == game.Platform.PrimaryMetadataProvider ? 0 : 1).FirstOrDefault()
            ?? game.MetadataTranslations.Where(x => x.Language == "und").OrderBy(x => x.Provider == game.Platform.PrimaryMetadataProvider ? 0 : 1).FirstOrDefault()
            ?? game.MetadataTranslations.OrderBy(x => x.Provider == game.Platform.PrimaryMetadataProvider ? 0 : 1).FirstOrDefault();
        return Ok(new { game.Id, title = localized?.Title ?? game.Title, description = localized?.Description ?? game.Description,
            game.ReleaseYear, game.ReleaseDateUtc, game.ReleaseDatePrecision, game.Developer, game.Publisher, game.Genres, game.Serials, game.BaseTitleId,
            game.CoverRelativePath, game.Monitored, game.Status, game.PlatformId, game.RootFolderId, game.QualityProfileId, game.MetadataUpdatedAtUtc, game.Version,
            requestedMetadataLanguage = language, titleLanguage = localized?.TitleLanguage, descriptionLanguage = localized?.DescriptionLanguage,
            availableMetadataLanguages = game.MetadataTranslations.Select(x => x.Language).Distinct().Order().ToArray(),
            metadataReferences = game.MetadataReferences.Select(x => new { x.Provider, x.ExternalId }),
            files = game.Files.Select(x => new { x.Id, x.RelativePath, x.OriginalFileName, x.ReleaseName, x.ContentKind, x.Role, x.FileGroupId, x.DiscNumber,
                x.Score, x.Size, x.Crc32, x.Md5, x.Sha1, x.TitleId, x.Revision, x.Regions, x.Languages, x.VerificationStatus, x.DatRomId,
                x.ImportedAtUtc, x.LastVerifiedAtUtc, x.LastSeenAtUtc, x.Missing, x.Version }) });
    }

    [HttpPost]
    public async Task<IActionResult> Add(GameRequest request, CancellationToken cancellationToken)
    {
        if (request.BaseTitleId is not null && (!ReleaseParser.TryParseSwitchTitleId(request.BaseTitleId, out var identity) || identity?.ContentKind != ContentKind.Base))
            return BadRequest(new { code = "BaseTitleIdRequired" });
        if (!await database.Platforms.AnyAsync(x => x.Id == request.PlatformId, cancellationToken)
            || !await database.RootFolders.AnyAsync(x => x.Id == request.RootFolderId, cancellationToken)
            || !await database.QualityProfiles.AnyAsync(x => x.Id == request.QualityProfileId && x.PlatformId == request.PlatformId, cancellationToken)) return BadRequest(new { code = "InvalidGameAssignment" });
        var data = request.Provider is not null && request.ExternalId is not null ? await metadata.GetAsync(request.Provider.Value, request.ExternalId, request.PlatformId, cancellationToken) : null;
        var title = data?.Title ?? request.Title;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) return BadRequest(new { code = "TitleRequired" });
        if (await database.Games.AnyAsync(x => x.PlatformId == request.PlatformId && (x.Title == title || x.MetadataReferences.Any(r => r.Provider == request.Provider && r.ExternalId == request.ExternalId)), cancellationToken))
            return Conflict(new { code = "GameAlreadyExists" });
        var game = new Game { Title = title, SortTitle = title, PlatformId = request.PlatformId, RootFolderId = request.RootFolderId,
            QualityProfileId = request.QualityProfileId, Monitored = request.Monitored, BaseTitleId = request.BaseTitleId?.ToUpperInvariant(),
            Description = data?.Description, ReleaseYear = data?.Year, ReleaseDateUtc = data?.ReleaseDateUtc,
            ReleaseDatePrecision = data?.ReleaseDatePrecision, Developer = data?.Developer, Publisher = data?.Publisher,
            Genres = data?.Genres.ToList() ?? [], CoverRelativePath = data?.CoverRelativePath, MetadataUpdatedAtUtc = data is null ? null : DateTime.UtcNow };
        if (data is not null)
        {
            game.MetadataReferences.Add(new GameMetadataReference { Provider = data.Provider, ExternalId = data.ExternalId });
            AddTranslations(game, data);
        }
        database.Games.Add(game);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = game.Id }, new { game.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, GameUpdate request, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.Files).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        if (request.RootFolderId != game.RootFolderId && game.Files.Count > 0) return Conflict(new { code = "MoveFilesBeforeChangingRoot" });
        if (!await database.QualityProfiles.AnyAsync(x => x.Id == request.QualityProfileId && x.PlatformId == game.PlatformId, cancellationToken))
            return BadRequest(new { code = "ProfilePlatformMismatch" });
        game.Title = request.Title.Trim(); game.SortTitle = game.Title; game.Monitored = request.Monitored;
        game.RootFolderId = request.RootFolderId; game.QualityProfileId = request.QualityProfileId;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:long}/refresh-metadata")]
    public async Task<IActionResult> Refresh(long id, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.MetadataReferences).Include(x => x.MetadataTranslations).Include(x => x.Platform).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var reference = game.MetadataReferences.OrderBy(x => x.Provider == game.Platform.PrimaryMetadataProvider ? 0 : 1).FirstOrDefault() ?? throw new InvalidOperationException("No metadata reference.");
        var data = await metadata.GetAsync(reference.Provider, reference.ExternalId, game.PlatformId, cancellationToken, refresh: true);
        game.Title = data.Title; game.SortTitle = data.Title; game.Description = data.Description; game.ReleaseYear = data.Year;
        game.ReleaseDateUtc = data.ReleaseDateUtc; game.ReleaseDatePrecision = data.ReleaseDatePrecision;
        game.Developer = data.Developer; game.Publisher = data.Publisher; game.Genres = data.Genres.ToList(); game.CoverRelativePath = data.CoverRelativePath;
        ReplaceTranslations(game, data);
        game.MetadataUpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:long}/metadata")]
    public async Task<IActionResult> AssignMetadata(long id, MetadataAssignment request, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.MetadataReferences).Include(x => x.MetadataTranslations).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var data = await metadata.GetAsync(request.Provider, request.ExternalId, game.PlatformId, cancellationToken);
        var reference = game.MetadataReferences.SingleOrDefault(x => x.Provider == request.Provider);
        if (reference is null) game.MetadataReferences.Add(new GameMetadataReference { Provider = request.Provider, ExternalId = data.ExternalId });
        else reference.ExternalId = data.ExternalId;
        game.Description = data.Description; game.ReleaseYear = data.Year; game.ReleaseDateUtc = data.ReleaseDateUtc;
        game.ReleaseDatePrecision = data.ReleaseDatePrecision; game.Developer = data.Developer; game.Publisher = data.Publisher;
        game.Genres = data.Genres.ToList(); game.CoverRelativePath = data.CoverRelativePath; game.MetadataUpdatedAtUtc = DateTime.UtcNow;
        ReplaceTranslations(game, data);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.RootFolder).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var tracked = await database.Downloads.Where(x => x.GameId == id).ToArrayAsync(cancellationToken);
        if (tracked.Any(x => x.Status is not (DownloadStatus.Imported or DownloadStatus.Failed) || x.CleanupPending)) return Conflict(new { code = "DownloadsStillTracked" });
        database.Downloads.RemoveRange(tracked);
        if (game.ManagedFolderRelativePath is { } managed)
        {
            var folder = SafePaths.Within(game.RootFolder.Path, managed);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        database.Games.Remove(game);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(new { filesRetained = true });
    }

    [HttpGet("{id:long}/search")]
    public async Task<IActionResult> Search(long id, CancellationToken cancellationToken)
    {
        var result = await search.SearchAsync(id, cancellationToken);
        return Ok(new { releases = result.Releases.Select(x => new { x.Release, x.Parsed, x.Decision, ticket = tickets.Store(id, x.Release) }), result.Failures, result.Truncated });
    }
    [HttpPost("{id:long}/grab")]
    public async Task<IActionResult> Grab(long id, GrabRequest request, CancellationToken cancellationToken)
    {
        var downloadId = await downloads.GrabAsync(id, tickets.Read(id, request.Ticket), false, cancellationToken);
        return downloadId is null ? Conflict(new { code = "DownloadAlreadyActive" }) : Accepted(new { downloadId });
    }

    private void ReplaceTranslations(Game game, MetadataGame data)
    {
        database.GameMetadataTranslations.RemoveRange(game.MetadataTranslations.Where(x => x.Provider == data.Provider));
        foreach (var old in game.MetadataTranslations.Where(x => x.Provider == data.Provider).ToArray()) game.MetadataTranslations.Remove(old);
        AddTranslations(game, data);
    }

    private static void AddTranslations(Game game, MetadataGame data)
    {
        var variants = data.Translations ?? [new MetadataTranslation("und", data.Title, data.Description, null, null)];
        foreach (var variant in variants.GroupBy(x => x.Language, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
            game.MetadataTranslations.Add(new GameMetadataTranslation { Provider = data.Provider, Language = variant.Language,
                Title = variant.Title, Description = variant.Description, TitleLanguage = variant.TitleLanguage,
                DescriptionLanguage = variant.DescriptionLanguage });
    }
}
