using System.Text.Json.Serialization;
using Ludus.Application.Profiles;
using Ludus.Application.Releases;
using Ludus.Domain.Common;
using Ludus.Domain.Integrations;

namespace Ludus.Application.Indexers;

public sealed record IndexerCategory(int Id, string Name, int? ParentId);
public sealed record IndexerCapabilities(bool SearchAvailable, IReadOnlyList<string> SearchParameters,
    int DefaultLimit, int MaximumLimit, IReadOnlyList<IndexerCategory> Categories);
public sealed record IndexerQuery(string? SearchTerm = null, IReadOnlyList<int>? Categories = null, int Offset = 0, int Limit = 100);
public sealed record IndexerRelease(long IndexerId, string ReleaseId, string Fingerprint, string Title, long Size,
    DownloadProtocol Protocol, int? Seeders, int? Peers, DateTime? PublishedAtUtc, IReadOnlyList<int> Categories,
    [property: JsonIgnore] ProtectedSecret? DownloadUrl, IReadOnlyList<string> ParseWarnings);
public sealed record IndexerPage(IReadOnlyList<IndexerRelease> Releases, int Offset, int? Total);
public sealed record IndexerFailure(long IndexerId, string Code, int? RetryAfterSeconds = null);
public sealed record EvaluatedRelease(IndexerRelease Release, ParsedRelease Parsed, ReleaseDecision Decision);
public sealed record InteractiveSearchResult(IReadOnlyList<EvaluatedRelease> Releases, IReadOnlyList<IndexerFailure> Failures, bool Truncated);

public sealed class IndexerException(string code, int? retryAfterSeconds = null) : Exception($"Indexer request failed: {code}.")
{
    public string Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public interface IIndexerClient
{
    Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken = default);
    Task<IndexerPage> SearchAsync(Indexer indexer, IndexerQuery query, CancellationToken cancellationToken = default);
    Task<IndexerPage> GetRssAsync(Indexer indexer, IReadOnlyList<int>? categories = null, CancellationToken cancellationToken = default);
}

public interface IInteractiveSearch
{
    Task<InteractiveSearchResult> SearchAsync(long gameId, CancellationToken cancellationToken = default, bool automatic = false);
}
