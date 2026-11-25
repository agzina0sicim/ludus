using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Ludus.Application.Indexers;
using Ludus.Application.Security;
using Ludus.Domain.Integrations;

namespace Ludus.Infrastructure.Indexers;

public sealed class TorznabClient(HttpClient httpClient, ISecretProtector secrets, IndexerXmlParser parser) : IIndexerClient
{
    public async Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken = default)
    {
        var document = await RequestAsync(indexer, new Dictionary<string, string> { ["t"] = "caps" }, cancellationToken);
        return IndexerXmlParser.ParseCapabilities(document);
    }

    public async Task<IndexerPage> SearchAsync(Indexer indexer, IndexerQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > 1000 || query.SearchTerm?.Length > 4096)
            throw new ArgumentException("Invalid indexer query.", nameof(query));
        if (query.Categories?.Any(x => x < 0) == true) throw new ArgumentException("Invalid indexer category.", nameof(query));
        var parameters = new Dictionary<string, string>
        {
            ["t"] = "search", ["extended"] = "1", ["offset"] = query.Offset.ToString(CultureInfo.InvariantCulture),
            ["limit"] = query.Limit.ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(query.SearchTerm)) parameters["q"] = query.SearchTerm;
        var categories = query.Categories ?? indexer.Categories;
        if (categories.Count > 0) parameters["cat"] = string.Join(',', categories.Distinct());
        var document = await RequestAsync(indexer, parameters, cancellationToken);
        return parser.ParseFeed(indexer, document, Endpoint(indexer), query.Offset);
    }

    public async Task<IndexerPage> GetRssAsync(Indexer indexer, IReadOnlyList<int>? categories = null, CancellationToken cancellationToken = default)
    {
        var caps = await GetCapabilitiesAsync(indexer, cancellationToken);
        if (!caps.SearchAvailable) throw new IndexerException("SearchUnsupported");
        return await SearchAsync(indexer, new IndexerQuery(Categories: categories, Limit: Math.Min(caps.MaximumLimit, 100)), cancellationToken);
    }

    private async Task<XDocument> RequestAsync(Indexer indexer, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(indexer);
        if (indexer.ApiKey is not null)
        {
            try
            {
                parameters["apikey"] = secrets.Unprotect(indexer.ApiKey);
            }
            catch (CryptographicException)
            {
                throw new IndexerException("InvalidCredentials");
            }
        }
        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join('&', parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"))
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
            request.Headers.Accept.ParseAdd("application/rss+xml, application/xml, text/xml");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var retry = response.Headers.RetryAfter;
                var seconds = retry?.Delta?.TotalSeconds ?? (retry?.Date - DateTimeOffset.UtcNow)?.TotalSeconds;
                throw new IndexerException($"Http{(int)response.StatusCode}", seconds is null ? null : (int)Math.Clamp(seconds.Value, 0, 86400));
            }
            const int maximumBytes = 16 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximumBytes) throw new IndexerException("ResponseTooLarge");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var content = new MemoryStream();
            var buffer = new byte[32768];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (content.Length + read > maximumBytes) throw new IndexerException("ResponseTooLarge");
                content.Write(buffer, 0, read);
            }
            content.Position = 0;
            using var reader = XmlReader.Create(content, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = maximumBytes, MaxCharactersFromEntities = 1024
            });
            return await XDocument.LoadAsync(reader, LoadOptions.None, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IndexerException("Timeout");
        }
        catch (HttpRequestException)
        {
            throw new IndexerException("ConnectionFailed");
        }
        catch (IOException)
        {
            throw new IndexerException("ConnectionFailed");
        }
        catch (XmlException)
        {
            throw new IndexerException("InvalidXml");
        }
    }

    internal static Uri Endpoint(Indexer indexer)
    {
        if (!Uri.TryCreate(indexer.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new IndexerException("InvalidBaseUrl");
        var path = uri.AbsolutePath.TrimEnd('/');
        return new UriBuilder(uri) { Path = path.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? path : path + "/api" }.Uri;
    }
}
