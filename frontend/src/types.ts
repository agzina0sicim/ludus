export type Row = Record<string, unknown> & { id: number };
export type Platform = Row & { name: string; accentColor: string; monitored: boolean };
export type RootFolder = Row & { name: string; path: string };
export type Profile = {
  profile: Row & { name: string; minimumScore: number; upgradeUntilScore: number; allowUpgrades: boolean };
  formats: { customFormatId: number; score: number }[];
};
export type Game = {
  id: number;
  title: string;
  platform: string;
  platformId: number;
  rootFolderId: number;
  qualityProfileId: number;
  monitored: boolean;
  status: string;
  releaseYear: number | null;
  coverRelativePath: string | null;
  fileCount: number;
  description?: string | null;
};
export type GameFile = {
  id: number;
  relativePath: string;
  originalFileName: string;
  releaseName: string;
  contentKind: string;
  role: string;
  discNumber: number | null;
  score: number;
  size: number;
  crc32: string | null;
  md5: string | null;
  sha1: string | null;
  verificationStatus: string;
  missing: boolean;
  regions: string[];
  languages: string[];
  importedAtUtc: string;
};
export type GameDetails = Game & {
  description: string | null;
  developer: string | null;
  publisher: string | null;
  genres: string[];
  serials: string[];
  baseTitleId: string | null;
  files: GameFile[];
  metadataReferences: { provider: string; externalId: string }[];
};
export type Page<T> = { total: number; page: number; pageSize: number; items: T[] };
export type Metadata = {
  provider: string;
  externalId: string;
  title: string;
  description: string | null;
  year: number | null;
  developer: string | null;
  genres: string[];
  coverRelativePath: string | null;
};
export type Release = {
  release: {
    indexerId: number;
    releaseId: string;
    title: string;
    size: number;
    protocol: string;
    seeders: number | null;
    publishedAtUtc: string | null;
  };
  parsed: { regions: string[]; extension: string };
  decision: {
    score: number;
    accepted: boolean;
    formats: { name: string; score: number }[];
    rejections: { code: string; messageDe: string; messageEn: string; detail: string | null }[];
  };
  ticket: string;
};
export type SearchResults = {
  releases: Release[];
  failures: { indexerId: number; code?: string; message?: string }[];
  truncated: boolean;
};
export type QueueItem = {
  id: number;
  gameId: number;
  releaseTitle: string;
  status: string;
  size: number;
  downloadedBytes: number;
  speedBytesPerSecond: number;
  etaSeconds: number | null;
  failureCode: string | null;
  cleanupPending: boolean;
  replacementSearchPending: boolean;
};
export type History = {
  id: number;
  gameId: number | null;
  gameTitle: string;
  releaseTitle: string | null;
  event: string;
  score: number | null;
  message: string;
  createdAtUtc: string;
};
export type HealthIssue = {
  id: number;
  code: string;
  resourceKey: string;
  severity: string;
  message: string;
  lastCheckedAtUtc: string;
};
export type Session = { authenticated: boolean; formsAuthenticationEnabled: boolean };
export type MediaLink = { url: string; expiresAtUtc: string };
