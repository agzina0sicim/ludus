using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ludus.Application.Metadata;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Metadata;

public sealed class IgdbProvider(AppDbContext database, MetadataHttp http, ISecretProtector secrets) : IMetadataProvider
{
    public MetadataProviderKind Kind => MetadataProviderKind.Igdb;
    private const string Fields = "fields name,summary,first_release_date,genres.name,platforms,cover.image_id,involved_companies.developer,involved_companies.publisher,involved_companies.company.name;";

    public async Task<IReadOnlyList<MetadataGame>> SearchAsync(string query, Platform platform, CancellationToken cancellationToken)
    {
        if (platform.IgdbPlatformId is not > 0) throw new MetadataException("PlatformMappingRequired");
        var escaped = query.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var data = await QueryAsync("games", $"search \"{escaped}\"; {Fields} where platforms = ({platform.IgdbPlatformId}); limit 20;", cancellationToken);
        return MetadataHttp.Items(data).Select(Parse).ToArray();
    }

    public async Task<MetadataGame> GetAsync(string externalId, Platform platform, CancellationToken cancellationToken)
    {
        if (!long.TryParse(externalId, out var id) || id <= 0) throw new ArgumentException("Invalid metadata ID.");
        var data = await QueryAsync("games", $"{Fields} where id = {id}; limit 1;", cancellationToken);
        var game = MetadataHttp.Items(data).Select(Parse).SingleOrDefault() ?? throw new KeyNotFoundException("Metadata game not found.");
        if (platform.IgdbPlatformId is not { } platformId || !game.PlatformIds.Contains(platformId)) throw new MetadataException("PlatformMismatch");
        return game;
    }

    public async Task<IReadOnlyList<MetadataSystem>> SystemsAsync(CancellationToken cancellationToken)
    {
        var result = new List<MetadataSystem>();
        for (var offset = 0; offset < 5000; offset += 500)
        {
            var data = await QueryAsync("platforms", $"fields name; sort id asc; limit 500; offset {offset};", cancellationToken);
            var page = MetadataHttp.Items(data).Select(x => new MetadataSystem(MetadataHttp.Number(x, "id"), MetadataHttp.Text(x, "name"))).ToArray();
            result.AddRange(page);
            if (page.Length < 500) return result;
        }
        throw new MetadataException("PlatformListTooLarge");
    }

    public async Task TestAsync(CancellationToken cancellationToken) => await QueryAsync("games", "fields id; limit 1;", cancellationToken);

    private async Task<JsonElement> QueryAsync(string endpoint, string query, CancellationToken cancellationToken)
    {
        var settings = await database.MetadataProviders.SingleAsync(x => x.Provider == Kind, cancellationToken);
        if (!settings.Enabled || settings.ClientId is null || settings.ClientSecret is null) throw new MetadataException("CredentialsRequired");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (settings.AccessToken is null || settings.AccessTokenExpiresAtUtc <= DateTime.UtcNow.AddMinutes(2))
            {
                using var auth = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = secrets.Unprotect(settings.ClientId),
                        ["client_secret"] = secrets.Unprotect(settings.ClientSecret), ["grant_type"] = "client_credentials" })
                };
                var token = await http.JsonAsync(auth, cancellationToken);
                var value = MetadataHttp.Text(token, "access_token");
                var expires = MetadataHttp.Number(token, "expires_in");
                if (value.Length == 0 || expires <= 0) throw new MetadataException("InvalidTokenResponse");
                settings.AccessToken = secrets.Protect(value);
                settings.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expires);
                await database.SaveChangesAsync(cancellationToken);
            }
            await Task.Delay(260, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint)
            { Content = new StringContent(query, Encoding.UTF8, "text/plain") };
            request.Headers.Add("Client-ID", secrets.Unprotect(settings.ClientId));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Unprotect(settings.AccessToken));
            try { return await http.JsonAsync(request, cancellationToken); }
            catch (MetadataException exception) when (exception.Code == "Http401" && attempt == 0) { settings.AccessToken = null; }
        }
        throw new MetadataException("AuthenticationFailed");
    }

    private static MetadataGame Parse(JsonElement game)
    {
        var companies = MetadataHttp.Items(MetadataHttp.Child(game, "involved_companies")).ToArray();
        string? Company(string role) => companies.Where(x => MetadataHttp.Text(x, role).Equals("True", StringComparison.OrdinalIgnoreCase))
            .Select(x => MetadataHttp.Text(MetadataHttp.Child(x, "company"), "name")).FirstOrDefault();
        var image = MetadataHttp.Text(MetadataHttp.Child(game, "cover"), "image_id");
        int? year = long.TryParse(MetadataHttp.Text(game, "first_release_date"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
            && unix is >= -2208988800 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(unix).Year : null;
        return new MetadataGame(MetadataProviderKind.Igdb, MetadataHttp.Text(game, "id"), MetadataHttp.Text(game, "name"), MetadataHttp.Text(game, "summary"), year,
            Company("developer"), Company("publisher"), MetadataHttp.Items(MetadataHttp.Child(game, "genres")).Select(x => MetadataHttp.Text(x, "name")).ToArray(),
            MetadataHttp.Items(MetadataHttp.Child(game, "platforms")).Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToArray(),
            RemoteCoverUrl: image.Length > 0 && image.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? $"https://images.igdb.com/igdb/image/upload/t_cover_big/{image}.jpg" : null);
    }
}
