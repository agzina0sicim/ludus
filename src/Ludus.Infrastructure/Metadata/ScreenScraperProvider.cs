using System.Text.Json;
using Ludus.Application.Metadata;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Library;
using Ludus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ludus.Infrastructure.Metadata;

public sealed class ScreenScraperProvider(AppDbContext database, MetadataHttp http, ISecretProtector secrets) : IMetadataProvider
{
    public MetadataProviderKind Kind => MetadataProviderKind.ScreenScraper;
    public async Task<IReadOnlyList<MetadataGame>> SearchAsync(string query, Platform platform, CancellationToken cancellationToken)
    {
        if (platform.ScreenScraperSystemId is not > 0) throw new MetadataException("PlatformMappingRequired");
        var data = await CallAsync("jeuRecherche.php", new() { ["recherche"] = query, ["systemeid"] = platform.ScreenScraperSystemId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken);
        return MetadataHttp.Items(MetadataHttp.Child(data, "jeux")).Select(Parse).Where(x => x.PlatformIds.Contains(platform.ScreenScraperSystemId.Value)).ToArray();
    }

    public async Task<MetadataGame> GetAsync(string externalId, Platform platform, CancellationToken cancellationToken)
    {
        if (!long.TryParse(externalId, out var id) || id <= 0 || platform.ScreenScraperSystemId is not > 0) throw new ArgumentException("Invalid metadata or system ID.");
        var data = await CallAsync("jeuInfos.php", new() { ["gameid"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["systemeid"] = platform.ScreenScraperSystemId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken);
        var game = Parse(MetadataHttp.Child(data, "jeu"));
        if (game.ExternalId.Length == 0) throw new KeyNotFoundException("Metadata game not found.");
        if (!game.PlatformIds.Contains(platform.ScreenScraperSystemId.Value)) throw new MetadataException("PlatformMismatch");
        return game;
    }

    public async Task<IReadOnlyList<MetadataSystem>> SystemsAsync(CancellationToken cancellationToken)
    {
        var data = await CallAsync("systemesListe.php", new(), cancellationToken);
        return MetadataHttp.Items(MetadataHttp.Child(data, "systemes")).Select(x => new MetadataSystem(MetadataHttp.Number(x, "id"),
            MetadataHttp.Text(MetadataHttp.Child(x, "noms"), "nom_eu") is { Length: > 0 } name ? name : MetadataHttp.Text(x, "nom"))).ToArray();
    }

    public async Task TestAsync(CancellationToken cancellationToken) => await CallAsync("ssuserInfos.php", new(), cancellationToken);

    private async Task<JsonElement> CallAsync(string endpoint, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var settings = await database.MetadataProviders.SingleAsync(x => x.Provider == Kind, cancellationToken);
        if (!settings.Enabled || settings.ClientId is null || settings.ClientSecret is null) throw new MetadataException("CredentialsRequired");
        parameters["devid"] = secrets.Unprotect(settings.ClientId);
        parameters["devpassword"] = secrets.Unprotect(settings.ClientSecret);
        parameters["softname"] = "Ludus";
        parameters["output"] = "json";
        if (settings.Username is not null) parameters["ssid"] = secrets.Unprotect(settings.Username);
        if (settings.Password is not null) parameters["sspassword"] = secrets.Unprotect(settings.Password);
        var query = string.Join('&', parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.screenscraper.fr/api2/" + endpoint + "?" + query);
        var result = await http.JsonAsync(request, cancellationToken);
        var response = MetadataHttp.Child(result, "response");
        if (response.ValueKind != JsonValueKind.Object) throw new MetadataException("InvalidResponse");
        return response;
    }

    private static MetadataGame Parse(JsonElement game)
    {
        string Localized(string field, string attribute, params string[] preference)
        {
            var entries = MetadataHttp.Items(MetadataHttp.Child(game, field)).ToArray();
            return entries.OrderBy(x => Array.IndexOf(preference, MetadataHttp.Text(x, attribute)) is var rank && rank >= 0 ? rank : 100)
                .Select(x => MetadataHttp.Text(x, "text")).FirstOrDefault(x => x.Length > 0) ?? "";
        }
        var date = Localized("dates", "region", "eu", "us", "wor", "jp");
        var year = date.Length >= 4 && int.TryParse(date[..4], out var parsed) && parsed is >= 1900 and <= 9999 ? (int?)parsed : null;
        var cover = MetadataHttp.Items(MetadataHttp.Child(game, "medias")).Where(x => MetadataHttp.Text(x, "type") is "box-2D" or "box-3D" or "sstitle")
            .OrderBy(x => MetadataHttp.Text(x, "type") == "box-2D" ? 0 : 1).ThenBy(x => MetadataHttp.Text(x, "region") == "eu" ? 0 : 1)
            .Select(x => MetadataHttp.Text(x, "url")).FirstOrDefault();
        return new MetadataGame(MetadataProviderKind.ScreenScraper, MetadataHttp.Text(game, "id"),
            Localized("noms", "region", "eu", "wor", "us", "ss", "jp") is { Length: > 0 } title ? title : MetadataHttp.Text(game, "nom"),
            Localized("synopsis", "langue", "de", "en", "fr"), year, MetadataHttp.Text(game, "developpeur"), MetadataHttp.Text(game, "editeur"),
            MetadataHttp.Items(MetadataHttp.Child(game, "genres")).SelectMany(x => MetadataHttp.Child(x, "noms").ValueKind == JsonValueKind.Array
                ? MetadataHttp.Items(MetadataHttp.Child(x, "noms")) : new[] { x }).Where(x => MetadataHttp.Text(x, "langue") is "en" or "de")
                .Select(x => MetadataHttp.Text(x, "text")).Where(x => x.Length > 0).Distinct().ToArray(), [MetadataHttp.Number(MetadataHttp.Child(game, "systeme"), "id")], RemoteCoverUrl: cover);
    }
}
