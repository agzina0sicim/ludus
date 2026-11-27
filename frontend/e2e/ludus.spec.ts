import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { createHash } from 'node:crypto';

const directory = process.env.LUDUS_E2E_DIRECTORY;
test.skip(!directory, 'Set LUDUS_E2E_DIRECTORY to an isolated Ludus test instance directory.');
let key: string;
let rootId: number;
let platformId: number;
let profileId: number;
const title = `Ludus Test ${Date.now()}`;
const administrator = 'ludus-e2e';
const administratorPassword = 'Ludus-e2e-password-2026';
async function authorized(request: APIRequestContext, path: string, method = 'GET', data?: unknown) {
  const response = await request
    .fetch(`/api/v1/${path}`, {
      method,
      headers: { 'X-Api-Key': key },
      ...(data === undefined ? {} : { data }),
    })
    .catch(() => {
      throw new Error(`Test API unavailable: ${method} ${path}`);
    });
  expect(response.ok(), `${method} ${path}: ${response.status()}`).toBeTruthy();
  return response.status() === 204 ? undefined : response.json();
}
async function signIn(page: Page) {
  await page.goto('/');
  const setup = page.getByRole('heading', { name: 'Administratorkonto einrichten', exact: true });
  const login = page.getByRole('heading', { name: 'Willkommen zurück.', exact: true });
  await expect(setup.or(login)).toBeVisible();
  if (await setup.isVisible().catch(() => false)) {
    await page.getByLabel('Benutzername', { exact: true }).fill(administrator);
    await page.getByLabel('Passwort (mindestens 12 Zeichen)', { exact: true }).fill(administratorPassword);
    await page.getByLabel('Passwort wiederholen', { exact: true }).fill(administratorPassword);
    await page.getByLabel('Anmeldemethode', { exact: true }).selectOption('Forms');
    await page.getByRole('button', { name: 'Konto erstellen', exact: true }).click();
  } else {
    await page.getByLabel('Benutzername', { exact: true }).fill(administrator);
    await page.getByLabel('Passwort', { exact: true }).fill(administratorPassword);
    await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
  }
  await expect(page.getByRole('heading', { name: 'Bibliothek', exact: true })).toBeVisible();
  await expect(page.getByText('Live verbunden', { exact: true })).toBeVisible();
}
test.beforeAll(async ({ request }) => {
  if (!directory) return;
  const persistedKey = join(directory, 'e2e-api-key');
  key = (
    await readFile(join(directory, 'config/bootstrap-api-key'), 'utf8').catch(() =>
      readFile(persistedKey, 'utf8'),
    )
  ).trim();
  await writeFile(persistedKey, key, { mode: 0o600 });
  const platforms = (await authorized(request, 'platforms')) as { id: number; slug: string }[];
  platformId = platforms.find((item) => item.slug === 'nes')!.id;
  const profiles = (await authorized(request, 'profiles')) as {
    profile: { id: number; platformId: number };
  }[];
  profileId = profiles.find((item) => item.profile.platformId === platformId)?.profile.id ?? 0;
  if (!profileId) {
    const profile = await authorized(request, 'profiles', 'POST', {
      name: 'NES E2E',
      platformId,
      minimumScore: 0,
      allowUpgrades: true,
      upgradeUntilScore: 100,
      allowedRegions: [],
      excludedRegions: [],
      rejectBeta: true,
      rejectDemo: true,
      rejectPrototype: true,
      allowUpdates: true,
      allowDlc: true,
      formats: [],
    });
    profileId = profile.id;
  }
  const path = resolve(directory, `roms/e2e-${Date.now()}`);
  await mkdir(path, { recursive: true });
  const root = await authorized(request, 'root-folders', 'POST', { name: title, path });
  rootId = root.id;
});

