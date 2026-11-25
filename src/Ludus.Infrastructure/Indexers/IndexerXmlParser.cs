using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Ludus.Application.Indexers;
using Ludus.Application.Releases;
using Ludus.Application.Security;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Infrastructure.Indexers;

public sealed class IndexerXmlParser(ISecretProtector secrets)
{
    public static IndexerCapabilities ParseCapabilities(XDocument document)
    {
        ThrowIfError(document);
        if (document.Root?.Name.LocalName != "caps") throw new IndexerException("InvalidCapabilities");
        var root = document.Root;
        var limits = Child(root, "limits");
        var maximum = PositiveInt((string?)limits?.Attribute("max"), 100);
        var defaultLimit = Math.Min(PositiveInt((string?)limits?.Attribute("default"), 100), maximum);
        var search = Child(Child(root, "searching"), "search");
        var available = string.Equals((string?)search?.Attribute("available"), "yes", StringComparison.OrdinalIgnoreCase);
        var parameters = ((string?)search?.Attribute("supportedParams") ?? "q")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var categories = new List<IndexerCategory>();
        foreach (var category in Child(root, "categories")?.Elements() ?? [])
        {
            var id = NonNegativeInt((string?)category.Attribute("id"));
            if (id is null) continue;
            categories.Add(new IndexerCategory(id.Value, (string?)category.Attribute("name") ?? id.ToString()!, null));
            foreach (var child in category.Elements().Where(x => x.Name.LocalName == "subcat"))
            {
                var childId = NonNegativeInt((string?)child.Attribute("id"));
                if (childId is not null) categories.Add(new IndexerCategory(childId.Value, (string?)child.Attribute("name") ?? childId.ToString()!, id));
            }
        }
        return new IndexerCapabilities(available, parameters, defaultLimit, maximum, categories.DistinctBy(x => x.Id).ToArray());
    }

    public IndexerPage ParseFeed(Indexer indexer, XDocument document, Uri endpoint, int requestedOffset)
    {
        ThrowIfError(document);
        if (document.Root?.Name.LocalName != "rss") throw new IndexerException("InvalidFeed");
        var channel = Child(document.Root, "channel") ?? throw new IndexerException("InvalidFeed");
        var response = Child(channel, "response");
        var offset = NonNegativeInt((string?)response?.Attribute("offset")) ?? requestedOffset;
        var total = NonNegativeInt((string?)response?.Attribute("total"));
        var results = new List<IndexerRelease>();
        foreach (var item in channel.Elements().Where(x => x.Name.LocalName == "item"))
        {
            if (results.Count >= 1000) throw new IndexerException("TooManyFeedItems");
            var warnings = new List<string>();
            var title = Child(item, "title")?.Value.Trim() ?? "";
            if (title.Length is 0 or > 4096) warnings.Add("InvalidTitle");
            var attributes = item.Elements().Where(x => x.Name.LocalName == "attr" && IsAttributeNamespace(x.Name.NamespaceName))
                .Where(x => x.Attribute("name") is not null).GroupBy(x => (string)x.Attribute("name")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Select(a => (string?)a.Attribute("value") ?? "").Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
            string? Attribute(string name) => attributes.GetValueOrDefault(name)?.FirstOrDefault();
            var enclosure = Child(item, "enclosure");
            var sizeText = Attribute("size") ?? (string?)enclosure?.Attribute("length") ?? Child(item, "size")?.Value;
            if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0)
            {
                size = 0;
                warnings.Add("InvalidSize");
            }
            var categories = (attributes.GetValueOrDefault("category") ?? [])
                .Concat(item.Elements().Where(x => x.Name.LocalName == "category").Select(x => x.Value))
                .Select(NonNegativeInt).Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
            var publishedText = Child(item, "pubDate")?.Value;
            DateTime? published = DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var timestamp) ? timestamp.UtcDateTime : null;
            var url = ResolveUrl((string?)enclosure?.Attribute("url"), endpoint, indexer.Protocol)
                ?? ResolveUrl(Attribute("magneturl"), endpoint, indexer.Protocol)
                ?? ResolveUrl(Child(item, "link")?.Value, endpoint, indexer.Protocol);
            var infoHash = Attribute("infohash")?.ToLowerInvariant();
            if (infoHash is not null && (infoHash.Length != 40 || !infoHash.All(Uri.IsHexDigit))) infoHash = null;
            var fingerprint = Digest(infoHash is not null ? $"btih:{infoHash}" : $"{ReleaseParser.NormalizeTitle(title)}:{size}");
            var guid = Child(item, "guid")?.Value ?? Attribute("guid") ?? fingerprint;
            results.Add(new IndexerRelease(indexer.Id, Digest($"{indexer.Id}:{guid}"), fingerprint, title, size,
                indexer.Protocol, NonNegativeInt(Attribute("seeders")), NonNegativeInt(Attribute("peers")), published,
                categories, url is null ? null : secrets.Protect(url.AbsoluteUri), warnings));
        }
        return new IndexerPage(results, offset, total);
    }

    public static string SerializeCapabilities(IndexerCapabilities capabilities) => new XElement("caps",
        new XElement("limits", new XAttribute("max", capabilities.MaximumLimit), new XAttribute("default", capabilities.DefaultLimit)),
        new XElement("searching", new XElement("search", new XAttribute("available", capabilities.SearchAvailable ? "yes" : "no"),
            new XAttribute("supportedParams", string.Join(',', capabilities.SearchParameters)))),
        new XElement("categories", capabilities.Categories.Where(x => x.ParentId is null).Select(category =>
            new XElement("category", new XAttribute("id", category.Id), new XAttribute("name", category.Name),
                capabilities.Categories.Where(x => x.ParentId == category.Id).Select(child =>
                    new XElement("subcat", new XAttribute("id", child.Id), new XAttribute("name", child.Name))))))).ToString(SaveOptions.DisableFormatting);

    private static Uri? ResolveUrl(string? value, Uri endpoint, DownloadProtocol protocol)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16384 || !Uri.TryCreate(endpoint, value, out var uri)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (uri.Scheme is "http" or "https" || (protocol == DownloadProtocol.Torrent && uri.Scheme == "magnet")) return uri;
        return null;
    }

    private static bool IsAttributeNamespace(string value) => value is "http://torznab.com/schemas/2015/feed"
        or "http://www.newznab.com/DTD/2010/feeds/attributes/" or "https://www.newznab.com/DTD/2010/feeds/attributes/";
    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    private static int? NonNegativeInt(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static int PositiveInt(string? value, int fallback) => NonNegativeInt(value) is > 0 and <= 1000 ? int.Parse(value!, CultureInfo.InvariantCulture) : fallback;
    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void ThrowIfError(XDocument document)
    {
        if (document.Root?.Name.LocalName == "error")
        {
            var code = NonNegativeInt((string?)document.Root.Attribute("code"));
            throw new IndexerException(code is null ? "ApiError" : $"ApiError{code}");
        }
    }
}
