using Ludus.Application.Downloads;
using Ludus.Domain.Common;
using Ludus.Domain.Configuration;
using Ludus.Domain.Operations;
using Ludus.Infrastructure.Notifications;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ludus.Infrastructure.Workers;

public sealed class AutomationWorker(IServiceScopeFactory scopes, ManualTaskQueue manual, ILogger<AutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var startup = scopes.CreateAsyncScope())
        {
            var database = startup.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.TaskRuns.Where(x => x.Status == TaskRunStatus.Running).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, TaskRunStatus.Interrupted).SetProperty(x => x.FinishedAtUtc, DateTime.UtcNow), stoppingToken);
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var database = services.GetRequiredService<AppDbContext>();
                var settings = await database.AppSettings.AsNoTracking().SingleAsync(stoppingToken);
                foreach (var task in services.GetServices<IAutomationTask>())
                {
                    var schedule = await database.ScheduledTasks.SingleAsync(x => x.Kind == task.Kind, stoppingToken);
                    var requested = manual.Take(task.Kind);
                    if (!requested && (!schedule.Enabled || schedule.NextRunAtUtc > DateTime.UtcNow)) continue;
                    var run = new TaskRun { ScheduledTaskId = schedule.Id, Status = TaskRunStatus.Running, StartedAtUtc = DateTime.UtcNow };
                    database.TaskRuns.Add(run);
                    schedule.LastStartedAtUtc = run.StartedAtUtc;
                    await database.SaveChangesAsync(stoppingToken);
                    var events = services.GetRequiredService<IRealtimePublisher>();
                    await events.PublishAsync("taskUpdated", new { task = task.Kind.ToString(), status = "Running" }, stoppingToken);
                    try
                    {
                        await task.RunAsync(stoppingToken);
                        run.Status = TaskRunStatus.Succeeded;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception exception)
                    {
                        run.Status = TaskRunStatus.Failed;
                        run.ErrorCode = exception.GetType().Name;
                        logger.LogWarning("Task {TaskKind} failed with {ErrorCode}.", task.Kind, run.ErrorCode);
                    }
                    run.FinishedAtUtc = DateTime.UtcNow;
                    schedule.LastCompletedAtUtc = run.FinishedAtUtc;
                    schedule.NextRunAtUtc = DateTime.UtcNow + Interval(task.Kind, settings);
                    await database.SaveChangesAsync(stoppingToken);
                    await events.PublishAsync("taskUpdated", new { task = task.Kind.ToString(), status = run.Status.ToString(), code = run.ErrorCode }, stoppingToken);
                }
                await services.GetRequiredService<WebhookService>().DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning("Automation cycle failed with {ErrorCode}.", exception.GetType().Name); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }

    private static TimeSpan Interval(TaskKind kind, AppSettings settings) => kind switch
    {
        TaskKind.RssSync => TimeSpan.FromMinutes(settings.RssIntervalMinutes),
        TaskKind.DownloadRefresh => TimeSpan.FromSeconds(settings.DownloadRefreshIntervalSeconds),
        TaskKind.RecyclingCleanup => TimeSpan.FromHours(24),
        TaskKind.LibraryScan => TimeSpan.FromHours(settings.LibraryScanIntervalHours),
        TaskKind.HealthCheck => TimeSpan.FromMinutes(settings.HealthCheckIntervalMinutes),
        TaskKind.Backup => TimeSpan.FromHours(settings.BackupIntervalHours),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
