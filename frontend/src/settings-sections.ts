import { useLocale } from './i18n';

export function useSettingsSections() {
  const { t } = useLocale();
  return [
    [
      'media',
      t('Medienverwaltung', 'Media management'),
      t('Root Folders, Plattformen und Dateibenennung', 'Root folders, platforms and file naming'),
    ],
    [
      'dat',
      'DAT Files',
      t('Prüfkataloge importieren und verwalten', 'Import and manage verification catalogs'),
    ],
    [
      'profiles',
      t('Profile', 'Profiles'),
      t('Plattformgebundene Qualitätsprofile und Cutoffs', 'Platform-bound quality profiles and cutoffs'),
    ],
    ['formats', 'Custom Formats', t('Wiederverwendbare Erkennungsregeln', 'Reusable matching rules')],
    [
      'indexers',
      t('Indexer', 'Indexers'),
      t('Quellen für Releases und RSS-Suche', 'Sources for releases and RSS search'),
    ],
    [
      'clients',
      t('Download-Clients', 'Download clients'),
      t('Download-Verbindungen und Pfadzuordnungen', 'Download connections and remote path mappings'),
    ],
    [
      'metadata',
      t('Metadaten', 'Metadata'),
      t('IGDB, ScreenScraper und Plattformzuordnungen', 'IGDB, ScreenScraper and platform mappings'),
    ],
    ['connect', 'Connect', t('Webhooks und Benachrichtigungen', 'Webhooks and notifications')],
    ['ui', 'UI', t('Datumsanzeige, Darstellung und Sprache', 'Date display, appearance, and language')],
    [
      'general',
      t('Allgemein & Sicherheit', 'General & security'),
      t('Instanzname, Zeitpläne, Anmeldung und API-Key', 'Instance name, schedules, sign-in and API key'),
    ],
  ] as const;
}
