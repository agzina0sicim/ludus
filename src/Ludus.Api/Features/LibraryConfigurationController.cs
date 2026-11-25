using System.ComponentModel.DataAnnotations;
using Ludus.Application.Verification;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record RootRequest([Required, MaxLength(128)] string Name, [Required, MaxLength(4096)] string Path);
public sealed record PlatformRequest([Required, MaxLength(128)] string Name, [Required, RegularExpression("^[a-z0-9-]{1,64}$")] string Slug,
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$")] string AccentColor, bool Monitored, bool SupportsFolderGames, bool ExtractArchives,
    MetadataProviderKind PrimaryMetadataProvider, bool MetadataFallbackEnabled, [Range(1, int.MaxValue)] int? IgdbPlatformId,
    [Range(1, int.MaxValue)] int? ScreenScraperSystemId, [Required, MinLength(1), MaxLength(100)] List<string> AllowedExtensions,
    [Required, MaxLength(100)] List<int> TorznabCategories);
public sealed record MonitoringRequest(bool Monitored);

[ApiController, Route("api/v1")]
public sealed class LibraryConfigurationController(AppDbContext database, LudusPaths paths, IDatImporter dat) : ControllerBase
{
    [HttpGet("root-folders")]
    public async Task<IActionResult> Roots(CancellationToken cancellationToken) => Ok((await database.RootFolders.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("root-folders")]
    public async Task<IActionResult> AddRoot(RootRequest request, CancellationToken cancellationToken)
    {
        var root = new RootFolder { Name = request.Name.Trim(), Path = ValidateRoot(request.Path) };
        database.RootFolders.Add(root); await database.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/root-folders/{root.Id}", ConfigurationSupport.View(root));
    }
    [HttpPut("root-folders/{id:long}")]
    public async Task<IActionResult> UpdateRoot(long id, RootRequest request, CancellationToken cancellationToken)
    {
        var root = await database.RootFolders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        var path = ValidateRoot(request.Path);
        if (path != root.Path && await database.GameFiles.AnyAsync(x => x.Game.RootFolderId == id, cancellationToken)) return Conflict(new { code = "RootContainsFiles" });
        root.Name = request.Name.Trim(); root.Path = path;
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("root-folders/{id:long}")]
    public async Task<IActionResult> DeleteRoot(long id, CancellationToken cancellationToken)
    {
        var root = await database.RootFolders.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        database.RootFolders.Remove(root); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpGet("platforms")]
    public async Task<IActionResult> Platforms(CancellationToken cancellationToken) => Ok((await database.Platforms.AsNoTracking().OrderBy(x => x.Name).ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("platforms")]
    public async Task<IActionResult> AddPlatform(PlatformRequest request, CancellationToken cancellationToken)
    {
        var platform = new Platform { Name = request.Name, Slug = request.Slug };
        ApplyPlatform(request, platform); database.Platforms.Add(platform); await database.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/platforms/{platform.Id}", ConfigurationSupport.View(platform));
    }
    [HttpPut("platforms/{id:long}")]
    public async Task<IActionResult> UpdatePlatform(long id, PlatformRequest request, CancellationToken cancellationToken)
    {
        var platform = await database.Platforms.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        ApplyPlatform(request, platform); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("platforms/{id:long}")]
    public async Task<IActionResult> DeletePlatform(long id, CancellationToken cancellationToken)
    {
        var platform = await database.Platforms.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        database.Platforms.Remove(platform); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpPut("platforms/{id:long}/monitoring")]
    public async Task<IActionResult> Monitoring(long id, MonitoringRequest request, CancellationToken cancellationToken)
    {
        var platform = await database.Platforms.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        platform.Monitored = request.Monitored;
        var games = await database.Games.Where(x => x.PlatformId == id).ToArrayAsync(cancellationToken);
        foreach (var game in games) game.Monitored = request.Monitored;
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpGet("dat-files")]
    public async Task<IActionResult> Dats(CancellationToken cancellationToken) => Ok(await database.DatFiles.AsNoTracking()
        .Select(x => new { x.Id, x.PlatformId, x.Name, x.FileName, x.Sha256, x.Description, x.DatVersion, x.Author, x.Enabled, x.ImportedAtUtc, games = x.Games.Count, roms = x.Games.Sum(g => g.Roms.Count) }).ToArrayAsync(cancellationToken));
    [HttpPost("dat-files"), RequestSizeLimit(268435456), RequestFormLimits(MultipartBodyLengthLimit = 268435456)]
    public async Task<IActionResult> UploadDat([FromForm] long platformId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || !Path.GetExtension(file.FileName).Equals(".dat", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { code = "DatFileRequired" });
        await using var input = file.OpenReadStream();
        return Ok(await dat.ImportAsync(platformId, Path.GetFileName(file.FileName), input, cancellationToken));
    }
    [HttpPut("dat-files/{id:long}")]
    public async Task<IActionResult> EnableDat(long id, EnabledRequest request, CancellationToken cancellationToken)
    {
        var item = await database.DatFiles.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        item.Enabled = request.Enabled; await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("dat-files/{id:long}")]
    public async Task<IActionResult> DeleteDat(long id, CancellationToken cancellationToken)
    {
        var item = await database.DatFiles.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        database.DatFiles.Remove(item); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    private string ValidateRoot(string value)
    {
        if (!Path.IsPathFullyQualified(value) || !Directory.Exists(value)) throw new ArgumentException("Root folder must be an existing absolute directory.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)); SafePaths.RejectLinks(path);
        if (path == paths.ConfigDirectory || SafePaths.IsWithin(paths.ConfigDirectory, path) || SafePaths.IsWithin(path, paths.ConfigDirectory)) throw new ArgumentException("Root folder overlaps configuration.");
        return path;
    }
    private static void ApplyPlatform(PlatformRequest request, Platform platform)
    {
        if (request.AllowedExtensions.Any(x => x.Length is < 2 or > 16 || x[0] != '.' || !x[1..].All(char.IsAsciiLetterOrDigit))
            || request.TorznabCategories.Any(x => x <= 0)) throw new ArgumentException("Invalid platform extensions or categories.");
        ConfigurationSupport.Apply(request, platform);
        platform.AllowedExtensions = request.AllowedExtensions.Select(x => x.ToLowerInvariant()).Distinct().ToList();
        platform.TorznabCategories = request.TorznabCategories.Distinct().ToList();
    }
}
public sealed record EnabledRequest(bool Enabled);
