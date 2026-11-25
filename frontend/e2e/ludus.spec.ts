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
  await page.getByLabel('API-Key', { exact: true }).fill(key);
  await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Bibliothek', exact: true })).toBeVisible();
  await expect(page.getByText('Live verbunden', { exact: true })).toBeVisible();
}
test.beforeAll(async ({ request }) => {
  if (!directory) return;
  key = (await readFile(join(directory, 'config/bootstrap-api-key'), 'utf8')).trim();
  const platforms = (await authorized(request, 'platforms')) as { id: number; slug: string }[];
  platformId = platforms.find((item) => item.slug === 'nes')!.id;
  const profiles = (await authorized(request, 'profiles')) as { profile: { id: number } }[];
  profileId = profiles[0].profile.id;
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
  await page.getByRole('tab', { name: 'Interaktive Suche', exact: true }).click();
  await page.getByRole('button', { name: 'Releases suchen', exact: true }).click();
  await expect(page.getByText('Keine Releases gefunden', { exact: true })).toBeVisible();
  await page.goto('/');
  await page.getByLabel('Spiele suchen', { exact: true }).fill(title);
  await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
  await page.screenshot({ path: info.outputPath('library-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: 'Tabellen-Ansicht', exact: true }).click();
  await expect(page.getByRole('cell', { name: title, exact: true })).toBeVisible();
  for (const path of [
    '/activity',
    '/activity/history',
    '/activity/blocklist',
    '/settings/media',
    '/settings/profiles',
    '/settings/indexers',
    '/settings/clients',
    '/settings/metadata',
    '/settings/connect',
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
  if (directory) await writeFile(join(directory, 'config/bootstrap-api-key'), key, { mode: 0o600 });
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await expect(page.getByLabel('API-Key', { exact: true })).toHaveValue(key);
  await expect(page.getByText('Live verbunden', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Verbergen', exact: true }).click();
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Bibliothek', exact: true })).toBeVisible();
  const diagnosticsPromise = page.waitForEvent('download');
  await page.goto('/system/logs');
  await page.getByRole('link', { name: 'Diagnose-ZIP', exact: true }).click();
  expect((await diagnosticsPromise).suggestedFilename()).toMatch(/^ludus-diagnostics-.*\.zip$/);
});

test('configuration writes and responsive English navigation', async ({ page }, info) => {
  await signIn(page);
  await page.goto('/settings/profiles');
  await page.getByRole('button', { name: 'Format hinzufügen', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`${title} Europe`);
  await page.getByRole('dialog').getByLabel('Wert', { exact: true }).fill('Europe');
  await page.getByRole('dialog').getByRole('button', { name: 'Speichern', exact: true }).click();
  await expect(page.getByRole('cell', { name: `${title} Europe`, exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Profil hinzufügen', exact: true }).click();
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`${title} Quality`);
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
  await page.getByRole('button', { name: 'Sprache: Deutsch. Zu Englisch wechseln.' }).click();
  await expect(page.getByRole('heading', { name: 'Settings', exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
  await page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Library', exact: true })
    .click();
  await expect(page.getByRole('heading', { name: 'Library', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Poster view', exact: true }).click();
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
    await page.getByLabel('Benutzeranmeldung aktivieren', { exact: true }).check();
    await page.getByLabel('Benutzername', { exact: true }).fill('ludus-e2e');
    await page.getByLabel('Neues Passwort (leer = unverändert)', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Sicherheitseinstellungen speichern', exact: true }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Bestätigen', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Willkommen zurück.', exact: true })).toBeVisible();
    await page.getByLabel('Benutzername', { exact: true }).fill('ludus-e2e');
    await page.getByLabel('Passwort', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Anmelden', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Einstellungen', exact: true })).toBeVisible();
    await expect(page.getByText('Live verbunden', { exact: true })).toBeVisible();
  } finally {
    await authorized(request, 'auth/security', 'PUT', { ...original, password: null });
    if (!original.formsAuthenticationEnabled && directory)
      await writeFile(join(directory, 'config/bootstrap-api-key'), key, { mode: 0o600 });
  }
});
