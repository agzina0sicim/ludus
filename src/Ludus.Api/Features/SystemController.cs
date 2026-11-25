using System.ComponentModel.DataAnnotations;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Library;
using Ludus.Infrastructure.Operations;
using Ludus.Infrastructure.Persistence;
using Ludus.Infrastructure.Workers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record ScanRequest(long? RootFolderId, long? PlatformId);

[ApiController, Route("api/v1/system")]
public sealed class SystemController(AppDbContext database, LudusPaths paths, BackupService backups, DiagnosticsService diagnostics,
    HealthService health, LibraryScanner scanner, ManualTaskQueue tasks, IHostApplicationLifetime lifetime) : ControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> Info(CancellationToken cancellationToken) => Ok(new { version = typeof(Program).Assembly.GetName().Version!.ToString(),
        runtime = Environment.Version.ToString(), os = Environment.OSVersion.Platform.ToString(), startedAtUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
        schema = (await database.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault(), database = "SQLite", journalMode = "WAL" });
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken) => Ok((await database.HealthIssues.AsNoTracking().OrderByDescending(x => x.Severity).ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("health/check")]
    public async Task<IActionResult> Check(CancellationToken cancellationToken) => Ok((await health.CheckAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks(CancellationToken cancellationToken) => Ok(new
    {
        scheduled = (await database.ScheduledTasks.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View), pending = tasks.Pending,
        recent = (await database.TaskRuns.AsNoTracking().OrderByDescending(x => x.Id).Take(100).ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View)
    });
    [HttpPost("tasks/{kind}/run")]
    public IActionResult Run(TaskKind kind) => Accepted(new { queued = tasks.Enqueue(kind), kind });
    [HttpPut("tasks/{kind}")]
    public async Task<IActionResult> EnableTask(TaskKind kind, EnabledRequest request, CancellationToken cancellationToken)
    {
        var task = await database.ScheduledTasks.SingleOrDefaultAsync(x => x.Kind == kind, cancellationToken) ?? throw new KeyNotFoundException();
        task.Enabled = request.Enabled; await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpPost("library/scan")]
    public async Task<IActionResult> Scan(ScanRequest request, CancellationToken cancellationToken) => Ok(await scanner.ScanAsync(request.RootFolderId, request.PlatformId, cancellationToken));
    [HttpGet("backups")]
    public async Task<IActionResult> Backups(CancellationToken cancellationToken) => Ok((await database.Backups.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("backups")]
    public async Task<IActionResult> Backup(CancellationToken cancellationToken) => Ok(ConfigurationSupport.View(await backups.CreateAsync(false, cancellationToken)));
    [HttpPost("backups/import"), Consumes("application/zip"), RequestSizeLimit(9663676416)]
    public async Task<IActionResult> ImportBackup(CancellationToken cancellationToken) => Ok(ConfigurationSupport.View(await backups.ImportAsync(Request.Body, cancellationToken)));
    [HttpGet("backups/{id:long}/download")]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken)
    {
        var item = await database.Backups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
        Response.Headers.CacheControl = "no-store";
        return File(System.IO.File.OpenRead(SafePaths.Within(Path.Combine(paths.ConfigDirectory, "Backups"), item.RelativePath)), "application/zip", Path.GetFileName(item.RelativePath));
    }
    [HttpPost("backups/{id:long}/restore")]
    public async Task<IActionResult> Restore(long id, CancellationToken cancellationToken)
    {
        await backups.StageRestoreAsync(id, cancellationToken);
        Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
        return Accepted(new { restartRequired = true, message = "Validated backup will be applied on the next start." });
    }
    [HttpDelete("backups/{id:long}")]
    public async Task<IActionResult> DeleteBackup(long id, CancellationToken cancellationToken)
    {
        if (System.IO.File.Exists(Path.Combine(paths.ConfigDirectory, "restore-pending.json"))) return Conflict(new { code = "RestorePending" });
        var item = await database.Backups.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        System.IO.File.Delete(SafePaths.Within(Path.Combine(paths.ConfigDirectory, "Backups"), item.RelativePath));
        database.Backups.Remove(item); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] bool debug = false, [FromQuery, RegularExpression("^(INF|DBG|WRN|ERR|FTL)$")] string? level = null,
        [FromQuery, Range(1, 2000)] int limit = 500, CancellationToken cancellationToken = default) => Ok(await diagnostics.LogsAsync(debug, level, limit, cancellationToken));
    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return File(await diagnostics.ExportAsync(cancellationToken), "application/zip", $"ludus-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
    }
}
