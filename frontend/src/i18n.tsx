import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
type Locale = 'de' | 'en';
function initialLocale(): Locale {
    try {
        return localStorage.getItem('ludus.locale') === 'en' ? 'en' : 'de';
    } catch {
        return 'de';
    }
}
const LocaleContext = createContext({
    locale: 'de' as Locale,
    setLocale: (_: Locale) => {},
    applyDefaultLocale: (_: Locale) => {},
    t: (de: string, en: string) => de || en,
});
export function LocaleProvider({ children }: { children: ReactNode }) {
    const [locale, setLocale] = useState<Locale>(initialLocale);
    const explicitPreference = useRef<boolean | undefined>(undefined);
    if (explicitPreference.current === undefined) {
        try {
            explicitPreference.current = ['de', 'en'].includes(localStorage.getItem('ludus.locale') ?? '');
        } catch {
            explicitPreference.current = false;
        }
    }
    useEffect(() => {
        document.documentElement.lang = locale;
        try {
            localStorage.setItem('ludus.locale', locale);
        } catch {
            /* Browser storage is optional. */
        }
    }, [locale]);
    return (
        <LocaleContext
            value={{
                locale,
                setLocale: (value) => {
                    explicitPreference.current = true;
                    setLocale(value);
                },
                applyDefaultLocale: (value) => {
                    if (!explicitPreference.current) setLocale(value);
                },
                t: (de, en) => (locale === 'de' ? de : en),
            }}
        >
            {children}
        </LocaleContext>
    );
}
export const useLocale = () => useContext(LocaleContext);
const labels: Record<string, [string, string]> = {
    Missing: ['Fehlend', 'Missing'],
    Downloading: ['Herunterladend', 'Downloading'],
    Available: ['Vorhanden', 'Available'],
    Unverified: ['Unverifiziert', 'Unverified'],
    VerifiedGoodDump: ['Verifiziert [!]', 'Verified good dump [!]'],
    Modified: ['Verändert', 'Modified'],
    Base: ['Basisspiel', 'Base game'],
    Update: ['Update', 'Update'],
    Dlc: ['DLC', 'DLC'],
    Rom: ['ROM', 'ROM'],
    Descriptor: ['Deskriptor', 'Descriptor'],
    Track: ['Track', 'Track'],
    Playlist: ['Playlist', 'Playlist'],
    Directory: ['Ordner', 'Directory'],
    Support: ['Begleitdatei', 'Support file'],
    Queued: ['Wartend', 'Queued'],
    Paused: ['Pausiert', 'Paused'],
    Completed: ['Abgeschlossen', 'Completed'],
    Importing: ['Import läuft', 'Importing'],
    Imported: ['Importiert', 'Imported'],
    Failed: ['Fehlgeschlagen', 'Failed'],
    Grabbed: ['Download gestartet', 'Grabbed'],
    Upgraded: ['Upgrade importiert', 'Upgraded'],
    DownloadFailed: ['Download fehlgeschlagen', 'Download failed'],
    Deleted: ['Entfernt', 'Deleted'],
    Renamed: ['Umbenannt', 'Renamed'],
    Verified: ['Verifiziert', 'Verified'],
    ScanMatched: ['Beim Scan erkannt', 'Matched during scan'],
    Cooldown: ['Abklingzeit', 'Cooldown'],
    Permanent: ['Dauerhaft', 'Permanent'],
    Warning: ['Warnung', 'Warning'],
    Error: ['Fehler', 'Error'],
    Running: ['Läuft', 'Running'],
    Succeeded: ['Erfolgreich', 'Succeeded'],
    Interrupted: ['Unterbrochen', 'Interrupted'],
    RssSync: ['RSS-Synchronisierung', 'RSS sync'],
    DownloadRefresh: ['Download-Aktualisierung', 'Download refresh'],
    LibraryScan: ['Bibliothek-Scan', 'Library scan'],
    HealthCheck: ['Systemprüfung', 'Health check'],
    Backup: ['Sicherung', 'Backup'],
    RecyclingCleanup: ['Papierkorb bereinigen', 'Recycling cleanup'],
    Igdb: ['IGDB', 'IGDB'],
    ScreenScraper: ['ScreenScraper.fr', 'ScreenScraper.fr'],
    QBittorrent: ['qBittorrent', 'qBittorrent'],
    Sabnzbd: ['SABnzbd', 'SABnzbd'],
    All: ['Alle Regeln', 'All rules'],
    Any: ['Eine beliebige Regel', 'Any rule'],
    Equals: ['Ist gleich', 'Equals'],
    Contains: ['Enthält', 'Contains'],
    Regex: ['Regulärer Ausdruck', 'Regular expression'],
    GreaterThanOrEqual: ['Größer oder gleich', 'Greater than or equal'],
    Region: ['Region', 'Region'],
    Language: ['Sprache', 'Language'],
    Extension: ['Dateiendung', 'Extension'],
    ReleaseTitle: ['Release-Name', 'Release title'],
    Revision: ['Revision', 'Revision'],
    Status: ['Status', 'Status'],
    ContentKind: ['Inhaltstyp', 'Content kind'],
    Serial: ['Seriennummer', 'Serial'],
    TitleId: ['Title-ID', 'Title ID'],
};
export function useLabel() {
    const { t } = useLocale();
    return (value: string) => (labels[value] ? t(...labels[value]) : value);
}
export function bytes(value: number | null | undefined, locale = 'de') {
    if (value == null || !Number.isFinite(value)) return '—';
    const unit = Math.min(4, Math.max(0, Math.floor(Math.log(Math.max(1, value)) / Math.log(1024))));
    return `${new Intl.NumberFormat(locale, { maximumFractionDigits: unit ? 1 : 0 }).format(value / 1024 ** unit)} ${['B', 'KiB', 'MiB', 'GiB', 'TiB'][unit]}`;
}
export function date(value: unknown, locale: string) {
    if (!value) return '—';
    const parsed = new Date(String(value));
    return Number.isNaN(parsed.valueOf())
        ? '—'
        : new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(parsed);
}
