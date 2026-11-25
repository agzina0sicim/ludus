import type { FormField } from './forms';
export const field = (key: string, de: string, en: string, extra: Partial<FormField> = {}): FormField => ({
  key,
  label: [de, en],
  ...extra,
});
const text = (key: string, de: string, en: string, maxLength = 128) =>
  field(key, de, en, { required: true, maxLength });
const toggle = (key: string, de: string, en: string) => field(key, de, en, { type: 'boolean' });
const number = (key: string, de: string, en: string, min: number, max: number) =>
  field(key, de, en, { type: 'number', required: true, min, max });
const secret = (key: string, de: string, en: string) => field(key, de, en, { type: 'secret' });
const select = (key: string, de: string, en: string, options: string[]) =>
  field(key, de, en, { type: 'select', options, required: true });
const list = (key: string, de: string, en: string, numeric = false) =>
  field(key, de, en, {
    type: numeric ? 'numbers' : 'list',
    hint: ['Mit Komma trennen', 'Separate with commas'],
    ...(numeric ? { pattern: '[0-9,\\s]*' } : {}),
  });
export const rootFields = [
  text('name', 'Name', 'Name'),
  text('path', 'Absoluter Ordnerpfad', 'Absolute directory path', 4096),
];
export const platformFields = [
  text('name', 'Name', 'Name'),
  field('slug', 'Kennung', 'Slug', { required: true, pattern: '[a-z0-9-]{1,64}', maxLength: 64 }),
  field('accentColor', 'Akzentfarbe', 'Accent color', { type: 'color' }),
  toggle('monitored', 'Plattform überwachen', 'Monitor platform'),
  toggle('supportsFolderGames', 'Ordnerbasierte Spiele', 'Folder-based games'),
  toggle('extractArchives', 'Archive entpacken', 'Extract archives'),
  select('primaryMetadataProvider', 'Primärer Provider', 'Primary provider', ['Igdb', 'ScreenScraper']),
  toggle('metadataFallbackEnabled', 'Provider-Fallback erlauben', 'Allow provider fallback'),
  field('igdbPlatformId', 'IGDB-Plattform-ID', 'IGDB platform ID', {
    type: 'nullableNumber',
    min: 1,
    max: 2147483647,
  }),
  field('screenScraperSystemId', 'ScreenScraper-System-ID', 'ScreenScraper system ID', {
    type: 'nullableNumber',
    min: 1,
    max: 2147483647,
  }),
  { ...list('allowedExtensions', 'Dateiendungen', 'File extensions'), required: true },
  list('torznabCategories', 'Torznab-Kategorien', 'Torznab categories', true),
];
export const platformDefaults = {
  name: '',
  slug: '',
  accentColor: '#ae9573',
  monitored: true,
  supportsFolderGames: false,
  extractArchives: true,
  primaryMetadataProvider: 'Igdb',
  metadataFallbackEnabled: true,
  igdbPlatformId: null,
  screenScraperSystemId: null,
  allowedExtensions: [],
  torznabCategories: [1000],
};
export const indexerFields = [
  text('name', 'Name', 'Name'),
  field('baseUrl', 'Basis-URL ohne API-Key', 'Base URL without API key', {
    type: 'url',
    required: true,
    maxLength: 2048,
  }),
  secret('apiKey', 'API-Key', 'API key'),
  select('kind', 'Typ', 'Type', ['Torznab', 'Newznab']),
  select('protocol', 'Protokoll', 'Protocol', ['Torrent', 'Usenet']),
  toggle('enableRss', 'RSS aktivieren', 'Enable RSS'),
  toggle('enableAutomaticSearch', 'Automatische Suche', 'Automatic search'),
  toggle('enableInteractiveSearch', 'Interaktive Suche', 'Interactive search'),
  number('priority', 'Priorität', 'Priority', 1, 100),
  number('minimumSeeders', 'Minimale Seeder', 'Minimum seeders', 0, 1000000),
  list('categories', 'Kategorien', 'Categories', true),
];
export const indexerDefaults = {
  name: '',
  baseUrl: '',
  kind: 'Torznab',
  protocol: 'Torrent',
  enableRss: true,
  enableAutomaticSearch: true,
  enableInteractiveSearch: true,
  priority: 25,
  minimumSeeders: 1,
  categories: [1000],
};
export const clientFields = [
  text('name', 'Name', 'Name'),
  select('kind', 'Client', 'Client', ['QBittorrent', 'Transmission', 'Sabnzbd']),
  field('baseUrl', 'Basis-URL', 'Base URL', { type: 'url', required: true, maxLength: 2048 }),
  secret('username', 'Benutzername', 'Username'),
  secret('password', 'Passwort', 'Password'),
  secret('apiKey', 'API-Key', 'API key'),
  toggle('enabled', 'Aktiviert', 'Enabled'),
  number('priority', 'Priorität', 'Priority', 1, 100),
  field('postImportTag', 'Tag nach Import', 'Post-import tag', { maxLength: 64, pattern: '[a-zA-Z0-9_-]*' }),
  toggle('removeCompleted', 'Abgeschlossene Aufträge entfernen', 'Remove completed jobs'),
  toggle('removeFailed', 'Fehlgeschlagene Aufträge entfernen', 'Remove failed jobs'),
];
export const clientDefaults = {
  name: '',
  kind: 'QBittorrent',
  baseUrl: '',
  enabled: true,
  priority: 25,
  postImportTag: 'ludus-imported',
  removeCompleted: false,
  removeFailed: true,
};
export const mappingFields = [
  text('host', 'Client-Host (ohne Port)', 'Client host (without port)', 253),
  text('remotePath', 'Remote-Pfad', 'Remote path', 4096),
  text('localPath', 'Lokaler Pfad', 'Local path', 4096),
];
export const providerFields = [
  toggle('enabled', 'Aktiviert', 'Enabled'),
  secret('clientId', 'Client-ID / Entwickler-ID', 'Client ID / developer ID'),
  secret('clientSecret', 'Client-Secret / Entwickler-Passwort', 'Client secret / developer password'),
  secret('username', 'ScreenScraper-Benutzer', 'ScreenScraper username'),
  secret('password', 'ScreenScraper-Passwort', 'ScreenScraper password'),
  number('cacheDurationHours', 'Cache-Dauer (Stunden)', 'Cache duration (hours)', 1, 8760),
];
export const webhookFields = [
  text('name', 'Name', 'Name'),
  secret('url', 'Webhook-URL', 'Webhook URL'),
  secret('authorization', 'Authorization-Header (optional)', 'Authorization header (optional)'),
  toggle('enabled', 'Aktiviert', 'Enabled'),
];
export const profileFields = [
  text('name', 'Name', 'Name'),
  number('minimumScore', 'Mindest-Score', 'Minimum score', -2147483648, 2147483647),
  toggle('allowUpgrades', 'Upgrades erlauben', 'Allow upgrades'),
  number(
    'upgradeUntilScore',
    'Upgrade bis Score (Cutoff)',
    'Upgrade until score (cutoff)',
    -2147483648,
    2147483647,
  ),
  list('allowedRegions', 'Erlaubte Regionen (leer = alle)', 'Allowed regions (empty = all)'),
  list('excludedRegions', 'Ausgeschlossene Regionen', 'Excluded regions'),
  toggle('rejectBeta', 'Beta ablehnen', 'Reject beta'),
  toggle('rejectDemo', 'Demo ablehnen', 'Reject demo'),
  toggle('rejectPrototype', 'Prototyp ablehnen', 'Reject prototype'),
  toggle('allowUpdates', 'Updates erlauben', 'Allow updates'),
  toggle('allowDlc', 'DLC erlauben', 'Allow DLC'),
];
export const formatFields = [
  text('name', 'Name', 'Name'),
  select('matchMode', 'Regeln verknüpfen', 'Combine rules', ['All', 'Any']),
];
export const mediaFields = [
  text('namingSchema', 'Benennungsschema', 'Naming schema', 512),
  toggle('useCanonicalDatNames', 'Kanonische DAT-Namen verwenden', 'Use canonical DAT names'),
  toggle('preferHardlinks', 'Hardlinks bevorzugen', 'Prefer hardlinks'),
  toggle('renameFiles', 'Dateien umbenennen', 'Rename files'),
  text('recyclingBinPath', 'Papierkorb-Pfad', 'Recycling bin path', 4096),
  number('recyclingRetentionDays', 'Papierkorb-Aufbewahrung (Tage)', 'Recycling retention (days)', 1, 3650),
  number('maximumArchiveEntries', 'Maximale Archiveinträge', 'Maximum archive entries', 1, 1000000),
  number('maximumExtractedBytes', 'Maximale entpackte Bytes', 'Maximum extracted bytes', 1, 17592186044416),
];
export const generalFields = [
  text('instanceName', 'Instanzname', 'Instance name'),
  select('language', 'Standardsprache', 'Default language', ['de', 'en']),
  number('rssIntervalMinutes', 'RSS-Intervall (Minuten)', 'RSS interval (minutes)', 1, 1440),
  number(
    'downloadRefreshIntervalSeconds',
    'Download-Intervall (Sekunden)',
    'Download interval (seconds)',
    5,
    3600,
  ),
  number('libraryScanIntervalHours', 'Scan-Intervall (Stunden)', 'Scan interval (hours)', 1, 8760),
  number('healthCheckIntervalMinutes', 'Systemprüfung (Minuten)', 'Health check (minutes)', 1, 1440),
  number('backupIntervalHours', 'Backup-Intervall (Stunden)', 'Backup interval (hours)', 1, 8760),
  number('backupRetentionCount', 'Anzahl aufzubewahrender Backups', 'Backup retention count', 1, 1000),
  number(
    'mediaLinkLifetimeSeconds',
    'Gültigkeit von Medien-Links (Sekunden)',
    'Media link lifetime (seconds)',
    30,
    3600,
  ),
];
export const securityFields = [
  toggle('formsAuthenticationEnabled', 'Benutzeranmeldung aktivieren', 'Enable account login'),
  field('username', 'Benutzername', 'Username', { maxLength: 128 }),
  toggle('trustForwardedHeaders', 'Proxy-Header vertrauen', 'Trust proxy headers'),
  number('forwardLimit', 'Maximale Proxy-Hops', 'Maximum proxy hops', 1, 10),
  list('trustedProxyNetworks', 'Vertrauenswürdige private CIDR-Netze', 'Trusted private CIDR networks'),
];
