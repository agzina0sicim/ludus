using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ludus.Application.Downloads;
using Ludus.Application.Metadata;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Configuration;
using Ludus.Infrastructure.DownloadClients;
using Ludus.Infrastructure.Imports;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ludus.Infrastructure.Metadata;

public sealed class MetadataService(AppDbContext database, IEnumerable<IMetadataProvider> providers, MetadataGate gate,
    ClientHttp http, LudusPaths paths, ReleaseParser parser, ILogger<MetadataService> logger)
{
    public async Task<IReadOnlyList<MetadataGame>> SearchAsync(string query, long platformId, MetadataProviderKind? selected, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || query.Any(char.IsControl)) throw new ArgumentException("Search must contain 1 to 200 characters.");
        var platform = await database.Platforms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == platformId, cancellationToken) ?? throw new KeyNotFoundException();
        query = parser.Parse(query).Title;
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A game title is required after release-tag cleanup.");
        var primary = selected ?? platform.PrimaryMetadataProvider;
        MetadataProviderKind[] order = selected is null && platform.MetadataFallbackEnabled ? [primary, primary == MetadataProviderKind.Igdb ? MetadataProviderKind.ScreenScraper : MetadataProviderKind.Igdb] : [primary];
        MetadataException? failure = null;
        var successful = false;
        foreach (var provider in order)
        {
            try
            {
                var games = await CachedAsync(provider, $"search:{platform.Id}:{query}", p => p.SearchAsync(query, platform, cancellationToken), cancellationToken);
                successful = true;
                if (games.Count > 0) return games;
            }
            catch (MetadataException exception) { failure = exception; }
        }
        if (!successful && failure is not null) throw failure;
        return [];
    }

    public async Task<MetadataGame> GetAsync(MetadataProviderKind provider, string id, long platformId, CancellationToken cancellationToken, bool refresh = false)
    {
        var platform = await database.Platforms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == platformId, cancellationToken) ?? throw new KeyNotFoundException();
        return (await CachedAsync(provider, $"game:{platformId}:{id}", async p => new[] { await p.GetAsync(id, platform, cancellationToken) }, cancellationToken, refresh)).Single();
    }

    public async Task<IReadOnlyList<MetadataSystem>> SystemsAsync(MetadataProviderKind provider, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try { return await Provider(provider).SystemsAsync(cancellationToken); }
        finally { gate.Semaphore.Release(); }
    }

    public async Task TestAsync(MetadataProviderKind provider, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try { await Provider(provider).TestAsync(cancellationToken); }
        finally { gate.Semaphore.Release(); }
    }

    private IMetadataProvider Provider(MetadataProviderKind provider) => providers.Single(x => x.Kind == provider);

    private async Task<IReadOnlyList<MetadataGame>> CachedAsync(MetadataProviderKind provider, string key,
        Func<IMetadataProvider, Task<IReadOnlyList<MetadataGame>>> fetch, CancellationToken cancellationToken, bool refresh = false)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var settings = await database.MetadataProviders.SingleAsync(x => x.Provider == provider, cancellationToken);
            if (!settings.Enabled) throw new MetadataException("ProviderDisabled");
            key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            var cached = await database.MetadataCache.SingleOrDefaultAsync(x => x.Provider == provider && x.CacheKey == key, cancellationToken);
            if (!refresh && cached?.ExpiresAtUtc > DateTime.UtcNow) return JsonSerializer.Deserialize<MetadataGame[]>(cached.PayloadJson) ?? [];
            var games = await fetch(Provider(provider));
            var result = new List<MetadataGame>();
            foreach (var game in games)
            {
                string? cover = null;
                if (game.RemoteCoverUrl is not null)
                {
                    try { cover = await CacheCoverAsync(game.RemoteCoverUrl, cancellationToken); }
                    catch (DownloadClientException exception) { logger.LogWarning("Metadata cover unavailable for {Provider} game {GameId}: {Code}.", game.Provider, game.ExternalId, exception.Code); }
                    catch (MetadataException exception) { logger.LogWarning("Metadata cover rejected for {Provider} game {GameId}: {Code}.", game.Provider, game.ExternalId, exception.Code); }
                }
                result.Add(game with { CoverRelativePath = cover, RemoteCoverUrl = null });
            }
            if (cached is null)
            {
                cached = new MetadataCacheEntry { Provider = provider, CacheKey = key, PayloadJson = "[]" };
                database.MetadataCache.Add(cached);
            }
            cached.PayloadJson = JsonSerializer.Serialize(result);
            cached.ExpiresAtUtc = DateTime.UtcNow.AddHours(settings.CacheDurationHours);
            await database.SaveChangesAsync(cancellationToken);
            return result;
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<string> CacheCoverAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || !uri.IsDefaultPort
            || !(uri.Host == "images.igdb.com" || uri.Host == "screenscraper.fr" || uri.Host.EndsWith(".screenscraper.fr", StringComparison.OrdinalIgnoreCase)))
            throw new MetadataException("UntrustedMediaUrl");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await http.SendAsync(request, false, cancellationToken);
        ClientHttp.RequireSuccess(response);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 10 * 1024 * 1024) throw new MetadataException("CoverTooLarge");
        var extension = bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) ? ".png"
            : bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ? ".jpg"
            : bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? ".webp" : null;
        if (extension is null) throw new MetadataException("UnsupportedCoverFormat");
        var relative = Convert.ToHexStringLower(SHA256.HashData(bytes)) + extension;
        var target = SafePaths.Within(paths.MediaDirectory, relative);
        if (!File.Exists(target))
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".partial";
            try { await File.WriteAllBytesAsync(temporary, bytes, cancellationToken); File.Move(temporary, target); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return relative;
    }
}
