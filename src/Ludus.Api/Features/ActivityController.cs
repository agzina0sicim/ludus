using System.ComponentModel.DataAnnotations;
using Ludus.Application.Downloads;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Infrastructure.Downloads;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

[ApiController, Route("api/v1/activity")]
public sealed class ActivityController(AppDbContext database, IEnumerable<IDownloadClientAdapter> clients, DownloadGate gate) : ControllerBase
{
    [HttpGet("queue")]
    public async Task<IActionResult> Queue(CancellationToken cancellationToken) => Ok(await database.Downloads.AsNoTracking()
        .Where(x => x.Status != DownloadStatus.Imported && x.Status != DownloadStatus.Failed || x.CleanupPending || x.ReplacementSearchPending)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.GameId, x.DownloadClientId, x.DownloadId, x.ReleaseTitle, x.Status, x.Score, x.Size,
            x.DownloadedBytes, x.SpeedBytesPerSecond, x.EtaSeconds, x.FailureCode, x.ImportAttempts, x.CleanupPending, x.ReplacementSearchPending }).ToArrayAsync(cancellationToken));
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] long? gameId, [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var query = database.History.AsNoTracking().Where(x => gameId == null || x.GameId == gameId);
        return Ok(new { total = await query.CountAsync(cancellationToken), page, pageSize,
            items = await query.OrderByDescending(x => x.Id).Skip(checked((page - 1) * pageSize)).Take(pageSize)
                .Select(x => new { x.Id, x.GameId, x.DownloadId, x.GameTitle, x.ReleaseTitle, x.Event, x.ContentKind, x.Score, x.Message, x.CreatedAtUtc }).ToArrayAsync(cancellationToken) });
    }
    [HttpGet("blocklist")]
    public async Task<IActionResult> Blocks(CancellationToken cancellationToken) => Ok((await database.ReleaseBlocks.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpDelete("blocklist/{id:long}")]
    public async Task<IActionResult> RemoveBlock(long id, CancellationToken cancellationToken)
    {
        var block = await database.ReleaseBlocks.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        database.ReleaseBlocks.Remove(block); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("queue/{id:long}")]
    public async Task<IActionResult> RemoveDownload(long id, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var item = await database.Downloads.Include(x => x.DownloadClient).Include(x => x.Game).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
            if (item.Status == DownloadStatus.Importing) return Conflict(new { code = "ImportInProgress" });
            await clients.Single(x => x.Kind == item.DownloadClient.Kind).RemoveAsync(item.DownloadClient, item.DownloadId, false, cancellationToken);
            item.Status = DownloadStatus.Failed; item.FailureCode = "RemovedByUser"; item.CleanupPending = item.ReplacementSearchPending = false;
            item.Game.Status = await database.GameFiles.AnyAsync(x => x.GameId == item.GameId && !x.Missing && x.ContentKind == ContentKind.Base, cancellationToken) ? GameStatus.Available : GameStatus.Missing;
            database.History.Add(new HistoryEntry { GameId = item.GameId, GameTitle = item.Game.Title, ReleaseTitle = item.ReleaseTitle, DownloadId = item.Id,
                Event = HistoryEvent.Deleted, Message = "Download removed by user; data retained." });
            await database.SaveChangesAsync(cancellationToken); return NoContent();
        }
        finally { gate.Semaphore.Release(); }
    }
}
