using Ludus.Domain.Integrations;
using Ludus.Infrastructure.Configuration;

namespace Ludus.Infrastructure.Imports;

public sealed record ResolvedDownloadPath(string Path, string AllowedRoot);

public sealed class RemotePathResolver(LudusPaths paths)
{
    public ResolvedDownloadPath Resolve(string host, string remotePath, IReadOnlyList<RemotePathMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(remotePath)) throw new ImportException("MissingContentPath", false);
        var normalized = remotePath.Replace('\\', '/').TrimEnd('/');
        var match = mappings.Where(x => string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase))
            .Select(x => new { Mapping = x, Prefix = x.RemotePath.Replace('\\', '/').TrimEnd('/') })
            .OrderByDescending(x => x.Prefix.Length).FirstOrDefault(x => normalized.StartsWith(x.Prefix + "/",
                x.Prefix.Length >= 2 && x.Prefix[1] == ':' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (match is not null)
            return new ResolvedDownloadPath(SafePaths.Within(match.Mapping.LocalPath, normalized[(match.Prefix.Length + 1)..]), match.Mapping.LocalPath);
        if (!Path.IsPathFullyQualified(remotePath) || !SafePaths.IsWithin(paths.DownloadDirectory, remotePath))
            throw new ImportException("RemotePathMappingRequired", false);
        var path = Path.GetFullPath(remotePath);
        SafePaths.RejectLinks(path);
        return new ResolvedDownloadPath(path, paths.DownloadDirectory);
    }
}
