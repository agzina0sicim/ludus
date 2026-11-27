namespace Ludus.Domain.Common;

public enum MetadataProviderKind { Igdb, ScreenScraper }
public enum AuthenticationMethod { Forms, Basic }
public enum AccountSetupMode { NewInstance, LegacyProtected, Complete }
public enum GrabOrigin { Automatic, Interactive }
public enum WebhookKind { Discord, Gotify, Telegram, Generic }
public enum ThemeMode { Light, Dark, Auto }
public enum ReleaseDatePrecision { Exact, Month, Year }
public enum ContentKind { Base, Update, Dlc }
public enum GameStatus { Missing, Downloading, Available }
public enum FileRole { Rom, Descriptor, Track, Playlist, Directory, Support }
public enum VerificationStatus { Unverified, VerifiedGoodDump, Modified }
public enum IndexerKind { Torznab, Newznab }
public enum DownloadProtocol { Torrent, Usenet }
public enum DownloadClientKind { QBittorrent, Transmission, Sabnzbd }
public enum DownloadStatus { Queued, Downloading, Paused, Completed, Importing, Imported, Failed }
public enum BlockKind { Cooldown, Permanent }
public enum FormatField { Region, Language, Extension, ReleaseTitle, Revision, Status, ContentKind, Serial, TitleId }
public enum FormatOperator { Equals, Contains, Regex, GreaterThanOrEqual }
public enum FormatMatchMode { All, Any }
public enum HistoryEvent { Grabbed, Imported, Upgraded, DownloadFailed, Deleted, Renamed, Verified, ScanMatched }
public enum NotificationEvent { Grabbed, Imported, ImportFailed, Upgraded, DownloadFailed }
public enum TaskKind { RssSync, DownloadRefresh, LibraryScan, HealthCheck, Backup, RecyclingCleanup }
public enum TaskRunStatus { Running, Succeeded, Failed, Interrupted }
public enum HealthSeverity { Warning, Error }
