using System.ComponentModel.DataAnnotations;
using Ludus.Application.Downloads;
using Ludus.Application.Indexers;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Notifications;
using Ludus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Api.Features;

public sealed record IndexerRequest([Required, MaxLength(128)] string Name, [Required, MaxLength(2048)] string BaseUrl, [MaxLength(4096)] string? ApiKey,
    IndexerKind Kind, DownloadProtocol Protocol, bool EnableRss, bool EnableAutomaticSearch, bool EnableInteractiveSearch,
    [Range(1, 100)] int Priority, [Range(0, 1000000)] int MinimumSeeders, [Required, MaxLength(100)] List<int> Categories);
public sealed record ClientRequest([Required, MaxLength(128)] string Name, DownloadClientKind Kind, [Required, MaxLength(2048)] string BaseUrl,
    [MaxLength(4096)] string? Username, [MaxLength(4096)] string? Password, [MaxLength(4096)] string? ApiKey,
    bool Enabled, [Range(1, 100)] int Priority, [MaxLength(64), RegularExpression("^[a-zA-Z0-9_-]*$")] string? PostImportTag, bool RemoveCompleted, bool RemoveFailed);
public sealed record MappingRequest([Required, MaxLength(253)] string Host, [Required, MaxLength(4096)] string RemotePath, [Required, MaxLength(4096)] string LocalPath);
public sealed record ProviderRequest(bool Enabled, [MaxLength(4096)] string? ClientId, [MaxLength(4096)] string? ClientSecret,
    [MaxLength(4096)] string? Username, [MaxLength(4096)] string? Password, [Range(1, 8760)] int CacheDurationHours);
public sealed record WebhookRequest([Required, MaxLength(128)] string Name, WebhookKind Kind,
    [MaxLength(4096)] string? Url, [MaxLength(4096)] string? Authorization, [MaxLength(4096)] string? Token,
    [MaxLength(512)] string? Target, bool Enabled, [Required, MinLength(1), MaxLength(5)] List<NotificationEvent> Events);