test('production library, DAT scan, signed ROM download and all configuration routes', async ({
  page,
  request,
}, info) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy|Refused to|violates/i.test(message.text()))
      errors.push(message.text());
  });
  await signIn(page);
  const sidebar = page.locator('#sidebar');
  const main = page.getByRole('main');
  expect(await page.getByRole('banner').boundingBox()).toMatchObject({ x: 0, y: 0, height: 60 });
  expect(await sidebar.boundingBox()).toMatchObject({ x: 0, y: 60, width: 210 });
  expect(await main.boundingBox()).toMatchObject({ x: 210, y: 60 });
  await page.goto('/add');
  await page.getByLabel('Plattform', { exact: true }).selectOption(String(platformId));
  await page.getByRole('button', { name: 'Manuell hinzufügen' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Titel', { exact: true }).fill(title);
  await dialog.getByLabel('Root Folder', { exact: true }).selectOption(String(rootId));
  await dialog.getByLabel('Qualitätsprofil', { exact: true }).selectOption(String(profileId));
  await dialog.getByRole('button', { name: 'Hinzufügen', exact: true }).click();
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  const gameId = Number(new URL(page.url()).pathname.split('/').pop());
  await expect(
    page.locator('#sidebar').getByRole('link', { name: 'Bibliothek', exact: true }),
  ).toHaveAttribute('aria-current', 'location');
  await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Überwacht', { exact: true }).uncheck();
  await page.getByRole('dialog').getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByText('Nicht überwacht', { exact: true })).toBeVisible();
  const roots = (await authorized(request, 'root-folders')) as { id: number; path: string }[];
  const root = roots.find((item) => item.id === rootId)!;
  const bytes = Buffer.from('Ludus synthetic test file; no game content.');
  const filename = `${title} (Europe).nes`;
  await writeFile(join(root.path, filename), bytes);
  const sha1 = createHash('sha1').update(bytes).digest('hex');
  await page.goto('/settings/dat');
  await page.getByLabel('Plattform', { exact: true }).selectOption(String(platformId));
  await page.getByLabel('DAT-Datei', { exact: true }).setInputFiles({
    name: `${title}.dat`,
    mimeType: 'application/xml',
    buffer: Buffer.from(
      `<datafile><header><name>${title}</name></header><game name="${title}"><rom name="${filename}" size="${bytes.length}" sha1="${sha1}"/></game></datafile>`,
    ),
  });
  await page.getByRole('button', { name: 'Importieren', exact: true }).click();
  await expect(page.getByRole('cell', { name: title, exact: true })).toBeVisible();
  await page.goto('/');
  await page.getByRole('button', { name: 'Bibliothek scannen', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Root Folder', { exact: true }).selectOption(String(rootId));
  await page.getByRole('dialog').getByLabel('Plattform', { exact: true }).selectOption(String(platformId));
  await page.getByRole('button', { name: 'Scan starten', exact: true }).click();
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.goto(`/games/${gameId}`);
  await expect(page.getByText('Verifiziert [!]', { exact: true })).toBeVisible();
  await page.getByText('Prüfsummen anzeigen', { exact: true }).click();
  await expect(page.getByText(sha1, { exact: true })).toBeVisible();
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Herunterladen', exact: true }).click();
  const download = await downloadEvent;
  expect(download.url()).toContain('/api/v1/media/content?token=');
  expect(download.url()).not.toContain(key);
  expect(await readFile((await download.path())!)).toEqual(bytes);
  await page.screenshot({ path: info.outputPath('game-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: 'Interaktive Suche', exact: true }).click();
  await page.getByRole('button', { name: 'Releases suchen', exact: true }).click();
  await expect(page.getByText('Keine Releases gefunden', { exact: true })).toBeVisible();
  const collectionSearch = page.getByLabel('Spiele suchen', { exact: true });
  await collectionSearch.fill(title);
  await collectionSearch.press('Enter');
  await expect(page).toHaveURL((url) => url.pathname === '/' && url.searchParams.get('query') === title);
  await page.goto('/?status=available&page=2');
  await page.getByLabel('Spiele suchen', { exact: true }).fill(title);
  await expect(page).toHaveURL(
    (url) => url.searchParams.get('status') === 'available' && !url.searchParams.has('page'),
  );
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('library-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: 'Ansicht', exact: true }).click();
  await page.getByRole('button', { name: 'Tabelle', exact: true }).click();
  await expect(page.getByRole('cell', { name: title, exact: true })).toBeVisible();
  for (const path of [
    '/activity',
    '/activity/history',
    '/activity/blocklist',
    '/calendar',
    '/settings/media',
    '/settings/profiles',
    '/settings/formats',
    '/settings/indexers',
    '/settings/clients',
    '/settings/metadata',
    '/settings/connect',
    '/settings/ui',
    '/settings/general',
    '/system',
    '/system/tasks',
    '/system/backups',
    '/system/logs',
  ]) {
    await page.goto(path);
    await expect(page.locator('h1')).toBeVisible();
    await expect(page.getByText('Wird geladen …', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), path).toBeTruthy();
  }
  await page.goto('/settings/profiles');
  await page.screenshot({ path: info.outputPath('profiles-desktop.png'), fullPage: true });
  expect(errors).toEqual([]);
});

test('search results, grouped files and administration layouts at desktop and phone widths', async ({
  page,
  request,
}, info) => {
  await signIn(page);
  const sample = {
    title: 'Orbit',
    provider: 'Igdb',
    externalId: '42',
    year: 1996,
    developer: 'Demo Studio',
    genres: ['Adventure'],
    description: 'Eine Forschungsreise durch das Sonnensystem.',
    coverRelativePath: null,
  };
  const created = await authorized(request, 'games', 'POST', {
    title: title + ' Orbit',
    platformId,
    rootFolderId: rootId,
    qualityProfileId: profileId,
    monitored: true,
  });
  try {
    const detail = await authorized(request, 'games/' + created.id);
    await page.route('**/api/v1/metadata/search?*', (route) =>
      route.fulfill({ json: [sample, { ...sample, title: 'Orbit II', externalId: '43' }] }),
    );
    await page.route('**/api/v1/games/' + created.id, (route) =>
      route.fulfill({
        json: {
          ...detail,
          ...sample,
          status: 'Available',
          releaseYear: sample.year,
          files: ['Base', 'Update', 'Dlc'].map((contentKind, index) => ({
            id: index + 1,
            originalFileName: contentKind + '.rom',
            relativePath: 'Orbit/' + contentKind + '.rom',
            contentKind,
            missing: false,
            role: 'Rom',
            score: 100,
            verificationStatus: index ? 'Unverified' : 'VerifiedGoodDump',
            discNumber: index ? null : 1,
            size: 32768,
            regions: ['Europe'],
            languages: ['de'],
            importedAtUtc: '2026-01-01T12:00:00Z',
            crc32: 'B6C13F22',
          })),
        },
      }),
    );
    await page.route('**/api/v1/activity/queue', (route) =>
      route.fulfill({
        json: [
          {
            id: 41,
            gameId: created.id,
            releaseTitle: 'Orbit (Europe)',
            status: 'Importing',
            size: 1024,
            downloadedBytes: 1024,
            speedBytesPerSecond: 0,
            etaSeconds: null,
          },
        ],
      }),
    );
    for (const width of [2560, 1440, 390]) {
      await page.setViewportSize({ width, height: width === 2560 ? 1298 : 900 });
      const main = page.getByRole('main');
      await page.goto('/add');
      await page.getByLabel('Plattform', { exact: true }).selectOption(String(platformId));
      await page.getByLabel('Spieltitel suchen').fill('Orbit');
      const results = main.locator('article');
      await expect(results).toHaveCount(2);
      const first = (await results.first().boundingBox())!;
      expect((await results.last().boundingBox())!.y).toBeGreaterThanOrEqual(first.y + first.height);
      await page.screenshot({ path: info.outputPath('add-' + width + '.png'), animations: 'disabled' });
      await results.first().getByRole('button', { name: 'Hinzufügen' }).click();
      await expect(page.getByRole('dialog').getByLabel('Titel', { exact: true })).toHaveValue('Orbit');
      await expect(page.getByRole('dialog').getByText(sample.description)).toBeVisible();
      await page.getByRole('dialog').getByRole('button', { name: 'Abbrechen' }).click();
      await page.goto('/games/' + created.id);
      await expect(main.getByRole('heading', { name: 'Orbit', exact: true })).toBeVisible();
      await expect(main.getByText(sample.description)).toBeVisible();
      await expect(main.getByText('Base.rom', { exact: true })).toBeVisible();
      await main.locator('summary').filter({ hasText: 'Updates (1)' }).click();
      await expect(main.getByText('Update.rom', { exact: true })).toBeVisible();
      await expect(main.getByText('Dlc.rom', { exact: true })).toBeHidden();
      await page.screenshot({ path: info.outputPath('detail-' + width + '.png'), animations: 'disabled' });
      await main.getByRole('button', { name: 'Metadaten', exact: true }).click();
      const menu = await main.locator('#game-metadata').boundingBox();
      expect(menu!.x).toBeGreaterThanOrEqual(0);
      expect(menu!.x + menu!.width).toBeLessThanOrEqual(width + 1);
      await main.getByRole('button', { name: 'Metadaten verknüpfen' }).click();
      await expect(page.getByRole('dialog')).toBeVisible();
      await page.keyboard.press('Escape');
      await main.getByRole('button', { name: 'Spielverlauf' }).click();
      await expect(main.getByRole('heading', { name: 'Spielverlauf' })).toBeVisible();
      await page.goto('/activity');
      await expect(main.getByRole('progressbar')).toBeVisible();
      await expect(main.getByRole('button', { name: 'Download entfernen' })).toBeDisabled();
      const refresh = page.waitForResponse((response) => response.url().endsWith('/activity/queue'));
      await main.getByRole('button', { name: 'Aktualisieren' }).click();
      await refresh;
      await page.screenshot({ path: info.outputPath('queue-' + width + '.png'), animations: 'disabled' });
      await page.goto('/settings');
      await expect(main.getByRole('link')).toHaveCount(10);
      await page.screenshot({ path: info.outputPath('settings-' + width + '.png'), animations: 'disabled' });
      await main.getByRole('link', { name: /Medienverwaltung/ }).click();
      await expect(main.getByRole('heading', { name: 'Root Folders' })).toBeVisible();
      await page.goto('/system');
      await expect(main.getByRole('heading', { name: 'Systeminformationen' })).toBeVisible();
      await page.screenshot({ path: info.outputPath('system-' + width + '.png'), animations: 'disabled' });
      expect(await main.evaluate((el) => el.scrollWidth === el.clientWidth)).toBe(true);
    }
  } finally {
    await authorized(request, 'games/' + created.id, 'DELETE');
  }
});

test('backup download/import and API key rotation preserve a usable browser session', async ({
  page,
  request,
}) => {
  await signIn(page);
  await page.goto('/system/backups');
  const before = (await authorized(request, 'system/backups')) as { id: number }[];
  await page.getByRole('button', { name: 'Backup erstellen', exact: true }).click();
  await expect(page.getByText('Backup erstellt.', { exact: true })).toBeVisible();
  const after = (await authorized(request, 'system/backups')) as { id: number }[];
  const created = after.find((item) => !before.some((old) => old.id === item.id))!;
  expect(created).toBeDefined();
  const downloadPromise = page.waitForEvent('download');
  await page.locator(`a[href="/api/v1/system/backups/${created.id}/download"]`).click();
  const download = await downloadPromise;
  const zipPath = (await download.path())!;
  expect((await readFile(zipPath)).subarray(0, 2).toString()).toBe('PK');
  await page.getByLabel('Ludus-Backup importieren (max. 9 GiB)', { exact: true }).setInputFiles(zipPath);
  await page.getByRole('button', { name: 'Importieren', exact: true }).click();
  await expect(page.getByText('Backup importiert und geprüft.', { exact: true })).toBeVisible();
  await page.goto('/settings/general');
  await page.getByRole('button', { name: 'API-Key zurücksetzen', exact: true }).click();
  const rotatedResponse = page.waitForResponse(
    (response) => response.url().endsWith('/auth/api-key/reset') && response.request().method() === 'POST',
  );
  await page.getByRole('dialog').getByRole('button', { name: 'Bestätigen', exact: true }).click();
  const rotated = await rotatedResponse;
  expect(rotated.ok()).toBeTruthy();
  key = (await rotated.json()).apiKey;
  if (directory) await writeFile(join(directory, 'e2e-api-key'), key, { mode: 0o600 });
  await expect(page.getByRole('heading', { name: 'Willkommen zurück.', exact: true })).toBeVisible();
  await signIn(page);
  const diagnosticsPromise = page.waitForEvent('download');
  await page.goto('/system/logs');
  await page.getByRole('link', { name: 'Diagnose-ZIP', exact: true }).click();
  expect((await diagnosticsPromise).suggestedFilename()).toMatch(/^ludus-diagnostics-.*\.zip$/);
});

test('configuration writes and responsive English navigation', async ({ page }, info) => {
  await signIn(page);
  await page.goto('/settings/formats');
  await page.getByRole('button', { name: 'Format hinzufügen', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`${title} Europe`);
  await page.getByRole('dialog').getByLabel('Wert', { exact: true }).fill('Europe');
  await page.getByRole('dialog').getByRole('button', { name: 'Speichern', exact: true }).click();
  await expect(page.getByRole('cell', { name: `${title} Europe`, exact: true })).toBeVisible();
  await page.goto('/settings/profiles');
  await page.getByRole('button', { name: 'Profil hinzufügen', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`${title} Quality`);
  await page.getByRole('dialog').getByLabel('Plattform', { exact: true }).selectOption(String(platformId));
  await page.getByRole('dialog').getByRole('button', { name: 'Format hinzufügen', exact: true }).click();
  await page
    .getByRole('dialog')
    .getByLabel('Format', { exact: true })
    .selectOption({ label: `${title} Europe` });
  await page.getByRole('dialog').getByLabel('Score', { exact: true }).fill('100');
  await page.getByRole('dialog').getByRole('button', { name: 'Speichern', exact: true }).click();
  await expect(page.getByRole('heading', { name: `${title} Quality`, exact: true })).toBeVisible();
  await page.goto('/settings/general');
  await page.getByLabel('Instanzname', { exact: true }).fill(title);
  await page.getByRole('button', { name: 'Speichern', exact: true }).click();
  await expect(page.getByText('Einstellungen gespeichert.', { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByLabel('Instanzname', { exact: true })).toHaveValue(title);
  await page.getByRole('button', { name: 'Benutzermenü' }).click();
  await page.getByRole('button', { name: 'Sprache: Deutsch. Zu Englisch wechseln.' }).click();
  await expect(page.getByRole('heading', { name: 'Settings', exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  const openNavigation = page.getByRole('button', { name: 'Open navigation', exact: true });
  await openNavigation.click();
  await page.keyboard.press('Escape');
  await expect(openNavigation).toBeFocused();
  await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Library', exact: true })
    .click();
  await expect(page.getByRole('heading', { name: 'Library', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'View', exact: true }).click();
  await page.getByRole('button', { name: 'Posters', exact: true }).click();
  await expect(page.locator('#sidebar')).toHaveAttribute('aria-hidden', 'true');
  await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Library', exact: true })
    .click();
  await expect(page.locator('#sidebar')).toHaveAttribute('aria-hidden', 'true');
  await page.screenshot({
    path: info.outputPath('library-mobile.png'),
    fullPage: true,
    animations: 'disabled',
  });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.goto('/settings/profiles');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({
    path: info.outputPath('settings-mobile.png'),
    fullPage: true,
    animations: 'disabled',
  });
  const storage = await page.evaluate(() => ({ ...localStorage }));
  expect(Object.keys(storage).every((name) => ['ludus.locale', 'ludus.view'].includes(name))).toBeTruthy();
});

test('security changes end the old session and forms login uses the updated configuration', async ({
  page,
  request,
}) => {
  const original = await authorized(request, 'auth/security');
  const password = `Test-${Date.now()}-secret`;
  try {
    await signIn(page);
    await page.goto('/settings/general');
    await page.getByLabel('Anmeldemethode', { exact: true }).selectOption('Forms');
    await page.getByLabel('Benutzername', { exact: true }).fill(administrator);
    await page.getByLabel('Neues Passwort (leer = unverändert)', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Sicherheitseinstellungen speichern', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Bestätigen', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Willkommen zurück.', exact: true })).toBeVisible();
    await page.getByLabel('Benutzername', { exact: true }).fill(administrator);
    await page.getByLabel('Passwort', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Einstellungen', exact: true })).toBeVisible();
    await expect(page.getByText('Live verbunden', { exact: true })).toBeVisible();
  } finally {
    await authorized(request, 'auth/security', 'PUT', {
      ...original,
      username: administrator,
      authenticationMethod: 'Forms',
      password: administratorPassword,
    });
  }
});

test('library empty states, filters, views and pagination on desktop and mobile', async ({
  page,
  request,
}, info) => {
  // Keep the empty-state check repeatable on a reused isolated test instance.
  const listRoute = '**/api/v1/games?*';
  await page.route(listRoute, (route) =>
    route.fulfill({ json: { items: [], total: 0, page: 1, pageSize: 48 } }),
  );
  await signIn(page);
  const empty = page.locator('.library-empty');
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(empty.getByRole('heading', { name: 'Deine Bibliothek ist leer' })).toBeVisible();
    await expect(empty.getByRole('link', { name: 'Spiel hinzufügen' })).toHaveAttribute('href', '/add');
    await empty.getByRole('button', { name: 'Bibliothek scannen' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Abbrechen' }).click();
    await expect(page.getByRole('button', { name: 'Nächste' })).toHaveCount(0);
  }
  await page.unroute(listRoute);
  const prefix = `Raster ${Date.now()}`;
  const ids: number[] = [];
  try {
    for (let i = 1; i <= 49; i++) {
      const game = await authorized(request, 'games', 'POST', {
        title: `${prefix} · ${String(i).padStart(2, '0')}`,
        platformId,
        rootFolderId: rootId,
        qualityProfileId: profileId,
        monitored: i % 2 === 1,
      });
      ids.push(game.id);
    }
    for (const width of [2560, 1440, 390]) {
      await page.setViewportSize({ width, height: width === 2560 ? 1298 : 900 });
      await page.goto('/?' + new URLSearchParams({ query: prefix }));
      const toolbar = page.getByRole('group', { name: 'Bibliotheksaktionen' });
      const filters = page.getByRole('dialog', { name: 'Erweiterte Filter' });
      await toolbar.getByRole('button', { name: 'Ansicht', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Poster', exact: true }).click();
      await expect(page.locator('.game-card')).toHaveCount(48);
      await toolbar.getByRole('button', { name: 'Optionen' }).click();
      const options = page.getByRole('dialog');
      await options.getByLabel('Postergröße').selectOption('large');
      await options.getByLabel('Titel', { exact: true }).uncheck();
      await options.getByRole('button', { name: 'Fertig' }).click();
      await expect(page.locator('.poster-size-large')).toBeVisible();
      await expect(page.locator('.game-card h2')).toHaveCount(0);
      await toolbar.getByRole('button', { name: 'Optionen' }).click();
      await options.getByLabel('Titel', { exact: true }).check();
      await options.getByLabel('Postergröße').selectOption('medium');
      await options.getByRole('button', { name: 'Fertig' }).click();
      await toolbar.getByRole('button', { name: 'Sortierung' }).click();
      await toolbar.getByRole('button', { name: 'Titel', exact: true }).click();
      await expect(page.locator('.game-card h2').first()).toHaveText(prefix + ' · 49');
      await page.getByRole('button', { name: 'Nächste' }).click();
      await expect(page.locator('.game-card h2')).toHaveText(prefix + ' · 01');
      await toolbar.getByRole('button', { name: 'Sortierung' }).click();
      await toolbar.getByRole('button', { name: 'Titel', exact: true }).click();
      await expect(page.locator('.game-card h2').first()).toHaveText(prefix + ' · 01');
      await toolbar.getByRole('button', { name: 'Ansicht', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Übersicht', exact: true }).click();
      await expect(page.locator('.library-overview > a')).toHaveCount(48);
      await toolbar.getByRole('button', { name: 'Ansicht', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Poster', exact: true }).click();
      expect((await toolbar.boundingBox())?.y).toBe(60);
      await page.screenshot({ path: info.outputPath('library-posters-' + width + '.png') });
      await page.getByRole('button', { name: 'Nächste' }).click();
      await expect(page.locator('.game-card')).toHaveCount(1);
      await expect(page.locator('.game-card h2')).toHaveText(prefix + ' · 49');
      await page.getByRole('button', { name: 'Zurück', exact: true }).click();
      await expect(page.locator('.game-card')).toHaveCount(48);
      await toolbar.getByRole('button', { name: 'Filter', exact: true }).click();
      const filterTrigger = toolbar.getByRole('button', { name: 'Filter', exact: true });
      const menuBox = (await page.locator('#lib-filter').boundingBox())!;
      const triggerBox = (await filterTrigger.boundingBox())!;
      expect(Math.abs(menuBox.x + menuBox.width - triggerBox.x - triggerBox.width)).toBeLessThanOrEqual(1);
      expect(menuBox.x).toBeGreaterThanOrEqual(0);
      expect(menuBox.y + menuBox.height).toBeLessThanOrEqual(page.viewportSize()!.height);
      await page.screenshot({ path: info.outputPath('library-filter-' + width + '.png') });
      await toolbar.getByRole('button', { name: 'Überwacht', exact: true }).click();
      await expect(page.locator('.game-card')).toHaveCount(25);
      await filterTrigger.click();
      await toolbar.getByRole('button', { name: 'Erweiterte Filter …' }).click();
      await filters.getByLabel('Plattform', { exact: true }).selectOption(String(platformId));
      await filters.getByLabel('Root Folder', { exact: true }).selectOption(String(rootId));
      await filters.getByLabel('Status', { exact: true }).selectOption('unmonitored');
      await filters.getByLabel('Filtername').fill('Meine ROMs');
      await filters.getByRole('button', { name: 'Filter speichern' }).click();
      await expect(page.locator('.game-card')).toHaveCount(24);
      await toolbar.getByRole('button', { name: 'Filter', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Alle Spiele', exact: true }).click();
      await expect(page.locator('.game-card')).toHaveCount(48);
      await page.reload();
      await toolbar.getByRole('button', { name: 'Filter', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Meine ROMs', exact: true }).click();
      await expect(page.locator('.game-card')).toHaveCount(24);
      await toolbar.getByRole('button', { name: 'Filter', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Erweiterte Filter …' }).click();
      await filters.getByRole('button', { name: 'Filter entfernen Meine ROMs' }).click();
      await filters.getByLabel('Verifikation', { exact: true }).selectOption('VerifiedGoodDump');
      await filters.getByRole('button', { name: 'Anwenden' }).click();
      await expect(filters).toBeHidden();
      await expect(empty.getByRole('heading', { name: 'Keine passenden Spiele' })).toBeVisible();
      await empty.getByRole('button', { name: 'Filter zurücksetzen' }).click();
      await expect(page.getByLabel('Spiele suchen')).toHaveValue('');
      await expect(page).toHaveURL((url) => !url.search);
      await page.getByLabel('Spiele suchen').fill(prefix);
      await expect(page.locator('.game-card').first()).toContainText(prefix);
      await toolbar.getByRole('button', { name: 'Ansicht', exact: true }).click();
      await toolbar.getByRole('button', { name: 'Tabelle', exact: true }).click();
      await expect(page.locator('.library-table tbody tr')).toHaveCount(48);
      await toolbar.getByRole('button', { name: 'Optionen' }).click();
      await options.getByLabel('Jahr').uncheck();
      await options.getByRole('button', { name: 'Fertig' }).click();
      await expect(page.getByRole('columnheader', { name: 'Jahr' })).toHaveCount(0);
      expect(
        (await page.locator('.library-table tbody tr').first().boundingBox())!.height,
      ).toBeLessThanOrEqual(48);
      expect(await page.getByRole('main').evaluate((el) => el.scrollWidth === el.clientWidth)).toBe(true);
      await page.getByRole('button', { name: 'Nächste' }).click();
      await expect(page.locator('.library-table tbody tr')).toHaveCount(1);
      await page.getByRole('button', { name: 'Zurück', exact: true }).click();
      await expect(page.locator('.library-table tbody tr')).toHaveCount(48);
      await page.screenshot({ path: info.outputPath(`library-table-${width}.png`) });
    }
  } finally {
    for (const id of ids) await authorized(request, `games/${id}`, 'DELETE');
  }
});

test('settings tiles, contextual navigation and aligned fields', async ({ page }, info) => {
  await signIn(page);
  await page.setViewportSize({ width: 1546, height: 911 });
  const nav = page.getByRole('navigation', { name: 'Hauptnavigation' });
  await expect(nav.getByRole('link', { name: 'Verlauf', exact: true })).toHaveCount(0);
  await nav.getByRole('link', { name: 'Aktivität', exact: true }).click();
  await nav.getByRole('link', { name: 'Verlauf', exact: true }).click();
  await expect(page).toHaveURL(/activity\/history$/);
  await nav.getByRole('link', { name: 'Einstellungen', exact: true }).click();
  await nav.getByRole('link', { name: 'Indexer', exact: true }).click();
  const add = page.getByRole('button', { name: 'Hinzufügen', exact: true });
  await expect(add).toBeVisible();
  expect(await add.boundingBox()).toMatchObject({ x: 240, width: 290, height: 74 });
  await page.screenshot({ path: info.outputPath('indexers-desktop.png') });
  await add.click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Verwalten' }).click();
  await expect(page.getByRole('button', { name: 'Karten' })).toBeVisible();
  await nav.getByRole('link', { name: 'Allgemein & Sicherheit' }).click();
  const field = page.getByLabel('Instanzname', { exact: true });
  const fieldLabel = page.getByText('Instanzname', { exact: true });
  await expect(field).toBeVisible();
  expect(await field.boundingBox()).toMatchObject({ x: 500, width: 380, height: 35 });
  expect(await fieldLabel.boundingBox()).toMatchObject({ x: 230, width: 250 });
  await expect(page.getByRole('button', { name: 'Keine Änderungen' })).toBeDisabled();
  await page.screenshot({ path: info.outputPath('general-desktop.png') });
  await page.setViewportSize({ width: 390, height: 844 });
  const mobileLabel = (await fieldLabel.boundingBox())!;
  const mobileField = (await field.boundingBox())!;
  expect(mobileField.x).toBe(mobileLabel.x);
  expect(mobileField.y).toBeGreaterThanOrEqual(mobileLabel.y + mobileLabel.height);
  expect(mobileField.x + mobileField.width).toBeLessThanOrEqual(390);
  await page.screenshot({ path: info.outputPath('general-mobile.png') });
});

test('UI date preview and light, dark, and automatic themes react immediately', async ({ page }) => {
  await signIn(page);
  await page.goto('/settings/ui');
  const dates = page.locator('section.settings-section').filter({
    has: page.getByRole('heading', { name: 'Datumsanzeige', exact: true }),
  });
  await dates.getByLabel('Kurzes Datumsformat', { exact: true }).selectOption('yyyy-MM-dd');
  await expect(dates.getByText(/2026-11-23/)).toBeVisible();

  const appearance = page.locator('section.settings-section').filter({
    has: page.getByRole('heading', { name: 'Darstellung', exact: true }),
  });
  const appearanceForm = await appearance.locator('form').getAttribute('id');
  const saveAppearance = () => page.locator(`button[form="${appearanceForm}"]`).click();
  await appearance.getByLabel('Darstellung', { exact: true }).selectOption('Dark');
  await saveAppearance();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await appearance.getByLabel('Darstellung', { exact: true }).selectOption('Light');
  await saveAppearance();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  await page.emulateMedia({ colorScheme: 'dark' });
  await appearance.getByLabel('Darstellung', { exact: true }).selectOption('Auto');
  await saveAppearance();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
});
