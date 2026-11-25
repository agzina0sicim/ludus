using System.Text.Json.Serialization;
using Ludus.Domain.Common;
using Ludus.Domain.Library;

namespace Ludus.Application.Metadata;

public sealed record MetadataGame(MetadataProviderKind Provider, string ExternalId, string Title, string? Description, int? Year,
    string? Developer, string? Publisher, IReadOnlyList<string> Genres, IReadOnlyList<int> PlatformIds,
    string? CoverRelativePath = null, [property: JsonIgnore] string? RemoteCoverUrl = null);
public sealed record MetadataSystem(int Id, string Name);
public sealed class MetadataException(string code) : Exception($"Metadata request failed: {code}.") { public string Code { get; } = code; }
public interface IMetadataProvider
{
    MetadataProviderKind Kind { get; }
    Task<IReadOnlyList<MetadataGame>> SearchAsync(string query, Platform platform, CancellationToken cancellationToken);
    Task<MetadataGame> GetAsync(string externalId, Platform platform, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetadataSystem>> SystemsAsync(CancellationToken cancellationToken);
    Task TestAsync(CancellationToken cancellationToken);
}
