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
        var language = await PreferredLanguageAsync(cancellationToken);
        return MetadataHttp.Items(MetadataHttp.Child(data, "jeux")).Select(x => Parse(x, language)).Where(x => x.PlatformIds.Contains(platform.ScreenScraperSystemId.Value)).ToArray();
    }

    public async Task<MetadataGame> GetAsync(string externalId, Platform platform, CancellationToken cancellationToken)
    {
        if (!long.TryParse(externalId, out var id) || id <= 0 || platform.ScreenScraperSystemId is not > 0) throw new ArgumentException("Invalid metadata or system ID.");
        var data = await CallAsync("jeuInfos.php", new() { ["gameid"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["systemeid"] = platform.ScreenScraperSystemId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken);
        var game = Parse(MetadataHttp.Child(data, "jeu"), await PreferredLanguageAsync(cancellationToken));
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

    private async Task<string> PreferredLanguageAsync(CancellationToken cancellationToken) =>
        (await database.AppSettings.AsNoTracking().SingleAsync(cancellationToken)).MetadataLanguage;

    private static MetadataGame Parse(JsonElement game, string preferredLanguage)
    {
        string Localized(string field, string attribute, params string[] preference)
        {
            var entries = MetadataHttp.Items(MetadataHttp.Child(game, field)).ToArray();
            return entries.OrderBy(x => Array.IndexOf(preference, MetadataHttp.Text(x, attribute)) is var rank && rank >= 0 ? rank : 100)
                .Select(x => MetadataHttp.Text(x, "text")).FirstOrDefault(x => x.Length > 0) ?? "";
        }
        var date = Localized("dates", "region", "eu", "us", "wor", "jp");
        var year = date.Length >= 4 && int.TryParse(date[..4], out var parsed) && parsed is >= 1900 and <= 9999 ? (int?)parsed : null;
        DateTime? releaseDate = null;
        ReleaseDatePrecision? precision = null;
        if (DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var exact))
        {
            releaseDate = DateTime.SpecifyKind(exact, DateTimeKind.Utc);
            precision = date.EndsWith("-01-01", StringComparison.Ordinal) ? ReleaseDatePrecision.Year : ReleaseDatePrecision.Exact;
        }
        else if (year is not null)
        {
            releaseDate = new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            precision = ReleaseDatePrecision.Year;
        }
        var cover = MetadataHttp.Items(MetadataHttp.Child(game, "medias")).Where(x => MetadataHttp.Text(x, "type") is "box-2D" or "box-3D" or "sstitle")
            .OrderBy(x => MetadataHttp.Text(x, "type") == "box-2D" ? 0 : 1).ThenBy(x => MetadataHttp.Text(x, "region") == "eu" ? 0 : 1)
            .Select(x => MetadataHttp.Text(x, "url")).FirstOrDefault();
        var genericTitle = Localized("noms", "region", "eu", "wor", "us", "ss", "jp") is { Length: > 0 } title
            ? title : MetadataHttp.Text(game, "nom");
        var titles = MetadataHttp.Items(MetadataHttp.Child(game, "noms"))
            .Select(x => (Language: RegionLanguage(MetadataHttp.Text(x, "region")), Text: MetadataHttp.Text(x, "text")))
            .Where(x => x.Language is not null && x.Text.Length > 0)
            .GroupBy(x => x.Language!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Text, StringComparer.OrdinalIgnoreCase);
        var descriptions = MetadataHttp.Items(MetadataHttp.Child(game, "synopsis"))
            .Select(x => (Language: MetadataHttp.Text(x, "langue").ToLowerInvariant(), Text: MetadataHttp.Text(x, "text")))
            .Where(x => x.Language.Length is >= 2 and <= 16 && x.Text.Length > 0)
            .GroupBy(x => x.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Text, StringComparer.OrdinalIgnoreCase);
        var fallbackDescription = descriptions.GetValueOrDefault("en") ?? descriptions.GetValueOrDefault("fr") ?? descriptions.Values.FirstOrDefault();
        var languages = titles.Keys.Concat(descriptions.Keys).Append(preferredLanguage).Distinct(StringComparer.OrdinalIgnoreCase);
        var translations = languages.Select(language => new MetadataTranslation(language,
            titles.GetValueOrDefault(language) ?? genericTitle,
            descriptions.GetValueOrDefault(language) ?? fallbackDescription,
            titles.ContainsKey(language) ? language : null,
            descriptions.ContainsKey(language) ? language : descriptions.FirstOrDefault(x => x.Value == fallbackDescription).Key)).ToArray();
        var selected = translations.First(x => x.Language.Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase));
        var genres = MetadataHttp.Items(MetadataHttp.Child(game, "genres")).SelectMany(x => MetadataHttp.Child(x, "noms").ValueKind == JsonValueKind.Array
                ? MetadataHttp.Items(MetadataHttp.Child(x, "noms")) : new[] { x })
            .Where(x => MetadataHttp.Text(x, "langue").Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase)
                || MetadataHttp.Text(x, "langue").Equals("en", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => MetadataHttp.Text(x, "langue").Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(x => MetadataHttp.Text(x, "text")).Where(x => x.Length > 0).Distinct().ToArray();
        return new MetadataGame(MetadataProviderKind.ScreenScraper, MetadataHttp.Text(game, "id"), selected.Title ?? genericTitle,
            selected.Description, year, MetadataHttp.Text(game, "developpeur"), MetadataHttp.Text(game, "editeur"), genres,
            [MetadataHttp.Number(MetadataHttp.Child(game, "systeme"), "id")], RemoteCoverUrl: cover,
            ReleaseDateUtc: releaseDate, ReleaseDatePrecision: precision, Translations: translations);
    }

    private static string? RegionLanguage(string region) => region.ToLowerInvariant() switch
    {
        "de" => "de", "fr" => "fr", "es" => "es", "it" => "it", "pt" => "pt",
        "us" or "uk" or "en" => "en", _ => null
    };
}
