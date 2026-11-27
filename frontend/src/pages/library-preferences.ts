export type View = 'grid' | 'table' | 'overview';
export const columnNames = {
  platform: ['Plattform', 'Platform'],
  year: ['Jahr', 'Year'],
  status: ['Status', 'Status'],
  monitored: ['Überwacht', 'Monitored'],
  files: ['Dateien', 'Files'],
} satisfies Record<string, [string, string]>;
export type Column = keyof typeof columnNames;
export const columns = Object.keys(columnNames) as Column[];
export const sortNames = {
  title: ['Titel', 'Title'],
  platform: columnNames.platform,
  year: ['Erscheinungsjahr', 'Release year'],
  status: columnNames.status,
  files: ['Dateianzahl', 'File count'],
} satisfies Record<string, [string, string]>;
export type Sort = keyof typeof sortNames;
export const posterNames = {
  title: ['Titel', 'Title'],
  platform: ['Plattform und Jahr', 'Platform and year'],
  monitored: ['Überwachungsstatus', 'Monitoring status'],
  statusText: ['Text im Statusstreifen', 'Status bar text'],
} satisfies Record<string, [string, string]>;
export type PosterField = keyof typeof posterNames;
const defaults = {
  size: 'medium' as 'small' | 'medium' | 'large',
  poster: { title: true, platform: true, monitored: true, statusText: true },
  table: { platform: true, year: true, status: true, monitored: true, files: true },
  overviewDescription: true,
};
export type Options = typeof defaults;
export const filterKeys = ['platformId', 'rootFolderId', 'status', 'verification'] as const;
export type Filters = Partial<Record<(typeof filterKeys)[number], string>>;
export type Preset = { name: string; filters: Filters };
export function readFilters(params: URLSearchParams): Filters {
  return Object.fromEntries(filterKeys.filter((k) => params.get(k)).map((k) => [k, params.get(k)!]));
}
function load(key: string) {
  try {
    return JSON.parse(localStorage.getItem(key) ?? 'null');
  } catch {
    return null;
  }
}
export function persist(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* Keep the current session usable without storage. */
  }
}
export function initialView(): View {
  try {
    const view = localStorage.getItem('ludus.view');
    return view === 'table' || view === 'overview' ? view : 'grid';
  } catch {
    return 'grid';
  }
}
function flags<T extends string>(keys: T[], stored: unknown): Record<T, boolean> {
  const values = stored && typeof stored === 'object' ? (stored as Record<string, unknown>) : {};
  return Object.fromEntries(keys.map((key) => [key, values[key] !== false])) as Record<T, boolean>;
}
export function initialOptions(): Options {
  const saved = load('ludus.library.options');
  return {
    size: saved?.size === 'small' || saved?.size === 'large' ? saved.size : defaults.size,
    poster: flags(Object.keys(defaults.poster) as PosterField[], saved?.poster),
    table: flags(columns, saved?.table),
    overviewDescription: saved?.overviewDescription !== false,
  };
}
export function initialPresets(): Preset[] {
  const saved = load('ludus.library.filters');
  if (!Array.isArray(saved)) return [];
  return saved.filter(
    (p) =>
      typeof p?.name === 'string' &&
      p.name.trim() &&
      p.filters &&
      typeof p.filters === 'object' &&
      Object.entries(p.filters).every(([k, v]) => filterKeys.some((f) => f === k) && typeof v === 'string'),
  );
}
