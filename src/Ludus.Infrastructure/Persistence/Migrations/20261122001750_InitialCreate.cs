using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ludus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", nullable: false),
                    NamingSchema = table.Column<string>(type: "TEXT", nullable: false),
                    UseCanonicalDatNames = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferHardlinks = table.Column<bool>(type: "INTEGER", nullable: false),
                    RenameFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecyclingBinPath = table.Column<string>(type: "TEXT", nullable: false),
                    RecyclingRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    BackupRetentionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RssIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadRefreshIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LibraryScanIntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthCheckIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    BackupIntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaLinkLifetimeSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumArchiveEntries = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumExtractedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                    table.CheckConstraint("CK_AppSettings_ArchiveLimits", "\"MaximumArchiveEntries\" > 0 AND \"MaximumExtractedBytes\" > 0");
                    table.CheckConstraint("CK_AppSettings_Intervals", "\"RssIntervalMinutes\" >= 1 AND \"DownloadRefreshIntervalSeconds\" >= 5 AND \"LibraryScanIntervalHours\" >= 1 AND \"HealthCheckIntervalMinutes\" >= 1 AND \"BackupIntervalHours\" >= 1");
                    table.CheckConstraint("CK_AppSettings_Language", "\"Language\" IN ('de', 'en')");
                    table.CheckConstraint("CK_AppSettings_MediaLifetime", "\"MediaLinkLifetimeSeconds\" BETWEEN 30 AND 3600");
                    table.CheckConstraint("CK_AppSettings_Retention", "\"BackupRetentionCount\" >= 1 AND \"RecyclingRetentionDays\" >= 1");
                    table.CheckConstraint("CK_AppSettings_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    ApplicationVersion = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Scheduled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                    table.CheckConstraint("CK_BackupRecord_Sha256", "\"Sha256\" IS NULL OR (length(\"Sha256\") = 64 AND \"Sha256\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_BackupRecord_Size", "\"Size\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CustomFormats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MatchMode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFormats", x => x.Id);
                    table.CheckConstraint("CK_CustomFormats_MatchMode", "\"MatchMode\" IN ('All', 'Any')");
                });

            migrationBuilder.CreateTable(
                name: "DownloadClients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    PostImportTag = table.Column<string>(type: "TEXT", nullable: true),
                    RemoveCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemoveFailed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadClients", x => x.Id);
                    table.CheckConstraint("CK_DownloadClient_BaseUrl", "instr(\"BaseUrl\", '?') = 0 AND instr(\"BaseUrl\", '@') = 0 AND instr(\"BaseUrl\", '#') = 0 AND (\"BaseUrl\" LIKE 'http://%' OR \"BaseUrl\" LIKE 'https://%')");
                    table.CheckConstraint("CK_DownloadClient_Category", "\"Category\" = 'ludus'");
                    table.CheckConstraint("CK_DownloadClient_Priority", "\"Priority\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_DownloadClients_Kind", "\"Kind\" IN ('QBittorrent', 'Transmission', 'Sabnzbd')");
                });

            migrationBuilder.CreateTable(
                name: "HealthIssues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceKey = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthIssues", x => x.Id);
                    table.CheckConstraint("CK_HealthIssues_Severity", "\"Severity\" IN ('Warning', 'Error')");
                });

            migrationBuilder.CreateTable(
                name: "Indexers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EnableRss = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableAutomaticSearch = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableInteractiveSearch = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumSeeders = table.Column<int>(type: "INTEGER", nullable: false),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    CapabilitiesXml = table.Column<string>(type: "TEXT", nullable: true),
                    CapabilitiesUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRssSyncAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRssGuid = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Indexers", x => x.Id);
                    table.CheckConstraint("CK_Indexer_BaseUrl", "instr(\"BaseUrl\", '?') = 0 AND instr(\"BaseUrl\", '@') = 0 AND instr(\"BaseUrl\", '#') = 0 AND (\"BaseUrl\" LIKE 'http://%' OR \"BaseUrl\" LIKE 'https://%')");
                    table.CheckConstraint("CK_Indexer_Categories_Json", "json_valid(\"Categories\") AND json_type(\"Categories\") = 'array'");
                    table.CheckConstraint("CK_Indexer_Priority", "\"Priority\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Indexer_Seeders", "\"MinimumSeeders\" >= 0");
                    table.CheckConstraint("CK_Indexers_Kind", "\"Kind\" IN ('Torznab', 'Newznab')");
                    table.CheckConstraint("CK_Indexers_Protocol", "\"Protocol\" IN ('Torrent', 'Usenet')");
                });

            migrationBuilder.CreateTable(
                name: "MetadataCache",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CacheKey = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataCache", x => x.Id);
                    table.CheckConstraint("CK_MetadataCache_Provider", "\"Provider\" IN ('Igdb', 'ScreenScraper')");
                    table.CheckConstraint("CK_MetadataCacheEntry_Json", "json_valid(\"PayloadJson\")");
                });

            migrationBuilder.CreateTable(
                name: "MetadataProviders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", nullable: true),
                    ClientSecret = table.Column<string>(type: "TEXT", nullable: true),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: true),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CacheDurationHours = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataProviders", x => x.Id);
                    table.CheckConstraint("CK_MetadataProviders_Provider", "\"Provider\" IN ('Igdb', 'ScreenScraper')");
                    table.CheckConstraint("CK_MetadataProviderSettings_CacheHours", "\"CacheDurationHours\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "Platforms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AccentColor = table.Column<string>(type: "TEXT", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsFolderGames = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtractArchives = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrimaryMetadataProvider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MetadataFallbackEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IgdbPlatformId = table.Column<int>(type: "INTEGER", nullable: true),
                    ScreenScraperSystemId = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowedExtensions = table.Column<string>(type: "TEXT", nullable: false),
                    TorznabCategories = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Platforms", x => x.Id);
                    table.CheckConstraint("CK_Platform_AllowedExtensions_Json", "json_valid(\"AllowedExtensions\") AND json_type(\"AllowedExtensions\") = 'array'");
                    table.CheckConstraint("CK_Platform_TorznabCategories_Json", "json_valid(\"TorznabCategories\") AND json_type(\"TorznabCategories\") = 'array'");
                    table.CheckConstraint("CK_Platforms_PrimaryMetadataProvider", "\"PrimaryMetadataProvider\" IN ('Igdb', 'ScreenScraper')");
                });

            migrationBuilder.CreateTable(
                name: "QualityProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumScore = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowUpgrades = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpgradeUntilScore = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedRegions = table.Column<string>(type: "TEXT", nullable: false),
                    ExcludedRegions = table.Column<string>(type: "TEXT", nullable: false),
                    RejectBeta = table.Column<bool>(type: "INTEGER", nullable: false),
                    RejectDemo = table.Column<bool>(type: "INTEGER", nullable: false),
                    RejectPrototype = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowUpdates = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowDlc = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfiles", x => x.Id);
                    table.CheckConstraint("CK_QualityProfile_AllowedRegions_Json", "json_valid(\"AllowedRegions\") AND json_type(\"AllowedRegions\") = 'array'");
                    table.CheckConstraint("CK_QualityProfile_Cutoff", "\"UpgradeUntilScore\" >= \"MinimumScore\"");
                    table.CheckConstraint("CK_QualityProfile_ExcludedRegions_Json", "json_valid(\"ExcludedRegions\") AND json_type(\"ExcludedRegions\") = 'array'");
                });

            migrationBuilder.CreateTable(
                name: "RemotePathMappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Host = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    RemotePath = table.Column<string>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemotePathMappings", x => x.Id);
                    table.CheckConstraint("CK_RemotePathMapping_Paths", "length(\"Host\") > 0 AND length(\"RemotePath\") > 0 AND length(\"LocalPath\") > 0");
                });

            migrationBuilder.CreateTable(
                name: "RootFolders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    LastScanAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RootFolders", x => x.Id);
                    table.CheckConstraint("CK_RootFolder_Path", "length(trim(\"Path\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "ScheduledTasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTasks", x => x.Id);
                    table.CheckConstraint("CK_ScheduledTasks_Kind", "\"Kind\" IN ('RssSync', 'DownloadRefresh', 'LibraryScan', 'HealthCheck', 'Backup', 'RecyclingCleanup')");
                });

            migrationBuilder.CreateTable(
                name: "SecuritySettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    FormsAuthenticationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SessionVersion = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrustForwardedHeaders = table.Column<bool>(type: "INTEGER", nullable: false),
                    ForwardLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    TrustedProxyNetworks = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuritySettings", x => x.Id);
                    table.CheckConstraint("CK_SecuritySettings_FormsCredentials", "\"FormsAuthenticationEnabled\" = 0 OR (\"Username\" IS NOT NULL AND length(trim(\"Username\")) > 0 AND \"PasswordHash\" IS NOT NULL AND length(\"PasswordHash\") > 0)");
                    table.CheckConstraint("CK_SecuritySettings_ForwardLimit", "\"ForwardLimit\" BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_SecuritySettings_Singleton", "\"Id\" = 1");
                    table.CheckConstraint("CK_SecuritySettings_TrustedProxyNetworks_Json", "json_valid(\"TrustedProxyNetworks\") AND json_type(\"TrustedProxyNetworks\") = 'array'");
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Authorization = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Events = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                    table.CheckConstraint("CK_Webhook_Events_Json", "json_valid(\"Events\") AND json_type(\"Events\") = 'array'");
                });

            migrationBuilder.CreateTable(
                name: "CustomFormatRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomFormatId = table.Column<long>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Negate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Required = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFormatRules", x => x.Id);
                    table.CheckConstraint("CK_CustomFormatRules_Field", "\"Field\" IN ('Region', 'Language', 'Extension', 'ReleaseTitle', 'Revision', 'Status', 'ContentKind', 'Serial', 'TitleId')");
                    table.CheckConstraint("CK_CustomFormatRules_Operator", "\"Operator\" IN ('Equals', 'Contains', 'Regex', 'GreaterThanOrEqual')");
                    table.ForeignKey(
                        name: "FK_CustomFormatRules_CustomFormats_CustomFormatId",
                        column: x => x.CustomFormatId,
                        principalTable: "CustomFormats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DatFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlatformId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DatVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatFiles", x => x.Id);
                    table.CheckConstraint("CK_DatFile_Sha256", "\"Sha256\" IS NULL OR (length(\"Sha256\") = 64 AND \"Sha256\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.ForeignKey(
                        name: "FK_DatFiles_Platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "Platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QualityProfileFormats",
                columns: table => new
                {
                    QualityProfileId = table.Column<long>(type: "INTEGER", nullable: false),
                    CustomFormatId = table.Column<long>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfileFormats", x => new { x.QualityProfileId, x.CustomFormatId });
                    table.ForeignKey(
                        name: "FK_QualityProfileFormats_CustomFormats_CustomFormatId",
                        column: x => x.CustomFormatId,
                        principalTable: "CustomFormats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QualityProfileFormats_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseYear = table.Column<int>(type: "INTEGER", nullable: true),
                    Developer = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    Serials = table.Column<string>(type: "TEXT", nullable: false),
                    BaseTitleId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CoverRelativePath = table.Column<string>(type: "TEXT", nullable: true),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PlatformId = table.Column<long>(type: "INTEGER", nullable: false),
                    RootFolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    QualityProfileId = table.Column<long>(type: "INTEGER", nullable: false),
                    MetadataUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.CheckConstraint("CK_Game_BaseTitleId", "\"BaseTitleId\" IS NULL OR (length(\"BaseTitleId\") = 16 AND \"BaseTitleId\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_Game_Genres_Json", "json_valid(\"Genres\") AND json_type(\"Genres\") = 'array'");
                    table.CheckConstraint("CK_Game_Serials_Json", "json_valid(\"Serials\") AND json_type(\"Serials\") = 'array'");
                    table.CheckConstraint("CK_Game_Title", "length(trim(\"Title\")) > 0");
                    table.CheckConstraint("CK_Game_Year", "\"ReleaseYear\" IS NULL OR \"ReleaseYear\" BETWEEN 1900 AND 9999");
                    table.CheckConstraint("CK_Games_Status", "\"Status\" IN ('Missing', 'Downloading', 'Available')");
                    table.ForeignKey(
                        name: "FK_Games_Platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "Platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Games_RootFolders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScheduledTaskId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRuns", x => x.Id);
                    table.CheckConstraint("CK_TaskRuns_Status", "\"Status\" IN ('Running', 'Succeeded', 'Failed', 'Interrupted')");
                    table.ForeignKey(
                        name: "FK_TaskRuns_ScheduledTasks_ScheduledTaskId",
                        column: x => x.ScheduledTaskId,
                        principalTable: "ScheduledTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DatGames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Serial = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatGames_DatFiles_DatFileId",
                        column: x => x.DatFileId,
                        principalTable: "DatFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Downloads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadClientId = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexerId = table.Column<long>(type: "INTEGER", nullable: true),
                    DownloadId = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseGuid = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadedBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    SpeedBytesPerSecond = table.Column<long>(type: "INTEGER", nullable: false),
                    EtaSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    RemoteContentPath = table.Column<string>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", nullable: true),
                    ImportAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Downloads", x => x.Id);
                    table.CheckConstraint("CK_Downloads_ContentKind", "\"ContentKind\" IN ('Base', 'Update', 'Dlc')");
                    table.CheckConstraint("CK_Downloads_Protocol", "\"Protocol\" IN ('Torrent', 'Usenet')");
                    table.CheckConstraint("CK_Downloads_Status", "\"Status\" IN ('Queued', 'Downloading', 'Paused', 'Completed', 'Importing', 'Imported', 'Failed')");
                    table.CheckConstraint("CK_TrackedDownload_Progress", "\"Size\" >= 0 AND \"DownloadedBytes\" >= 0 AND \"SpeedBytesPerSecond\" >= 0 AND (\"EtaSeconds\" IS NULL OR \"EtaSeconds\" >= 0) AND \"ImportAttempts\" >= 0");
                    table.ForeignKey(
                        name: "FK_Downloads_DownloadClients_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "DownloadClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Downloads_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Downloads_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GameMetadataReferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameMetadataReferences", x => x.Id);
                    table.CheckConstraint("CK_GameMetadataReferences_Provider", "\"Provider\" IN ('Igdb', 'ScreenScraper')");
                    table.ForeignKey(
                        name: "FK_GameMetadataReferences_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseBlocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexerId = table.Column<long>(type: "INTEGER", nullable: true),
                    ReleaseFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseBlocks", x => x.Id);
                    table.CheckConstraint("CK_ReleaseBlock_Expiry", "(\"Kind\" = 'Permanent' AND \"ExpiresAtUtc\" IS NULL) OR (\"Kind\" = 'Cooldown' AND \"ExpiresAtUtc\" IS NOT NULL AND \"ExpiresAtUtc\" > \"CreatedAtUtc\")");
                    table.CheckConstraint("CK_ReleaseBlocks_Kind", "\"Kind\" IN ('Cooldown', 'Permanent')");
                    table.ForeignKey(
                        name: "FK_ReleaseBlocks_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseBlocks_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DatRoms",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatGameId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: true),
                    Crc32 = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true, collation: "NOCASE"),
                    Md5 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true, collation: "NOCASE"),
                    Sha1 = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Merge = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatRoms", x => x.Id);
                    table.CheckConstraint("CK_DatRom_Crc32", "\"Crc32\" IS NULL OR (length(\"Crc32\") = 8 AND \"Crc32\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_DatRom_Md5", "\"Md5\" IS NULL OR (length(\"Md5\") = 32 AND \"Md5\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_DatRom_Sha1", "\"Sha1\" IS NULL OR (length(\"Sha1\") = 40 AND \"Sha1\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_DatRom_Size", "\"Size\" IS NULL OR \"Size\" >= 0");
                    table.ForeignKey(
                        name: "FK_DatRoms_DatGames_DatGameId",
                        column: x => x.DatGameId,
                        principalTable: "DatGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<long>(type: "INTEGER", nullable: true),
                    DownloadId = table.Column<long>(type: "INTEGER", nullable: true),
                    GameTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Event = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                    table.CheckConstraint("CK_History_ContentKind", "\"ContentKind\" IN ('Base', 'Update', 'Dlc')");
                    table.CheckConstraint("CK_History_Event", "\"Event\" IN ('Grabbed', 'Imported', 'Upgraded', 'DownloadFailed', 'Deleted', 'Renamed', 'Verified', 'ScanMatched')");
                    table.ForeignKey(
                        name: "FK_History_Downloads_DownloadId",
                        column: x => x.DownloadId,
                        principalTable: "Downloads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_History_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GameFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Crc32 = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Md5 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Sha1 = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    TitleId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Revision = table.Column<string>(type: "TEXT", nullable: true),
                    Regions = table.Column<string>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DatRomId = table.Column<long>(type: "INTEGER", nullable: true),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Missing = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameFiles", x => x.Id);
                    table.CheckConstraint("CK_GameFile_Crc32", "\"Crc32\" IS NULL OR (length(\"Crc32\") = 8 AND \"Crc32\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_GameFile_Disc", "\"DiscNumber\" IS NULL OR \"DiscNumber\" > 0");
                    table.CheckConstraint("CK_GameFile_Languages_Json", "json_valid(\"Languages\") AND json_type(\"Languages\") = 'array'");
                    table.CheckConstraint("CK_GameFile_Md5", "\"Md5\" IS NULL OR (length(\"Md5\") = 32 AND \"Md5\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_GameFile_Regions_Json", "json_valid(\"Regions\") AND json_type(\"Regions\") = 'array'");
                    table.CheckConstraint("CK_GameFile_RelativePath", "length(\"RelativePath\") > 0 AND substr(\"RelativePath\", 1, 1) <> '/' AND instr(\"RelativePath\", ':') = 0 AND instr(\"RelativePath\", char(92)) = 0 AND instr('/' || \"RelativePath\" || '/', '/../') = 0 AND instr('/' || \"RelativePath\" || '/', '/./') = 0");
                    table.CheckConstraint("CK_GameFile_Sha1", "\"Sha1\" IS NULL OR (length(\"Sha1\") = 40 AND \"Sha1\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_GameFile_Size", "\"Size\" >= 0");
                    table.CheckConstraint("CK_GameFile_TitleId", "\"TitleId\" IS NULL OR (length(\"TitleId\") = 16 AND \"TitleId\" NOT GLOB '*[^0-9a-fA-F]*')");
                    table.CheckConstraint("CK_GameFiles_ContentKind", "\"ContentKind\" IN ('Base', 'Update', 'Dlc')");
                    table.CheckConstraint("CK_GameFiles_Role", "\"Role\" IN ('Rom', 'Descriptor', 'Track', 'Playlist', 'Directory', 'Support')");
                    table.CheckConstraint("CK_GameFiles_VerificationStatus", "\"VerificationStatus\" IN ('Unverified', 'VerifiedGoodDump', 'Modified')");
                    table.ForeignKey(
                        name: "FK_GameFiles_DatRoms_DatRomId",
                        column: x => x.DatRomId,
                        principalTable: "DatRoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GameFiles_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Backups_RelativePath",
                table: "Backups",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFormatRules_CustomFormatId",
                table: "CustomFormatRules",
                column: "CustomFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFormats_Name",
                table: "CustomFormats",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatFiles_PlatformId_Sha256",
                table: "DatFiles",
                columns: new[] { "PlatformId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatGames_DatFileId_Name",
                table: "DatGames",
                columns: new[] { "DatFileId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatRoms_Crc32_Size",
                table: "DatRoms",
                columns: new[] { "Crc32", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_DatRoms_DatGameId_Name",
                table: "DatRoms",
                columns: new[] { "DatGameId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatRoms_Md5_Size",
                table: "DatRoms",
                columns: new[] { "Md5", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_DatRoms_Sha1_Size",
                table: "DatRoms",
                columns: new[] { "Sha1", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadClients_Name",
                table: "DownloadClients",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_DownloadClientId_DownloadId",
                table: "Downloads",
                columns: new[] { "DownloadClientId", "DownloadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_GameId_ReleaseFingerprint",
                table: "Downloads",
                columns: new[] { "GameId", "ReleaseFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_IndexerId",
                table: "Downloads",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_Status",
                table: "Downloads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GameFiles_Crc32_Size",
                table: "GameFiles",
                columns: new[] { "Crc32", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_GameFiles_DatRomId",
                table: "GameFiles",
                column: "DatRomId");

            migrationBuilder.CreateIndex(
                name: "IX_GameFiles_FileGroupId",
                table: "GameFiles",
                column: "FileGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GameFiles_GameId_ContentKind_Missing",
                table: "GameFiles",
                columns: new[] { "GameId", "ContentKind", "Missing" });

            migrationBuilder.CreateIndex(
                name: "IX_GameFiles_GameId_RelativePath",
                table: "GameFiles",
                columns: new[] { "GameId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameFiles_Sha1_Size",
                table: "GameFiles",
                columns: new[] { "Sha1", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_GameMetadataReferences_GameId_Provider",
                table: "GameMetadataReferences",
                columns: new[] { "GameId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameMetadataReferences_Provider_ExternalId",
                table: "GameMetadataReferences",
                columns: new[] { "Provider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_PlatformId_BaseTitleId",
                table: "Games",
                columns: new[] { "PlatformId", "BaseTitleId" },
                unique: true,
                filter: "\"BaseTitleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Games_PlatformId_Monitored_Status",
                table: "Games",
                columns: new[] { "PlatformId", "Monitored", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_QualityProfileId",
                table: "Games",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_RootFolderId",
                table: "Games",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_SortTitle",
                table: "Games",
                column: "SortTitle");

            migrationBuilder.CreateIndex(
                name: "IX_HealthIssues_Code_ResourceKey",
                table: "HealthIssues",
                columns: new[] { "Code", "ResourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_History_CreatedAtUtc",
                table: "History",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_History_DownloadId",
                table: "History",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_History_GameId_CreatedAtUtc",
                table: "History",
                columns: new[] { "GameId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Indexers_Name",
                table: "Indexers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataCache_ExpiresAtUtc",
                table: "MetadataCache",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataCache_Provider_CacheKey",
                table: "MetadataCache",
                columns: new[] { "Provider", "CacheKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProviders_Provider",
                table: "MetadataProviders",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Platforms_Slug",
                table: "Platforms",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfileFormats_CustomFormatId",
                table: "QualityProfileFormats",
                column: "CustomFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_Name",
                table: "QualityProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseBlocks_ExpiresAtUtc",
                table: "ReleaseBlocks",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseBlocks_GameId_ReleaseFingerprint",
                table: "ReleaseBlocks",
                columns: new[] { "GameId", "ReleaseFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseBlocks_IndexerId",
                table: "ReleaseBlocks",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_RemotePathMappings_Host_RemotePath",
                table: "RemotePathMappings",
                columns: new[] { "Host", "RemotePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RootFolders_Path",
                table: "RootFolders",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledTasks_Kind",
                table: "ScheduledTasks",
                column: "Kind",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuns_ScheduledTaskId_StartedAtUtc",
                table: "TaskRuns",
                columns: new[] { "ScheduledTaskId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_Name",
                table: "Webhooks",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropTable(
                name: "CustomFormatRules");

            migrationBuilder.DropTable(
                name: "GameFiles");

            migrationBuilder.DropTable(
                name: "GameMetadataReferences");

            migrationBuilder.DropTable(
                name: "HealthIssues");

            migrationBuilder.DropTable(
                name: "History");

            migrationBuilder.DropTable(
                name: "MetadataCache");

            migrationBuilder.DropTable(
                name: "MetadataProviders");

            migrationBuilder.DropTable(
                name: "QualityProfileFormats");

            migrationBuilder.DropTable(
                name: "ReleaseBlocks");

            migrationBuilder.DropTable(
                name: "RemotePathMappings");

            migrationBuilder.DropTable(
                name: "SecuritySettings");

            migrationBuilder.DropTable(
                name: "TaskRuns");

            migrationBuilder.DropTable(
                name: "Webhooks");

            migrationBuilder.DropTable(
                name: "DatRoms");

            migrationBuilder.DropTable(
                name: "Downloads");

            migrationBuilder.DropTable(
                name: "CustomFormats");

            migrationBuilder.DropTable(
                name: "ScheduledTasks");

            migrationBuilder.DropTable(
                name: "DatGames");

            migrationBuilder.DropTable(
                name: "DownloadClients");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Indexers");

            migrationBuilder.DropTable(
                name: "DatFiles");

            migrationBuilder.DropTable(
                name: "QualityProfiles");

            migrationBuilder.DropTable(
                name: "RootFolders");

            migrationBuilder.DropTable(
                name: "Platforms");
        }
    }
}
