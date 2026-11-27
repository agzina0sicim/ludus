import { useId, useState, type ReactNode } from 'react';
import { Link, Navigate, Route, Routes } from 'react-router-dom';
import { Plus, Pencil, Trash2, Save } from 'lucide-react';
import { api, resetCsrf } from '../api';
import { useResource } from '../hooks';
import { date, dateOnly, timeOnly, useLabel, useLocale } from '../i18n';
import { CrudSection, Editor, Fields, payload, type FormField, type Values } from '../forms';
import { SettingsLayout, SettingsActions } from '../SettingsToolbar';
import * as schema from '../settings-schema';
import { useSettingsSections } from '../settings-sections';
import { Action, Badge, Button, Confirm, Empty, ErrorBox, Field, Loading, Modal, useToast } from '../ui';
import type { Platform, Profile, Row } from '../types';

export default function SettingsPage() {
    const { t } = useLocale();
    return (
        <section className="admin-page compact-page">
            <SettingsLayout title={t('Einstellungen', 'Settings')}>
                <Routes>
                    <Route index element={<SettingsDirectory />} />
                    <Route path="media" element={<MediaSettings />} />
                    <Route path="dat" element={<DatSettings />} />
                    <Route path="profiles" element={<ProfileSettings view="profiles" />} />
                    <Route path="formats" element={<ProfileSettings view="formats" />} />
                    <Route path="indexers" element={<Indexers />} />
                    <Route path="clients" element={<Clients />} />
                    <Route path="metadata" element={<MetadataSettings />} />
                    <Route path="connect" element={<ConnectSettings />} />
                    <Route path="ui" element={<UiSettings />} />
                    <Route path="general" element={<GeneralSettings />} />
                    <Route path="*" element={<Navigate replace to="/settings/media" />} />
                </Routes>
            </SettingsLayout>
        </section>
    );
}
function SettingsDirectory() {
    const sections = useSettingsSections();
    return (
        <div className="settings-directory">
            {sections.map(([slug, name, description]) => (
                <Link key={slug} to={slug}>
                    <h2>{name}</h2>
                    <p>{description}</p>
                </Link>
            ))}
        </div>
    );
}
function MediaSettings() {
    const { t } = useLocale();
    const [monitor, setMonitor] = useState<Row>();
    const capabilities = useResource<{ permissionsSupported: boolean; permissionPlatform: string }>(
        'settings/capabilities',
    );
    return (
        <>
            <CrudSection
                title="Root Folders"
                description={t(
                    'Bestehende, absolute Ordnerpfade auf dem Ludus-Host. Jedes Spiel erhält einen Root Folder.',
                    'Existing absolute directories on the Ludus host. Each game is assigned a root folder.',
                )}
                endpoint="root-folders"
                fields={schema.rootFields}
                defaults={{ name: '', path: '' }}
                columns={[
                    { key: 'name', label: t('Name', 'Name') },
                    { key: 'path', label: t('Pfad', 'Path') },
                ]}
            />
            <SettingsForm
                title={t('Import & Benennung', 'Import & naming')}
                fields={schema.mediaFields}
                hint={t(
                    'Tokens: {RootFolder}, {Game Title}, {Year}, {Region}, {ext}. Hardlinks erhalten Torrent-Dateien zum Seeden.',
                    'Tokens: {RootFolder}, {Game Title}, {Year}, {Region}, {ext}. Hardlinks retain torrent files for seeding.',
                )}
            />
            <p className="hint">
                {capabilities.data?.permissionsSupported
                    ? t(
                          'Datei- und Ordnerrechte sowie die optionale Gruppe wirken auf diesem Linux-System. Bei Hardlinks bleiben Dateirechte und Gruppe unverändert, damit die Quelldatei nicht verändert wird.',
                          'File and directory permissions and the optional group are active on this Linux system. For hardlinks, file mode and group remain unchanged so the source is not modified.',
                      )
                    : t(
                          'Dateirechte und Mediengruppe sind auf diesem System nicht verfügbar; die Prozess-UMASK gilt.',
                          'File permissions and media group are unavailable on this system; the process umask applies.',
                      )}
            </p>
            <CrudSection
                title={t('Plattformen', 'Platforms')}
                endpoint="platforms"
                fields={schema.platformFields}
                defaults={schema.platformDefaults}
                columns={[
                    { key: 'name', label: t('Name', 'Name') },
                    { key: 'allowedExtensions', label: t('Dateiendungen', 'Extensions') },
                    { key: 'monitored', label: t('Überwachung', 'Monitoring') },
                ]}
                extra={(row) => (
                    <Button onClick={() => setMonitor(row)}>
                        {row.monitored
                            ? t('Alle deaktivieren', 'Unmonitor all')
                            : t('Alle überwachen', 'Monitor all')}
                    </Button>
                )}
            />
            {monitor && (
                <Confirm
                    title={t('Plattform-Überwachung ändern?', 'Change platform monitoring?')}
                    text={t(
                        'Ändert den Überwachungsstatus der Plattform und aller zugehörigen Spiele.',
                        'Changes monitoring for the platform and all its games.',
                    )}
                    onClose={() => setMonitor(undefined)}
                    onConfirm={() =>
                        api(`platforms/${monitor.id}/monitoring`, {
                            method: 'PUT',
                            body: { monitored: !monitor.monitored },
                        })
                    }
                />
            )}
        </>
    );
}
function SettingsForm({
    title,
    fields,
    hint,
    preview,
}: {
    title: string;
    fields: FormField[];
    hint?: string;
    preview?: (values: Values) => ReactNode;
}) {
    const { t, setLocale } = useLocale();
    const formId = useId();
    const data = useResource<Row>('settings');
    const toast = useToast();
    const [changes, setChanges] = useState<Values>({});
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <section className="settings-section">
            <h2>{title}</h2>
            <SettingsActions>
                <Button
                    type="submit"
                    form={formId}
                    disabled={busy || !Object.keys(changes).length}
                    busy={busy}
                >
                    <Save size={20} />
                    {Object.keys(changes).length
                        ? t('Speichern', 'Save')
                        : t('Keine Änderungen', 'No changes')}
                </Button>
            </SettingsActions>
            {hint && <p className="muted">{hint}</p>}
            <ErrorBox error={error || data.error} retry={data.reload} />
            {data.data ? (
                <form
                    id={formId}
                    className="settings-form"
                    onSubmit={async (event) => {
                        event.preventDefault();
                        setBusy(true);
                        setError(undefined);
                        try {
                            const current = await api<Row>('settings');
                            const merged = { ...current, ...changes };
                            await api('settings', {
                                method: 'PUT',
                                body: payload(
                                    [
                                        ...schema.mediaFields,
                                        ...schema.generalFields,
                                        ...schema.metadataLanguageFields,
                                        ...schema.indexerPolicyFields,
                                        ...schema.downloadHandlingFields,
                                        ...schema.dateDisplayFields,
                                        ...schema.appearanceFields,
                                        ...schema.languageFields,
                                    ],
                                    merged,
                                ),
                            });
                            setChanges({});
                            if ('language' in changes) setLocale(merged.language === 'en' ? 'en' : 'de');
                            toast(t('Einstellungen gespeichert.', 'Settings saved.'));
                        } catch (err) {
                            setError(err);
                        } finally {
                            setBusy(false);
                        }
                    }}
                >
                    <fieldset disabled={busy}>
                        <Fields
                            fields={fields}
                            values={{ ...data.data, ...changes }}
                            onChange={(next) => {
                                const changed = Object.fromEntries(
                                    fields
                                        .filter((field) => next[field.key] !== data.data![field.key])
                                        .map((field) => [field.key, next[field.key]]),
                                );
                                setChanges(changed);
                            }}
                        />
                        {preview?.({ ...data.data, ...changes })}
                    </fieldset>
                </form>
            ) : (
                data.loading && <Loading />
            )}
        </section>
    );
}
function Indexers() {
    const { t } = useLocale();
    return (
        <>
            <CrudSection
                title={t('Indexer', 'Indexers')}
                cards
                endpoint="indexers"
                fields={schema.indexerFields}
                defaults={schema.indexerDefaults}
                columns={[
                    { key: 'name', label: 'Name' },
                    { key: 'kind', label: t('Typ', 'Type') },
                    { key: 'baseUrl', label: 'URL' },
                    { key: 'enableRss', label: 'RSS' },
                ]}
                testEndpoint={(row) => `indexers/${row.id}/test`}
            />
            <SettingsForm title={t('Auswahl & RSS', 'Selection & RSS')} fields={schema.indexerPolicyFields} />
        </>
    );
}
function Clients() {
    const { t } = useLocale();
    return (
        <>
            <CrudSection
                title={t('Download-Clients', 'Download clients')}
                cards
                endpoint="download-clients"
                fields={schema.clientFields}
                defaults={schema.clientDefaults}
                columns={[
                    { key: 'name', label: 'Name' },
                    { key: 'kind', label: 'Client' },
                    { key: 'baseUrl', label: 'URL' },
                    { key: 'enabled', label: 'Status' },
                ]}
                testEndpoint={(row) => `download-clients/${row.id}/test`}
            />
            <SettingsForm title="Completed Download Handling" fields={schema.downloadHandlingFields} />
            <CrudSection
                title="Remote Path Mappings"
                endpoint="remote-path-mappings"
                fields={schema.mappingFields}
                defaults={{ host: '', remotePath: '', localPath: '' }}
                columns={[
                    { key: 'host', label: 'Host' },
                    { key: 'remotePath', label: t('Remote-Pfad', 'Remote path') },
                    { key: 'localPath', label: t('Lokaler Pfad', 'Local path') },
                ]}
            />
        </>
    );
}
function MetadataSettings() {
    const { t } = useLocale();
    const [systems, setSystems] = useState<{ provider: string; items: { id: number; name: string }[] }>();
    return (
        <>
            <SettingsForm
                title={t('Metadatensprache', 'Metadata language')}
                fields={schema.metadataLanguageFields}
                hint={t(
                    'Diese Auswahl ist unabhängig von der Sprache der Oberfläche. Feldweise Fallbacks bleiben in den Spieldetails sichtbar.',
                    'This selection is independent of the interface language. Per-field fallbacks remain visible in game details.',
                )}
            />
            <CrudSection
                title={t('Metadaten-Provider', 'Metadata providers')}
                description={t(
                    'IGDB: Twitch Client-ID und Secret. ScreenScraper: Entwickler-ID und Passwort sowie optional ein Benutzerkonto. Änderungen vor dem Test speichern.',
                    'IGDB: Twitch client ID and secret. ScreenScraper: developer ID and password, plus an optional user account. Save changes before testing.',
                )}
                cards
                endpoint="metadata/providers"
                fields={schema.providerFields}
                defaults={{}}
                allowCreate={false}
                idKey="provider"
                columns={[
                    { key: 'provider', label: 'Provider' },
                    { key: 'enabled', label: 'Status' },
                    { key: 'cacheDurationHours', label: t('Cache (Stunden)', 'Cache (hours)') },
                ]}
                testEndpoint={(row) => `metadata/${row.provider}/test`}
                extra={(row) => (
                    <Action
                        run={async () => {
                            const items = await api<{ id: number; name: string }[]>(
                                `metadata/${row.provider}/systems`,
                            );
                            setSystems({ provider: String(row.provider), items });
                        }}
                    >
                        {t('System-IDs', 'System IDs')}
                    </Action>
                )}
            />
            <div className="panel">
                <h2>{t('Priorität pro Plattform', 'Priority per platform')}</h2>
                <p className="muted">
                    {t(
                        'Primärer Provider, Fallback und System-IDs werden direkt an der Plattform konfiguriert.',
                        'Configure the primary provider, fallback, and system IDs on each platform.',
                    )}
                </p>
                <Link className="button" to="/settings/media">
                    {t('Plattformen verwalten', 'Manage platforms')}
                </Link>
            </div>
            {systems && (
                <Modal
                    title={`${systems.provider} · ${t('System-IDs', 'System IDs')}`}
                    onClose={() => setSystems(undefined)}
                >
                    <div className="table-wrap">
                        <table>
                            <thead>
                                <tr>
                                    <th>ID</th>
                                    <th>Name</th>
                                </tr>
                            </thead>
                            <tbody>
                                {systems.items.map((item) => (
                                    <tr key={item.id}>
                                        <td>{item.id}</td>
                                        <td>{item.name}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </Modal>
            )}
        </>
    );
}
function DatSettings() {
    const { t, locale } = useLocale();
    const toast = useToast();
    const data = useResource<Row[]>('dat-files');
    const platforms = useResource<Platform[]>('platforms');
    const [platform, setPlatform] = useState('');
    const [file, setFile] = useState<File>();
    const [inputKey, setInputKey] = useState(0);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    const [remove, setRemove] = useState<Row>();
    return (
        <section className="settings-section">
            <h2>{t('DAT-Dateien', 'DAT files')}</h2>
            <p className="muted">
                {t(
                    'Importiere No-Intro- oder Redump-Dateien im Logiqx-XML-Format (max. 256 MiB). Nach einem Import prüft ein Bibliothek-Scan deine Dateien erneut.',
                    'Import No-Intro or Redump files in Logiqx XML format (max. 256 MiB). Run a library scan after import to recheck your files.',
                )}
            </p>
            <form
                className="panel upload-form"
                onSubmit={async (event) => {
                    event.preventDefault();
                    if (!file) return;
                    setBusy(true);
                    setError(undefined);
                    try {
                        const body = new FormData();
                        body.append('platformId', platform);
                        body.append('file', file);
                        const result = await api<{ games: number; roms: number; alreadyImported: boolean }>(
                            'dat-files',
                            { method: 'POST', body },
                        );
                        toast(
                            result.alreadyImported
                                ? t(
                                      'Diese DAT-Datei wurde bereits importiert.',
                                      'This DAT file was already imported.',
                                  )
                                : `${result.games} ${t('Spiele', 'games')} · ${result.roms} ROMs`,
                        );
                        setFile(undefined);
                        setInputKey((value) => value + 1);
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                <Field label={t('Plattform', 'Platform')}>
                    <select required value={platform} onChange={(event) => setPlatform(event.target.value)}>
                        <option value="">{t('Auswählen', 'Select')}</option>
                        {platforms.data?.map((item) => (
                            <option key={item.id} value={item.id}>
                                {item.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label={t('DAT-Datei', 'DAT file')}>
                    <input
                        key={inputKey}
                        type="file"
                        accept=".dat"
                        required
                        onChange={(event) => {
                            const selected = event.target.files?.[0];
                            if (selected && selected.size > 268435456) {
                                event.target.setCustomValidity(t('Maximal 256 MiB.', 'Maximum 256 MiB.'));
                                setFile(undefined);
                            } else {
                                event.target.setCustomValidity('');
                                setFile(selected);
                            }
                        }}
                    />
                </Field>
                <Button variant="primary" type="submit" busy={busy}>
                    {t('Importieren', 'Import')}
                </Button>
            </form>
            <ErrorBox error={error || data.error || platforms.error} retry={data.reload} />
            {!data.data && data.loading ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <Empty title={t('Noch keine DAT-Dateien', 'No DAT files yet')} />
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>{t('Plattform', 'Platform')}</th>
                                <th>ROMs</th>
                                <th>{t('Importiert', 'Imported')}</th>
                                <th>Status</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.map((item) => (
                                <tr key={item.id}>
                                    <td className="title-cell">
                                        {String(item.name)}
                                        <small>{String(item.datVersion ?? '')}</small>
                                    </td>
                                    <td>{platforms.data?.find((p) => p.id === item.platformId)?.name}</td>
                                    <td>{String(item.roms)}</td>
                                    <td>{date(item.importedAtUtc, locale)}</td>
                                    <td>
                                        <Badge tone={item.enabled ? 'good' : ''}>
                                            {item.enabled ? t('Aktiv', 'Enabled') : t('Inaktiv', 'Disabled')}
                                        </Badge>
                                    </td>
                                    <td>
                                        <div className="actions">
                                            <Action
                                                run={() =>
                                                    api(`dat-files/${item.id}`, {
                                                        method: 'PUT',
                                                        body: { enabled: !item.enabled },
                                                    })
                                                }
                                            >
                                                {item.enabled
                                                    ? t('Deaktivieren', 'Disable')
                                                    : t('Aktivieren', 'Enable')}
                                            </Action>
                                            <Button
                                                className="icon-button"
                                                aria-label={t('DAT entfernen', 'Remove DAT')}
                                                onClick={() => setRemove(item)}
                                            >
                                                <Trash2 size={17} />
                                            </Button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {remove && (
                <Confirm
                    title={t('DAT-Datei entfernen?', 'Remove DAT file?')}
                    text={t(
                        'Entfernt die Prüfsummen aus der Datenbank. ROM-Dateien bleiben erhalten.',
                        'Removes checksums from the database. ROM files are retained.',
                    )}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(`dat-files/${remove.id}`, { method: 'DELETE' })}
                />
            )}
        </section>
    );
}
type FormatScore = { customFormatId: number; score: number };
type FormatRule = { field: string; operator: string; value: string; negate: boolean; required: boolean };
function ProfileSettings({ view }: { view: 'profiles' | 'formats' }) {
    const { t } = useLocale();
    const label = useLabel();
    const profiles = useResource<Profile[]>('profiles');
    const formats = useResource<Row[]>('custom-formats');
    const platforms = useResource<Platform[]>('platforms');
    const [editProfile, setEditProfile] = useState<Values>();
    const [editFormat, setEditFormat] = useState<Values>();
    const [remove, setRemove] = useState<{ path: string; name: string }>();
    const defaultRule: FormatRule = {
        field: 'Region',
        operator: 'Equals',
        value: '',
        negate: false,
        required: false,
    };
    return (
        <div className={`profile-settings-view ${view}`}>
            <section className="settings-section profiles-section">
                <div className="section-heading">
                    <div>
                        <h2>{t('Qualitätsprofile', 'Quality profiles')}</h2>
                        <p className="muted">
                            {t(
                                'Mindest-Score, Upgrade-Cutoff und gewichtete Formate bestimmen die Release-Auswahl.',
                                'Minimum score, upgrade cutoff, and weighted formats determine release selection.',
                            )}
                        </p>
                    </div>
                    <Button
                        onClick={() =>
                            setEditProfile({
                                name: '',
                                platformId: platforms.data?.[0]?.id ?? '',
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
                            })
                        }
                    >
                        <Plus size={17} />
                        {t('Profil hinzufügen', 'Add profile')}
                    </Button>
                </div>
                <ErrorBox error={profiles.error || formats.error} retry={profiles.reload} />
                {profiles.loading && !profiles.data && <Loading />}
                <div className="profile-grid">
                    {profiles.data?.map(({ profile, formats: scores }) => (
                        <article className="panel" key={profile.id}>
                            <div className="section-heading">
                                <h3>{profile.name}</h3>
                                <div className="actions">
                                    <Button
                                        className="icon-button"
                                        aria-label={`${t('Bearbeiten', 'Edit')}: ${profile.name}`}
                                        onClick={() => setEditProfile({ ...profile, formats: scores })}
                                    >
                                        <Pencil size={17} />
                                    </Button>
                                    <Button
                                        className="icon-button"
                                        aria-label={`${t('Entfernen', 'Remove')}: ${profile.name}`}
                                        onClick={() =>
                                            setRemove({ path: `profiles/${profile.id}`, name: profile.name })
                                        }
                                    >
                                        <Trash2 size={17} />
                                    </Button>
                                </div>
                            </div>
                            <dl className="facts">
                                <div>
                                    <dt>{t('Plattform', 'Platform')}</dt>
                                    <dd>
                                        {platforms.data?.find((x) => x.id === profile.platformId)?.name ??
                                            `#${profile.platformId}`}
                                    </dd>
                                </div>
                                <div>
                                    <dt>{t('Minimum', 'Minimum')}</dt>
                                    <dd>{profile.minimumScore}</dd>
                                </div>
                                <div>
                                    <dt>Cutoff</dt>
                                    <dd>{profile.upgradeUntilScore}</dd>
                                </div>
                            </dl>
                            <Badge tone={profile.allowUpgrades ? 'good' : ''}>
                                {profile.allowUpgrades
                                    ? t('Upgrades aktiviert', 'Upgrades enabled')
                                    : t('Upgrades deaktiviert', 'Upgrades disabled')}
                            </Badge>
                            <ul className="score-list">
                                {scores.map((score) => (
                                    <li key={score.customFormatId}>
                                        <span>
                                            {String(
                                                formats.data?.find((item) => item.id === score.customFormatId)
                                                    ?.name ?? `#${score.customFormatId}`,
                                            )}
                                        </span>
                                        <strong>
                                            {score.score > 0 ? '+' : ''}
                                            {score.score}
                                        </strong>
                                    </li>
                                ))}
                            </ul>
                        </article>
                    ))}
                </div>
            </section>
            <section className="settings-section formats-section">
                <div className="section-heading">
                    <h2>Custom Formats</h2>
                    <Button
                        onClick={() =>
                            setEditFormat({ name: '', matchMode: 'All', rules: [{ ...defaultRule }] })
                        }
                    >
                        <Plus size={17} />
                        {t('Format hinzufügen', 'Add format')}
                    </Button>
                </div>
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>{t('Verknüpfung', 'Match mode')}</th>
                                <th>{t('Regeln', 'Rules')}</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {formats.data?.map((format) => (
                                <tr key={format.id}>
                                    <td>{String(format.name)}</td>
                                    <td>{label(String(format.matchMode))}</td>
                                    <td>{(format.rules as unknown[]).length}</td>
                                    <td>
                                        <div className="actions">
                                            <Button
                                                className="icon-button"
                                                aria-label={`${t('Bearbeiten', 'Edit')}: ${format.name}`}
                                                onClick={() => setEditFormat({ ...format })}
                                            >
                                                <Pencil size={17} />
                                            </Button>
                                            <Button
                                                className="icon-button"
                                                aria-label={`${t('Entfernen', 'Remove')}: ${format.name}`}
                                                onClick={() =>
                                                    setRemove({
                                                        path: `custom-formats/${format.id}`,
                                                        name: String(format.name),
                                                    })
                                                }
                                            >
                                                <Trash2 size={17} />
                                            </Button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </section>
            {view === 'profiles' && editProfile && (
                <Editor
                    title={t('Qualitätsprofil', 'Quality profile')}
                    initial={editProfile}
                    fields={schema.profileFields}
                    onClose={() => setEditProfile(undefined)}
                    onSave={(values) =>
                        api(editProfile.id ? `profiles/${editProfile.id}` : 'profiles', {
                            method: editProfile.id ? 'PUT' : 'POST',
                            body: {
                                ...payload(schema.profileFields, values),
                                platformId: Number(values.platformId),
                                formats: (values.formats as FormatScore[]).map((item) => ({
                                    customFormatId: Number(item.customFormatId),
                                    score: Number(item.score),
                                })),
                            },
                        })
                    }
                >
                    {(values, setValues) => (
                        <div className="rule-editor">
                            <Field label={t('Plattform', 'Platform')}>
                                <select
                                    required
                                    value={String(values.platformId ?? '')}
                                    onChange={(event) =>
                                        setValues({ ...values, platformId: Number(event.target.value) })
                                    }
                                >
                                    <option value="" disabled>
                                        {t('Auswählen', 'Select')}
                                    </option>
                                    {platforms.data?.map((platform) => (
                                        <option key={platform.id} value={platform.id}>
                                            {platform.name}
                                        </option>
                                    ))}
                                </select>
                            </Field>
                            <h3>{t('Format-Scores', 'Format scores')}</h3>
                            {(values.formats as FormatScore[]).map((item, index) => (
                                <div className="score-editor" key={index}>
                                    <Field label={t('Format', 'Format')}>
                                        <select
                                            required
                                            value={item.customFormatId || ''}
                                            onChange={(event) =>
                                                setValues({
                                                    ...values,
                                                    formats: (values.formats as FormatScore[]).map(
                                                        (score, i) =>
                                                            i === index
                                                                ? {
                                                                      ...score,
                                                                      customFormatId: Number(
                                                                          event.target.value,
                                                                      ),
                                                                  }
                                                                : score,
                                                    ),
                                                })
                                            }
                                        >
                                            <option value="" disabled>
                                                {t('Auswählen', 'Select')}
                                            </option>
                                            {formats.data?.map((format) => (
                                                <option
                                                    disabled={(values.formats as FormatScore[]).some(
                                                        (score, i) =>
                                                            i !== index && score.customFormatId === format.id,
                                                    )}
                                                    key={format.id}
                                                    value={format.id}
                                                >
                                                    {String(format.name)}
                                                </option>
                                            ))}
                                        </select>
                                    </Field>
                                    <Field label="Score">
                                        <input
                                            type="number"
                                            required
                                            min={-2147483648}
                                            max={2147483647}
                                            value={item.score}
                                            onChange={(event) =>
                                                setValues({
                                                    ...values,
                                                    formats: (values.formats as FormatScore[]).map(
                                                        (score, i) =>
                                                            i === index
                                                                ? { ...score, score: event.target.value }
                                                                : score,
                                                    ),
                                                })
                                            }
                                        />
                                    </Field>
                                    <Button
                                        aria-label={t('Format entfernen', 'Remove format')}
                                        onClick={() =>
                                            setValues({
                                                ...values,
                                                formats: (values.formats as FormatScore[]).filter(
                                                    (_, i) => i !== index,
                                                ),
                                            })
                                        }
                                    >
                                        <Trash2 size={17} />
                                    </Button>
                                </div>
                            ))}
                            <Button
                                disabled={
                                    (values.formats as FormatScore[]).length >= (formats.data?.length ?? 0)
                                }
                                onClick={() =>
                                    setValues({
                                        ...values,
                                        formats: [
                                            ...(values.formats as FormatScore[]),
                                            { customFormatId: 0, score: 0 },
                                        ],
                                    })
                                }
                            >
                                <Plus size={17} />
                                {t('Format hinzufügen', 'Add format')}
                            </Button>
                        </div>
                    )}
                </Editor>
            )}
            {view === 'formats' && editFormat && (
                <Editor
                    title="Custom Format"
                    initial={editFormat}
                    fields={schema.formatFields}
                    onClose={() => setEditFormat(undefined)}
                    onSave={(values) =>
                        api(editFormat.id ? `custom-formats/${editFormat.id}` : 'custom-formats', {
                            method: editFormat.id ? 'PUT' : 'POST',
                            body: {
                                ...payload(schema.formatFields, values),
                                rules: (values.rules as FormatRule[]).map(
                                    ({ field, operator, value, negate, required }) => ({
                                        field,
                                        operator,
                                        value,
                                        negate,
                                        required,
                                    }),
                                ),
                            },
                        })
                    }
                >
                    {(values, setValues) => (
                        <div className="rule-editor">
                            <h3>{t('Regeln', 'Rules')}</h3>
                            {(values.rules as FormatRule[]).map((rule, index) => (
                                <div className="rule-card" key={index}>
                                    <Fields
                                        fields={[
                                            schema.field('field', 'Feld', 'Field', {
                                                type: 'select',
                                                options: [
                                                    'Region',
                                                    'Language',
                                                    'Extension',
                                                    'ReleaseTitle',
                                                    'Revision',
                                                    'Status',
                                                    'ContentKind',
                                                    'Serial',
                                                    'TitleId',
                                                ],
                                            }),
                                            schema.field('operator', 'Operator', 'Operator', {
                                                type: 'select',
                                                options: [
                                                    'Equals',
                                                    'Contains',
                                                    'Regex',
                                                    'GreaterThanOrEqual',
                                                ],
                                            }),
                                            schema.field('value', 'Wert', 'Value', {
                                                required: true,
                                                maxLength: 2000,
                                            }),
                                            schema.field('negate', 'Negieren', 'Negate', { type: 'boolean' }),
                                            schema.field(
                                                'required',
                                                'Zwingend erforderlich',
                                                'Required match',
                                                { type: 'boolean' },
                                            ),
                                        ]}
                                        values={rule as unknown as Values}
                                        onChange={(next) =>
                                            setValues({
                                                ...values,
                                                rules: (values.rules as FormatRule[]).map((item, i) =>
                                                    i === index ? next : item,
                                                ),
                                            })
                                        }
                                    />
                                    <Button
                                        disabled={(values.rules as FormatRule[]).length <= 1}
                                        onClick={() =>
                                            setValues({
                                                ...values,
                                                rules: (values.rules as FormatRule[]).filter(
                                                    (_, i) => i !== index,
                                                ),
                                            })
                                        }
                                    >
                                        {t('Regel entfernen', 'Remove rule')}
                                    </Button>
                                </div>
                            ))}
                            <Button
                                disabled={(values.rules as FormatRule[]).length >= 100}
                                onClick={() =>
                                    setValues({
                                        ...values,
                                        rules: [...(values.rules as FormatRule[]), { ...defaultRule }],
                                    })
                                }
                            >
                                <Plus size={17} />
                                {t('Regel hinzufügen', 'Add rule')}
                            </Button>
                        </div>
                    )}
                </Editor>
            )}
            {remove && (
                <Confirm
                    title={t('Eintrag entfernen?', 'Remove entry?')}
                    text={`${remove.name}. ${t('Verknüpfte Spiele oder Profile können diese Aktion verhindern.', 'Linked games or profiles may prevent this action.')}`}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(remove.path, { method: 'DELETE' })}
                />
            )}
        </div>
    );
}
function ConnectSettings() {
    const { t } = useLocale();
    const label = useLabel();
    const toast = useToast();
    const data = useResource<Row[]>('webhooks');
    const [edit, setEdit] = useState<Values>();
    const [remove, setRemove] = useState<Row>();
    return (
        <section className="settings-section">
            <div className="section-heading">
                <div>
                    <h2>{t('Benachrichtigungen', 'Notifications')}</h2>
                    <p className="muted">
                        {t(
                            'Discord, Gotify, Telegram und generische HTTP-Webhooks. Ein Test sendet eine echte Testnachricht.',
                            'Discord, Gotify, Telegram, and generic HTTP webhooks. Testing sends a real test notification.',
                        )}
                    </p>
                </div>
                <Button
                    onClick={() =>
                        setEdit({
                            name: '',
                            kind: 'Discord',
                            enabled: true,
                            events: ['Grabbed', 'Imported', 'ImportFailed', 'Upgraded', 'DownloadFailed'],
                        })
                    }
                >
                    <Plus size={17} />
                    {t('Webhook hinzufügen', 'Add webhook')}
                </Button>
            </div>
            <ErrorBox error={data.error} retry={data.reload} />
            {!data.data && data.loading ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <Empty title={t('Noch keine Verbindungen', 'No connections yet')} />
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>{t('Typ', 'Type')}</th>
                                <th>{t('Ereignisse', 'Events')}</th>
                                <th>Status</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.map((item) => (
                                <tr key={item.id}>
                                    <td>{String(item.name)}</td>
                                    <td>{label(String(item.kind))}</td>
                                    <td>{(item.events as string[]).map(label).join(', ')}</td>
                                    <td>
                                        <Badge tone={item.enabled ? 'good' : ''}>
                                            {item.enabled ? t('Aktiv', 'Enabled') : t('Inaktiv', 'Disabled')}
                                        </Badge>
                                    </td>
                                    <td>
                                        <div className="actions">
                                            <Action
                                                run={async () => {
                                                    await api(`webhooks/${item.id}/test`, { method: 'POST' });
                                                    toast(
                                                        t(
                                                            'Testnachricht gesendet.',
                                                            'Test notification sent.',
                                                        ),
                                                    );
                                                }}
                                            >
                                                {t('Testen', 'Test')}
                                            </Action>
                                            <Button
                                                className="icon-button"
                                                aria-label={t('Bearbeiten', 'Edit')}
                                                onClick={() => setEdit(item)}
                                            >
                                                <Pencil size={17} />
                                            </Button>
                                            <Button
                                                className="icon-button"
                                                aria-label={t('Entfernen', 'Remove')}
                                                onClick={() => setRemove(item)}
                                            >
                                                <Trash2 size={17} />
                                            </Button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {edit && (
                <Editor
                    title="Webhook"
                    initial={edit}
                    fields={schema.webhookFields}
                    onClose={() => setEdit(undefined)}
                    onSave={(values) =>
                        api(edit.id ? `webhooks/${edit.id}` : 'webhooks', {
                            method: edit.id ? 'PUT' : 'POST',
                            body: { ...payload(schema.webhookFields, values), events: values.events },
                        })
                    }
                >
                    {(values, setValues) => (
                        <>
                            <fieldset className="events-fieldset">
                                <legend>{t('Ereignisse', 'Events')}</legend>
                                {['Grabbed', 'Imported', 'ImportFailed', 'Upgraded', 'DownloadFailed'].map(
                                    (event) => (
                                        <label key={event} className="check">
                                            <input
                                                type="checkbox"
                                                checked={(values.events as string[]).includes(event)}
                                                onChange={(e) =>
                                                    setValues({
                                                        ...values,
                                                        events: e.target.checked
                                                            ? [...(values.events as string[]), event]
                                                            : (values.events as string[]).filter(
                                                                  (item) => item !== event,
                                                              ),
                                                    })
                                                }
                                            />
                                            {label(event)}
                                        </label>
                                    ),
                                )}
                            </fieldset>
                            <details>
                                <summary>
                                    {t('Payload für generische Webhooks', 'Generic webhook payload')}
                                </summary>
                                <pre className="mono">{`{"eventType":"Imported","occurredAtUtc":"2026-11-27T12:00:00Z","game":{"title":"Example"},"release":{"title":"Example (Europe)"}}`}</pre>
                            </details>
                        </>
                    )}
                </Editor>
            )}
            {remove && (
                <Confirm
                    title={t('Webhook entfernen?', 'Remove webhook?')}
                    text={String(remove.name)}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(`webhooks/${remove.id}`, { method: 'DELETE' })}
                />
            )}
        </section>
    );
}
function GeneralSettings() {
    const { t } = useLocale();
    return (
        <>
            <SettingsForm
                title={t('Allgemein & Zeitpläne', 'General & schedules')}
                fields={schema.generalFields}
            />
            <SecuritySettings />
            <ApiKeySettings />
        </>
    );
}
function UiSettings() {
    const { t, locale } = useLocale();
    const example = new Date('2026-11-23T17:45:00Z');
    return (
        <>
            <SettingsForm
                title={t('Datumsanzeige', 'Date display')}
                fields={schema.dateDisplayFields}
                preview={(values) => {
                    const display = {
                        shortDateFormat: String(values.shortDateFormat),
                        longDateFormat: String(values.longDateFormat),
                        use12HourTime: Boolean(values.use12HourTime),
                    };
                    return (
                        <div className="panel">
                            <h3>{t('Vorschau', 'Preview')}</h3>
                            <p>
                                {dateOnly(example, locale, 'short', display)} ·{' '}
                                {dateOnly(example, locale, 'long', display)} ·{' '}
                                {timeOnly(example, locale, display)}
                            </p>
                        </div>
                    );
                }}
            />
            <SettingsForm title={t('Darstellung', 'Appearance')} fields={schema.appearanceFields} />
            <SettingsForm
                title={t('Sprache', 'Language')}
                fields={schema.languageFields}
                hint={t(
                    'Die Metadatensprache bleibt bei einem Wechsel unverändert.',
                    'The metadata language remains unchanged when switching.',
                )}
            />
        </>
    );
}
function SecuritySettings() {
    const { t } = useLocale();
    const data = useResource<Values>('auth/security');
    const [changes, setChanges] = useState<Values>({});
    const [password, setPassword] = useState('');
    const [confirm, setConfirm] = useState(false);
    const values = { ...data.data, ...changes };
    return (
        <section className="settings-section">
            <h2>{t('Anmeldung & Reverse Proxy', 'Login & reverse proxy')}</h2>
            <p className="muted">
                {t(
                    'Das Speichern meldet alle Sitzungen ab und widerruft bestehende Medien-Links. Nur private Netze und Loopback sind als Proxy-Netze zulässig.',
                    'Saving signs out all sessions and revokes existing media links. Only private and loopback proxy networks are allowed.',
                )}
            </p>
            <ErrorBox error={data.error} retry={data.reload} />
            {data.data ? (
                <form
                    className="settings-form"
                    onSubmit={(event) => {
                        event.preventDefault();
                        setConfirm(true);
                    }}
                >
                    <Fields fields={schema.securityFields} values={values} onChange={setChanges} />
                    <Field
                        label={t('Neues Passwort (leer = unverändert)', 'New password (empty = unchanged)')}
                        hint={t(
                            'Mindestens 12 Zeichen. Bei neuem Benutzer ist ein neues Passwort erforderlich.',
                            'At least 12 characters. A new username requires a new password.',
                        )}
                    >
                        <input
                            type="password"
                            autoComplete="new-password"
                            minLength={12}
                            maxLength={1024}
                            required={values.username !== data.data.username}
                            value={password}
                            onChange={(event) => setPassword(event.target.value)}
                        />
                    </Field>
                    <div className="section-footer">
                        <Button type="submit" variant="primary">
                            {t('Sicherheitseinstellungen speichern', 'Save security settings')}
                        </Button>
                    </div>
                </form>
            ) : (
                data.loading && <Loading />
            )}
            {confirm && (
                <Confirm
                    title={t('Speichern und abmelden?', 'Save and sign out?')}
                    text={t(
                        'Alle Browser müssen sich danach neu anmelden. Basic zeigt den nativen Browserdialog und darf nur über HTTPS verwendet werden.',
                        'All browsers must sign in again. Basic uses the native browser dialog and must only be used over HTTPS.',
                    )}
                    onClose={() => setConfirm(false)}
                    onConfirm={async () => {
                        await api('auth/security', {
                            method: 'PUT',
                            body: { ...payload(schema.securityFields, values), password: password || null },
                        });
                        setPassword('');
                        resetCsrf();
                        window.dispatchEvent(new Event('ludus:unauthorized'));
                    }}
                />
            )}
        </section>
    );
}
function ApiKeySettings() {
    const { t } = useLocale();
    const toast = useToast();
    const [key, setKey] = useState('');
    const [confirm, setConfirm] = useState(false);
    return (
        <section className="settings-section">
            <h2>API-Key</h2>
            <p className="muted">
                {t(
                    'Ausschließlich für externe Integrationen im X-Api-Key-Header. Der Browser-Login verwendet immer das Administratorkonto. Ein Reset widerruft den alten Key, alle Sitzungen und Medien-Links.',
                    'Only for external integrations through the X-Api-Key header. Browser sign-in always uses the administrator account. Reset revokes the old key, all sessions, and media links.',
                )}
            </p>
            <div className="panel">
                <div className="actions">
                    {key ? (
                        <>
                            <Field label="API-Key">
                                <input
                                    readOnly
                                    value={key}
                                    autoComplete="off"
                                    onFocus={(event) => event.target.select()}
                                />
                            </Field>
                            <Action
                                run={async () => {
                                    await navigator.clipboard.writeText(key);
                                    toast(t('API-Key kopiert.', 'API key copied.'));
                                }}
                            >
                                {t('Kopieren', 'Copy')}
                            </Action>
                            <Button onClick={() => setKey('')}>{t('Verbergen', 'Hide')}</Button>
                        </>
                    ) : (
                        <Action
                            run={async () => {
                                setKey((await api<{ apiKey: string }>('auth/api-key')).apiKey);
                            }}
                        >
                            {t('API-Key anzeigen', 'Show API key')}
                        </Action>
                    )}
                    <Button variant="danger" onClick={() => setConfirm(true)}>
                        {t('API-Key zurücksetzen', 'Reset API key')}
                    </Button>
                </div>
            </div>
            {confirm && (
                <Confirm
                    title={t('API-Key wirklich zurücksetzen?', 'Reset API key?')}
                    text={t(
                        'Externe Anwendungen benötigen anschließend den neuen Key.',
                        'External applications will need the new key.',
                    )}
                    onClose={() => setConfirm(false)}
                    onConfirm={async () => {
                        const result = await api<{ apiKey: string }>('auth/api-key/reset', {
                            method: 'POST',
                        });
                        setKey(result.apiKey);
                        toast(
                            t(
                                'Neuer API-Key erstellt. Externe Anwendungen jetzt aktualisieren.',
                                'New API key created. Update external applications now.',
                            ),
                        );
                        resetCsrf();
                        window.dispatchEvent(new Event('ludus:unauthorized'));
                    }}
                />
            )}
        </section>
    );
}
