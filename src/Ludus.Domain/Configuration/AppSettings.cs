using Ludus.Domain.Common;

namespace Ludus.Domain.Configuration;

public sealed class AppSettings : Entity
{
    public string Language { get; set; } = "de";
    public string InstanceName { get; set; } = "Ludus";
    public string NamingSchema { get; set; } = "{Game Title} ({Year})/{Game Title} ({Region}).{ext}";
    public bool UseCanonicalDatNames { get; set; }
    public bool PreferHardlinks { get; set; } = true;
    public bool RenameFiles { get; set; } = true;
    public required string RecyclingBinPath { get; set; }
    public int RecyclingRetentionDays { get; set; } = 30;
    public int BackupRetentionCount { get; set; } = 7;
    public int RssIntervalMinutes { get; set; } = 15;
    public int DownloadRefreshIntervalSeconds { get; set; } = 15;
    public int LibraryScanIntervalHours { get; set; } = 24;
    public int HealthCheckIntervalMinutes { get; set; } = 5;
    public int BackupIntervalHours { get; set; } = 24;
    public int MediaLinkLifetimeSeconds { get; set; } = 300;
    public int MaximumArchiveEntries { get; set; } = 100000;
    public long MaximumExtractedBytes { get; set; } = 536870912000;
}

public sealed class SecuritySettings : Entity
{
    public required ProtectedSecret ApiKey { get; set; }
    public bool FormsAuthenticationEnabled { get; set; }
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public Guid SessionVersion { get; set; }
    public bool TrustForwardedHeaders { get; set; }
    public int ForwardLimit { get; set; } = 1;
    public List<string> TrustedProxyNetworks { get; set; } = ["127.0.0.0/8", "::1/128"];
}
