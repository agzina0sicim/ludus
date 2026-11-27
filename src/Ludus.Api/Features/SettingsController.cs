using System.ComponentModel.DataAnnotations;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record SettingsRequest([Required, RegularExpression("^(de|en)$")] string Language,
    [Required, RegularExpression("^[a-z]{2}(-[A-Z]{2})?$")] string MetadataLanguage, [Required, MaxLength(128)] string InstanceName,
    [Required, MaxLength(512)] string NamingSchema, bool UseCanonicalDatNames, bool PreferHardlinks, bool RenameFiles,
    bool CreateEmptyFolders, bool DeleteEmptyFolders, bool SkipFreeSpaceCheck, [Range(0, long.MaxValue)] long MinimumFreeSpaceBytes,
    [Required, RegularExpression("^0[0-7]{3}$")] string FilePermissions, [Required, RegularExpression("^0[0-7]{3}$")] string DirectoryPermissions,
    [MaxLength(128)] string? MediaGroup,
    [Required, MaxLength(4096)] string RecyclingBinPath, [Range(1, 3650)] int RecyclingRetentionDays, [Range(1, 1000)] int BackupRetentionCount,
    [Range(1, 1440)] int RssIntervalMinutes, [Range(0, 10080)] int MinimumAgeMinutes, [Range(0, long.MaxValue)] long MaximumIndexerSizeBytes,
    [Range(5, 3600)] int DownloadRefreshIntervalSeconds, bool EnableCompletedDownloadHandling, bool RedownloadFailed,
    bool RedownloadFailedFromInteractiveSearch, [Range(1, 8760)] int LibraryScanIntervalHours,
    [Range(1, 1440)] int HealthCheckIntervalMinutes, [Range(1, 8760)] int BackupIntervalHours, [Range(30, 3600)] int MediaLinkLifetimeSeconds,
    [Range(1, 1000000)] int MaximumArchiveEntries, [Range(1, 17592186044416)] long MaximumExtractedBytes,
    [Required, MaxLength(64)] string ShortDateFormat, [Required, MaxLength(128)] string LongDateFormat,
    bool Use12HourTime, ThemeMode Theme);

[ApiController, Route("api/v1/settings")]
public sealed class SettingsController(AppDbContext database, ReleaseParser parser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(ConfigurationSupport.View(await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken)));
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new
    {
        permissionsSupported = MediaPermissions.Supported,
        permissionPlatform = MediaPermissions.Supported ? "Linux" : "Unsupported",
        hardlinkFilePermissionsApplied = false
    });
    [HttpPut]
    public async Task<IActionResult> Update(SettingsRequest request, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(request.RecyclingBinPath)) return BadRequest(new { code = "AbsoluteRecyclingPathRequired" });
        SafePaths.RejectLinks(request.RecyclingBinPath);
        var sample = ImportPlanner.Render(request.NamingSchema, new Game { Title = "Game", SortTitle = "Game", ReleaseYear = 2000 }, parser.Parse("Game (Europe).nes"), "nes");
        SafePaths.Within(Path.GetFullPath(request.RecyclingBinPath), sample);
        var shortFormats = new[] { "dd.MM.yyyy", "yyyy-MM-dd", "MM/dd/yyyy" };
        var longFormats = new[] { "dddd, d. MMMM yyyy", "d. MMMM yyyy", "MMMM d, yyyy" };
        if (!shortFormats.Contains(request.ShortDateFormat, StringComparer.Ordinal)
            || !longFormats.Contains(request.LongDateFormat, StringComparer.Ordinal)
            || request.MediaGroup?.Any(x => char.IsControl(x) || x is ':' or '/') == true)
            return BadRequest(new { code = "InvalidDisplayOrPermissionSetting" });
        foreach (var root in await database.RootFolders.AsNoTracking().ToArrayAsync(cancellationToken))
            if (request.RecyclingBinPath == root.Path || SafePaths.IsWithin(root.Path, request.RecyclingBinPath) || SafePaths.IsWithin(request.RecyclingBinPath, root.Path))
                return BadRequest(new { code = "RecyclingOverlapsLibrary" });
        var settings = await database.AppSettings.SingleAsync(cancellationToken);
        ConfigurationSupport.Apply(request, settings);
        var rss = await database.ScheduledTasks.SingleAsync(x => x.Kind == TaskKind.RssSync, cancellationToken);
        rss.NextRunAtUtc = DateTime.UtcNow.AddMinutes(request.RssIntervalMinutes);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
}