[ApiController, Route("api/v1")]
public sealed class IntegrationsController(AppDbContext database, ISecretProtector secrets, IIndexerClient indexers,
    IEnumerable<IDownloadClientAdapter> clients, WebhookService webhooks) : ControllerBase
{
    [HttpGet("indexers")]
    public async Task<IActionResult> Indexers(CancellationToken cancellationToken) => Ok((await database.Indexers.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("indexers")]
    public async Task<IActionResult> AddIndexer(IndexerRequest request, CancellationToken cancellationToken)
    {
        var item = new Indexer { Name = request.Name, BaseUrl = request.BaseUrl }; Apply(request, item); database.Indexers.Add(item);
        await database.SaveChangesAsync(cancellationToken); return Created($"/api/v1/indexers/{item.Id}", ConfigurationSupport.View(item));
    }
    [HttpPut("indexers/{id:long}")]
    public async Task<IActionResult> UpdateIndexer(long id, IndexerRequest request, CancellationToken cancellationToken)
    {
        var item = await database.Indexers.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); Apply(request, item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("indexers/{id:long}")]
    public async Task<IActionResult> DeleteIndexer(long id, CancellationToken cancellationToken)
    {
        var item = await database.Indexers.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); database.Indexers.Remove(item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpPost("indexers/{id:long}/test")]
    public async Task<IActionResult> TestIndexer(long id, CancellationToken cancellationToken)
    {
        var item = await database.Indexers.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        return Ok(await indexers.GetCapabilitiesAsync(item, cancellationToken));
    }
    [HttpGet("download-clients")]
    public async Task<IActionResult> Clients(CancellationToken cancellationToken) => Ok((await database.DownloadClients.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("download-clients")]
    public async Task<IActionResult> AddClient(ClientRequest request, CancellationToken cancellationToken)
    {
        var item = new DownloadClient { Name = request.Name, BaseUrl = request.BaseUrl }; Apply(request, item); database.DownloadClients.Add(item);
        await database.SaveChangesAsync(cancellationToken); return Created($"/api/v1/download-clients/{item.Id}", ConfigurationSupport.View(item));
    }
    [HttpPut("download-clients/{id:long}")]
    public async Task<IActionResult> UpdateClient(long id, ClientRequest request, CancellationToken cancellationToken)
    {
        var item = await database.DownloadClients.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        if (item.Kind != request.Kind && await database.Downloads.AnyAsync(x => x.DownloadClientId == id, cancellationToken)) return Conflict(new { code = "ClientHasTrackedDownloads" });
        Apply(request, item); await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("download-clients/{id:long}")]
    public async Task<IActionResult> DeleteClient(long id, CancellationToken cancellationToken)
    {
        var item = await database.DownloadClients.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); database.DownloadClients.Remove(item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpPost("download-clients/{id:long}/test")]
    public async Task<IActionResult> TestClient(long id, CancellationToken cancellationToken)
    {
        var item = await database.DownloadClients.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        var queue = await clients.Single(x => x.Kind == item.Kind).GetDownloadsAsync(item, cancellationToken); return Ok(new { success = true, trackedCategoryJobs = queue.Count });
    }
    [HttpGet("remote-path-mappings")]
    public async Task<IActionResult> Mappings(CancellationToken cancellationToken) => Ok((await database.RemotePathMappings.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("remote-path-mappings")]
    public async Task<IActionResult> AddMapping(MappingRequest request, CancellationToken cancellationToken)
    {
        var item = new RemotePathMapping { Host = request.Host, LocalPath = request.LocalPath, RemotePath = request.RemotePath }; Apply(request, item);
        database.RemotePathMappings.Add(item); await database.SaveChangesAsync(cancellationToken); return Created($"/api/v1/remote-path-mappings/{item.Id}", ConfigurationSupport.View(item));
    }
    [HttpPut("remote-path-mappings/{id:long}")]
    public async Task<IActionResult> UpdateMapping(long id, MappingRequest request, CancellationToken cancellationToken)
    {
        var item = await database.RemotePathMappings.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); Apply(request, item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("remote-path-mappings/{id:long}")]
    public async Task<IActionResult> DeleteMapping(long id, CancellationToken cancellationToken)
    {
        var item = await database.RemotePathMappings.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); database.RemotePathMappings.Remove(item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpGet("metadata/providers")]
    public async Task<IActionResult> Providers(CancellationToken cancellationToken) => Ok((await database.MetadataProviders.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPut("metadata/providers/{provider}")]
    public async Task<IActionResult> UpdateProvider(MetadataProviderKind provider, ProviderRequest request, CancellationToken cancellationToken)
    {
        var item = await database.MetadataProviders.SingleOrDefaultAsync(x => x.Provider == provider, cancellationToken) ?? throw new KeyNotFoundException();
        ConfigurationSupport.Apply(request, item);
        item.ClientId = ConfigurationSupport.Secret(request.ClientId, item.ClientId, secrets); item.ClientSecret = ConfigurationSupport.Secret(request.ClientSecret, item.ClientSecret, secrets);
        item.Username = ConfigurationSupport.Secret(request.Username, item.Username, secrets); item.Password = ConfigurationSupport.Secret(request.Password, item.Password, secrets);
        if (request.ClientId is not null || request.ClientSecret is not null) { item.AccessToken = null; item.AccessTokenExpiresAtUtc = null; }
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.MetadataCache.Where(x => x.Provider == provider).ExecuteDeleteAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return NoContent();
    }
    [HttpGet("webhooks")]
    public async Task<IActionResult> Webhooks(CancellationToken cancellationToken) => Ok((await database.Webhooks.AsNoTracking().ToArrayAsync(cancellationToken)).Select(ConfigurationSupport.View));
    [HttpPost("webhooks")]
    public async Task<IActionResult> AddWebhook(WebhookRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url)) return BadRequest(new { code = "WebhookUrlRequired" });
        var item = new Webhook { Name = request.Name, Url = secrets.Protect(request.Url) }; Apply(request, item); database.Webhooks.Add(item);
        await database.SaveChangesAsync(cancellationToken); return Created($"/api/v1/webhooks/{item.Id}", ConfigurationSupport.View(item));
    }
    [HttpPut("webhooks/{id:long}")]
    public async Task<IActionResult> UpdateWebhook(long id, WebhookRequest request, CancellationToken cancellationToken)
    {
        var item = await database.Webhooks.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); Apply(request, item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpDelete("webhooks/{id:long}")]
    public async Task<IActionResult> DeleteWebhook(long id, CancellationToken cancellationToken)
    {
        var item = await database.Webhooks.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); database.Webhooks.Remove(item);
        await database.SaveChangesAsync(cancellationToken); return NoContent();
    }
    [HttpPost("webhooks/{id:long}/test")]
    public async Task<IActionResult> TestWebhook(long id, CancellationToken cancellationToken)
    {
        var item = await database.Webhooks.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException(); await webhooks.TestAsync(item, cancellationToken); return Ok(new { success = true });
    }

    private void Apply(IndexerRequest request, Indexer item)
    {
        if (request.Categories.Any(x => x <= 0) || request.Kind == IndexerKind.Newznab && request.Protocol != DownloadProtocol.Usenet) throw new ArgumentException("Invalid indexer categories or protocol.");
        ConfigurationSupport.Apply(request, item); item.BaseUrl = ConfigurationSupport.BaseUrl(request.BaseUrl);
        item.ApiKey = ConfigurationSupport.Secret(request.ApiKey, item.ApiKey, secrets); item.CapabilitiesXml = null; item.CapabilitiesUpdatedAtUtc = null;
    }
    private void Apply(ClientRequest request, DownloadClient item)
    {
        ConfigurationSupport.Apply(request, item); item.BaseUrl = ConfigurationSupport.BaseUrl(request.BaseUrl);
        item.Username = ConfigurationSupport.Secret(request.Username, item.Username, secrets); item.Password = ConfigurationSupport.Secret(request.Password, item.Password, secrets);
        item.ApiKey = ConfigurationSupport.Secret(request.ApiKey, item.ApiKey, secrets);
    }
    private static void Apply(MappingRequest request, RemotePathMapping item)
    {
        if (Uri.CheckHostName(request.Host) == UriHostNameType.Unknown || !Path.IsPathFullyQualified(request.LocalPath)
            || !(request.RemotePath.StartsWith('/') || request.RemotePath.Length >= 3 && char.IsAsciiLetter(request.RemotePath[0]) && request.RemotePath[1] == ':'))
            throw new ArgumentException("Mappings require a host and absolute paths.");
        SafePaths.RejectLinks(request.LocalPath); ConfigurationSupport.Apply(request, item);
        item.LocalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.LocalPath));
    }
    private void Apply(WebhookRequest request, Webhook item)
    {
        if (request.Url is not null && (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)) throw new ArgumentException("Invalid webhook URL.");
        ConfigurationSupport.Apply(request, item);
        item.Url = ConfigurationSupport.Secret(request.Url, item.Url, secrets) ?? throw new ArgumentException("Webhook URL required.");
        item.Authorization = ConfigurationSupport.Secret(request.Authorization, item.Authorization, secrets);
        item.Token = ConfigurationSupport.Secret(request.Token, item.Token, secrets);
        item.Events = item.Events.Distinct().ToList();
        if (request.Kind is WebhookKind.Gotify or WebhookKind.Telegram && item.Token is null)
            throw new ArgumentException("A token is required for this connection type.");
        if (request.Kind == WebhookKind.Telegram && string.IsNullOrWhiteSpace(request.Target))
            throw new ArgumentException("A Telegram chat ID is required.");
    }
}
