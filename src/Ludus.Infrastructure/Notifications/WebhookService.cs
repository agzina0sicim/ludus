using System.Net.Http.Json;
using Ludus.Application.Downloads;
using Ludus.Domain.Activity;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Infrastructure.DownloadClients;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Notifications;

public sealed class WebhookService(IDbContextFactory<AppDbContext> databaseFactory, ClientHttp http)
{
    public static async Task EnqueueAsync(AppDbContext database, NotificationEvent eventType, string gameTitle, string? releaseTitle, CancellationToken cancellationToken)
    {
        var hooks = await database.Webhooks.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
        foreach (var hook in hooks.Where(x => x.Events.Contains(eventType)))
            database.WebhookDeliveries.Add(new WebhookDelivery { WebhookId = hook.Id, Event = eventType, GameTitle = gameTitle, ReleaseTitle = releaseTitle, NextAttemptAtUtc = DateTime.UtcNow });
    }

    public async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        await using var database = await databaseFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var pending = await database.WebhookDeliveries.Include(x => x.Webhook).Where(x => x.CompletedAtUtc == null && x.NextAttemptAtUtc <= now && x.Attempts < 10)
            .OrderBy(x => x.NextAttemptAtUtc).Take(50).ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            if (!item.Webhook.Enabled) continue;
            item.Attempts++;
            try
            {
                await SendAsync(item.Webhook, item.Event.ToString(), item.GameTitle, item.ReleaseTitle, cancellationToken);
                item.CompletedAtUtc = DateTime.UtcNow;
                item.ErrorCode = null;
            }
            catch (DownloadClientException exception)
            {
                item.ErrorCode = exception.Code;
                item.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, item.Attempts)));
            }
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    public Task TestAsync(Webhook webhook, CancellationToken cancellationToken) => SendAsync(webhook, "Test", "Ludus", "Connection test", cancellationToken);

    private async Task SendAsync(Webhook webhook, string eventName, string title, string? release, CancellationToken cancellationToken)
    {
        var url = http.Secret(webhook.Url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new DownloadClientException("InvalidWebhookUrl");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new
            {
                username = "Ludus", allowed_mentions = new { parse = Array.Empty<string>() },
                embeds = new[] { new { title = Limit($"{eventName}: {title}", 256), description = Limit(release ?? "", 2048), timestamp = DateTime.UtcNow } }
            })
        };
        if (webhook.Authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", http.Secret(webhook.Authorization));
        using var response = await http.SendAsync(request, true, cancellationToken);
        ClientHttp.RequireSuccess(response);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
