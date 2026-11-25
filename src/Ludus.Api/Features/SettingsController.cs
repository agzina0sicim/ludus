using System.ComponentModel.DataAnnotations;
using Ludus.Application.Releases;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record SettingsRequest([Required, RegularExpression("^(de|en)$")] string Language, [Required, MaxLength(128)] string InstanceName,
    [Required, MaxLength(512)] string NamingSchema, bool UseCanonicalDatNames, bool PreferHardlinks, bool RenameFiles,
    [Required, MaxLength(4096)] string RecyclingBinPath, [Range(1, 3650)] int RecyclingRetentionDays, [Range(1, 1000)] int BackupRetentionCount,
    [Range(1, 1440)] int RssIntervalMinutes, [Range(5, 3600)] int DownloadRefreshIntervalSeconds, [Range(1, 8760)] int LibraryScanIntervalHours,
    [Range(1, 1440)] int HealthCheckIntervalMinutes, [Range(1, 8760)] int BackupIntervalHours, [Range(30, 3600)] int MediaLinkLifetimeSeconds,
    [Range(1, 1000000)] int MaximumArchiveEntries, [Range(1, 17592186044416)] long MaximumExtractedBytes);

[ApiController, Route("api/v1/settings")]
public sealed class SettingsController(AppDbContext database, ReleaseParser parser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(ConfigurationSupport.View(await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken)));
    [HttpPut]
    public async Task<IActionResult> Update(SettingsRequest request, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(request.RecyclingBinPath)) return BadRequest(new { code = "AbsoluteRecyclingPathRequired" });
        SafePaths.RejectLinks(request.RecyclingBinPath);
        var sample = ImportPlanner.Render(request.NamingSchema, new Game { Title = "Game", SortTitle = "Game", ReleaseYear = 2000 }, parser.Parse("Game (Europe).nes"), "nes");
        SafePaths.Within(Path.GetFullPath(request.RecyclingBinPath), sample);
        foreach (var root in await database.RootFolders.AsNoTracking().ToArrayAsync(cancellationToken))
            if (request.RecyclingBinPath == root.Path || SafePaths.IsWithin(root.Path, request.RecyclingBinPath) || SafePaths.IsWithin(request.RecyclingBinPath, root.Path))
                return BadRequest(new { code = "RecyclingOverlapsLibrary" });
        var settings = await database.AppSettings.SingleAsync(cancellationToken);
        ConfigurationSupport.Apply(request, settings);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
}
