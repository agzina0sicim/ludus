using Ludus.Domain.Common;

namespace Ludus.Domain.Integrations;

public sealed class DownloadClient : Entity
{
    public const string LudusCategory = "ludus";

    public required string Name { get; set; }
    public DownloadClientKind Kind { get; set; }
    public required string BaseUrl { get; set; }
    public ProtectedSecret? Username { get; set; }
    public ProtectedSecret? Password { get; set; }
    public ProtectedSecret? ApiKey { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
    public string Category { get; set; } = LudusCategory;
    public string? PostImportTag { get; set; } = "ludus-imported";
    public bool RemoveCompleted { get; set; }
    public bool RemoveFailed { get; set; } = true;
}

public sealed class RemotePathMapping : Entity
{
    public required string Host { get; set; }
    public required string RemotePath { get; set; }
    public required string LocalPath { get; set; }
}
