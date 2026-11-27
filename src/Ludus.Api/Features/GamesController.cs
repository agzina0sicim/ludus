using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Ludus.Application.Indexers;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Downloads;
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
            .Select(x => new { x.Id, x.Title, x.ReleaseYear, x.PlatformId, platform = x.Platform.Name, x.RootFolderId, x.QualityProfileId, x.Monitored,
                x.Status, x.CoverRelativePath, x.Description, fileCount = x.Files.Count(f => !f.Missing), x.Version }).ToArrayAsync(cancellationToken);
        return Ok(new { page, pageSize, total, items });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var game = await database.Games.AsNoTracking().Include(x => x.Files).Include(x => x.MetadataReferences).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        return Ok(new { game.Id, game.Title, game.Description, game.ReleaseYear, game.Developer, game.Publisher, game.Genres, game.Serials, game.BaseTitleId,
            game.CoverRelativePath, game.Monitored, game.Status, game.PlatformId, game.RootFolderId, game.QualityProfileId, game.MetadataUpdatedAtUtc, game.Version,
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
            || !await database.QualityProfiles.AnyAsync(x => x.Id == request.QualityProfileId, cancellationToken)) return BadRequest(new { code = "InvalidGameAssignment" });
        var data = request.Provider is not null && request.ExternalId is not null ? await metadata.GetAsync(request.Provider.Value, request.ExternalId, request.PlatformId, cancellationToken) : null;
        var title = data?.Title ?? request.Title;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) return BadRequest(new { code = "TitleRequired" });
        if (await database.Games.AnyAsync(x => x.PlatformId == request.PlatformId && (x.Title == title || x.MetadataReferences.Any(r => r.Provider == request.Provider && r.ExternalId == request.ExternalId)), cancellationToken))
            return Conflict(new { code = "GameAlreadyExists" });
        var game = new Game { Title = title, SortTitle = title, PlatformId = request.PlatformId, RootFolderId = request.RootFolderId,
            QualityProfileId = request.QualityProfileId, Monitored = request.Monitored, BaseTitleId = request.BaseTitleId?.ToUpperInvariant(),
            Description = data?.Description, ReleaseYear = data?.Year, Developer = data?.Developer, Publisher = data?.Publisher,
            Genres = data?.Genres.ToList() ?? [], CoverRelativePath = data?.CoverRelativePath, MetadataUpdatedAtUtc = data is null ? null : DateTime.UtcNow };
        if (data is not null) game.MetadataReferences.Add(new GameMetadataReference { Provider = data.Provider, ExternalId = data.ExternalId });
        database.Games.Add(game);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = game.Id }, new { game.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, GameUpdate request, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.Files).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        if (request.RootFolderId != game.RootFolderId && game.Files.Count > 0) return Conflict(new { code = "MoveFilesBeforeChangingRoot" });
        game.Title = request.Title.Trim(); game.SortTitle = game.Title; game.Monitored = request.Monitored;
        game.RootFolderId = request.RootFolderId; game.QualityProfileId = request.QualityProfileId;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:long}/refresh-metadata")]
    public async Task<IActionResult> Refresh(long id, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.MetadataReferences).Include(x => x.Platform).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var reference = game.MetadataReferences.OrderBy(x => x.Provider == game.Platform.PrimaryMetadataProvider ? 0 : 1).FirstOrDefault() ?? throw new InvalidOperationException("No metadata reference.");
        var data = await metadata.GetAsync(reference.Provider, reference.ExternalId, game.PlatformId, cancellationToken, refresh: true);
        game.Title = data.Title; game.SortTitle = data.Title; game.Description = data.Description; game.ReleaseYear = data.Year;
        game.Developer = data.Developer; game.Publisher = data.Publisher; game.Genres = data.Genres.ToList(); game.CoverRelativePath = data.CoverRelativePath;
        game.MetadataUpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:long}/metadata")]
    public async Task<IActionResult> AssignMetadata(long id, MetadataAssignment request, CancellationToken cancellationToken)
    {
        var game = await database.Games.Include(x => x.MetadataReferences).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var data = await metadata.GetAsync(request.Provider, request.ExternalId, game.PlatformId, cancellationToken);
        var reference = game.MetadataReferences.SingleOrDefault(x => x.Provider == request.Provider);
        if (reference is null) game.MetadataReferences.Add(new GameMetadataReference { Provider = request.Provider, ExternalId = data.ExternalId });
        else reference.ExternalId = data.ExternalId;
        game.Description = data.Description; game.ReleaseYear = data.Year; game.Developer = data.Developer; game.Publisher = data.Publisher;
        game.Genres = data.Genres.ToList(); game.CoverRelativePath = data.CoverRelativePath; game.MetadataUpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var game = await database.Games.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var tracked = await database.Downloads.Where(x => x.GameId == id).ToArrayAsync(cancellationToken);
        if (tracked.Any(x => x.Status is not (DownloadStatus.Imported or DownloadStatus.Failed) || x.CleanupPending)) return Conflict(new { code = "DownloadsStillTracked" });
        database.Downloads.RemoveRange(tracked);
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
}
