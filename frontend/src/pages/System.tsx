import { useEffect, useState } from 'react';
import { Download, Play, RefreshCw, Trash2 } from 'lucide-react';
import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { api, query } from '../api';
import { useResource } from '../hooks';
import { bytes, date, useLabel, useLocale } from '../i18n';
import {
    Action,
    Badge,
    Button,
    Confirm,
    Empty,
    ErrorBox,
    Field,
    Heading,
    Loading,
    Modal,
    useToast,
} from '../ui';
import type { HealthIssue, Row } from '../types';
import { ScanDialog } from './Library';

export default function SystemPage() {
    const { t } = useLocale();
    return (
        <>
            <Heading
                title={t('System', 'System')}
                subtitle={t('Ein Blick unter die Haube.', 'A look under the hood.')}
            />
            <nav className="tabs" aria-label={t('Systemansichten', 'System views')}>
                <NavLink end to="/system">
                    {t('Status & Zustand', 'Status & health')}
                </NavLink>
                <NavLink to="/system/tasks">{t('Aufgaben', 'Tasks')}</NavLink>
                <NavLink to="/system/backups">Backups</NavLink>
                <NavLink to="/system/logs">Logs</NavLink>
            </nav>
            <Routes>
                <Route index element={<Health />} />
                <Route path="tasks" element={<Tasks />} />
                <Route path="backups" element={<Backups />} />
                <Route path="logs" element={<Logs />} />
                <Route path="*" element={<Navigate replace to="/system" />} />
            </Routes>
        </>
    );
}
const healthMessages: Record<string, [string, string]> = {
    RootNotWritable: ['Root Folder ist nicht beschreibbar.', 'Root folder is not writable.'],
    NoDownloadClient: ['Kein aktiver Download-Client konfiguriert.', 'No active download client configured.'],
    DownloadClientUnavailable: ['Download-Client ist nicht erreichbar.', 'Download client is unavailable.'],
    NoRssIndexer: ['Kein aktiver RSS-Indexer konfiguriert.', 'No active RSS indexer configured.'],
    IndexerUnavailable: ['Indexer ist nicht erreichbar.', 'Indexer is unavailable.'],
    MetadataUnavailable: [
        'Metadaten-Provider ist nicht erreichbar. Zugangsdaten prüfen.',
        'Metadata provider is unavailable. Check credentials.',
    ],
    MetadataNotConfigured: [
        'Metadaten-Provider ist nicht konfiguriert.',
        'Metadata provider is not configured.',
    ],
    MetadataSystemMappingMissing: [
        'Für diese Plattform fehlt die Provider-System-ID.',
        'This platform is missing its provider system ID.',
    ],
    RemotePathMappingMissing: [
        'Ein Download-Pfad ist lokal nicht erreichbar. Remote Path Mapping prüfen.',
        'A download path cannot be accessed locally. Check remote path mappings.',
    ],
};
function Health() {
    const { t, locale } = useLocale();
    const toast = useToast();
    const health = useResource<HealthIssue[]>('system/health', 60000);
    const info = useResource<Record<string, string>>('system/info');
    return (
        <>
            <div className="section-heading">
                <h2>{t('Systemzustand', 'System health')}</h2>
                <Action
                    run={async () => {
                        await api('system/health/check', { method: 'POST' });
                        toast(t('Systemprüfung abgeschlossen.', 'Health check complete.'));
                    }}
                >
                    <RefreshCw size={17} />
                    {t('Jetzt prüfen', 'Check now')}
                </Action>
            </div>
            <ErrorBox error={health.error || info.error} retry={health.reload} />
            {health.loading && !health.data ? (
                <Loading />
            ) : health.data?.length === 0 ? (
                <div className="notice success">
                    {t('Keine aktiven Systemwarnungen.', 'No active health warnings.')}
                </div>
            ) : (
                <div className="health-list">
                    {health.data?.map((issue) => (
                        <article className="panel health-item" key={issue.id}>
                            <Badge value={issue.severity} />
                            <div>
                                <h3>
                                    {healthMessages[issue.code]
                                        ? t(...healthMessages[issue.code])
                                        : issue.message}
                                </h3>
                                <p className="muted">
                                    {issue.resourceKey} · {date(issue.lastCheckedAtUtc, locale)}
                                </p>
                                <small className="mono">{issue.code}</small>
                            </div>
                        </article>
                    ))}
                </div>
            )}
            <section className="settings-section">
                <h2>{t('Systeminformationen', 'System information')}</h2>
                {info.data && (
                    <dl className="system-facts panel">
                        {[
                            ['version', t('Version', 'Version')],
                            ['runtime', '.NET Runtime'],
                            ['os', t('Betriebssystem', 'Operating system')],
                            ['database', t('Datenbank', 'Database')],
                            ['journalMode', t('Journal-Modus', 'Journal mode')],
                            ['schema', t('Schema', 'Schema')],
                            ['startedAtUtc', t('Gestartet', 'Started')],
                        ].map(([key, name]) => (
                            <div key={key}>
                                <dt>{name}</dt>
                                <dd>
                                    {key === 'startedAtUtc'
                                        ? date(info.data?.[key], locale)
                                        : info.data?.[key]}
                                </dd>
                            </div>
                        ))}
                    </dl>
                )}
            </section>
        </>
    );
}
function Tasks() {
    const { t, locale } = useLocale();
    const label = useLabel();
    const toast = useToast();
    const data = useResource<{ scheduled: Row[]; pending: string[]; recent: Row[] }>('system/tasks', 15000);
    const [scan, setScan] = useState(false);
    return (
        <>
            <div className="section-heading">
                <h2>{t('Geplante Aufgaben', 'Scheduled tasks')}</h2>
                <Button onClick={() => setScan(true)}>
                    {t('Gezielter Bibliothek-Scan', 'Targeted library scan')}
                </Button>
            </div>
            <ErrorBox error={data.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>{t('Aufgabe', 'Task')}</th>
                                <th>Status</th>
                                <th>{t('Zuletzt abgeschlossen', 'Last completed')}</th>
                                <th>{t('Nächster Lauf', 'Next run')}</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.scheduled.map((task) => (
                                <tr key={task.id}>
                                    <td>{label(String(task.kind))}</td>
                                    <td>
                                        <Badge tone={task.enabled ? 'good' : ''}>
                                            {task.enabled ? t('Aktiv', 'Enabled') : t('Inaktiv', 'Disabled')}
                                        </Badge>
                                        {data.data?.pending.includes(String(task.kind)) && (
                                            <Badge value="Queued" />
                                        )}
                                    </td>
                                    <td>{date(task.lastCompletedAtUtc, locale)}</td>
                                    <td>{task.enabled ? date(task.nextRunAtUtc, locale) : '—'}</td>
                                    <td>
                                        <div className="actions">
                                            <Action
                                                disabled={data.data?.pending.includes(String(task.kind))}
                                                run={async () => {
                                                    const result = await api<{ queued: boolean }>(
                                                        `system/tasks/${task.kind}/run`,
                                                        { method: 'POST' },
                                                    );
                                                    toast(
                                                        result.queued
                                                            ? t('Aufgabe eingereiht.', 'Task queued.')
                                                            : t(
                                                                  'Aufgabe ist bereits eingereiht oder aktiv.',
                                                                  'Task is already queued or running.',
                                                              ),
                                                    );
                                                }}
                                            >
                                                <Play size={16} />
                                                {t('Starten', 'Run')}
                                            </Action>
                                            <Action
                                                run={() =>
                                                    api(`system/tasks/${task.kind}`, {
                                                        method: 'PUT',
                                                        body: { enabled: !task.enabled },
                                                    })
                                                }
                                            >
                                                {task.enabled
                                                    ? t('Deaktivieren', 'Disable')
                                                    : t('Aktivieren', 'Enable')}
                                            </Action>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            <section className="settings-section">
                <h2>{t('Letzte Ausführungen', 'Recent runs')}</h2>
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>{t('Aufgabe', 'Task')}</th>
                                <th>Status</th>
                                <th>{t('Start', 'Started')}</th>
                                <th>{t('Ende', 'Finished')}</th>
                                <th>{t('Fehlercode', 'Error code')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.recent.map((run) => (
                                <tr key={run.id}>
                                    <td>
                                        {label(
                                            String(
                                                data.data?.scheduled.find(
                                                    (task) => task.id === run.scheduledTaskId,
                                                )?.kind ?? run.scheduledTaskId,
                                            ),
                                        )}
                                    </td>
                                    <td>
                                        <Badge value={String(run.status)} />
                                    </td>
                                    <td>{date(run.startedAtUtc, locale)}</td>
                                    <td>{date(run.finishedAtUtc, locale)}</td>
                                    <td>{String(run.errorCode ?? '—')}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </section>
            {scan && <ScanDialog onClose={() => setScan(false)} />}
        </>
    );
}
function Backups() {
    const { t, locale } = useLocale();
    const toast = useToast();
    const data = useResource<Row[]>('system/backups');
    const [remove, setRemove] = useState<Row>();
    const [restore, setRestore] = useState<Row>();
    const [restoring, setRestoring] = useState(false);
    const [file, setFile] = useState<File>();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    const [inputKey, setInputKey] = useState(0);
    return (
        <>
            <div className="section-heading">
                <div>
                    <h2>{t('Sicherungen', 'Backups')}</h2>
                    <p className="muted">
                        {t(
                            'Sichert Datenbank, Konfiguration und Schlüssel. ROM-Dateien sind nicht enthalten.',
                            'Backs up the database, configuration, and keys. ROM files are not included.',
                        )}
                    </p>
                </div>
                <Action
                    variant="primary"
                    run={async () => {
                        await api('system/backups', { method: 'POST' });
                        toast(t('Backup erstellt.', 'Backup created.'));
                    }}
                >
                    <PlusIcon />
                    {t('Backup erstellen', 'Create backup')}
                </Action>
            </div>
            <form
                className="panel upload-form"
                onSubmit={async (event) => {
                    event.preventDefault();
                    if (!file) return;
                    setBusy(true);
                    setError(undefined);
                    try {
                        await api('system/backups/import', { method: 'POST', body: file, raw: true });
                        setFile(undefined);
                        setInputKey((value) => value + 1);
                        toast(t('Backup importiert und geprüft.', 'Backup imported and validated.'));
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                <Field label={t('Ludus-Backup importieren (max. 9 GiB)', 'Import Ludus backup (max. 9 GiB)')}>
                    <input
                        key={inputKey}
                        type="file"
                        required
                        accept=".zip,application/zip"
                        onChange={(event) => {
                            const selected = event.target.files?.[0];
                            event.target.setCustomValidity(
                                selected && selected.size > 9663676416
                                    ? t('Maximal 9 GiB.', 'Maximum 9 GiB.')
                                    : '',
                            );
                            setFile(selected);
                        }}
                    />
                </Field>
                <Button type="submit" busy={busy}>
                    {t('Importieren', 'Import')}
                </Button>
            </form>
            <ErrorBox error={error || data.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <Empty title={t('Noch keine Backups', 'No backups yet')} />
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>{t('Sicherung', 'Backup')}</th>
                                <th>{t('Erstellt', 'Created')}</th>
                                <th>{t('Größe', 'Size')}</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.map((item) => (
                                <tr key={item.id}>
                                    <td className="title-cell">
                                        {String(item.relativePath)}
                                        <small>
                                            {item.scheduled
                                                ? t('Automatisch', 'Scheduled')
                                                : t('Manuell', 'Manual')}{' '}
                                            · v{String(item.applicationVersion)}
                                        </small>
                                    </td>
                                    <td>{date(item.createdAtUtc, locale)}</td>
                                    <td>{bytes(Number(item.size), locale)}</td>
                                    <td>
                                        <div className="actions">
                                            <a
                                                className="button"
                                                href={`/api/v1/system/backups/${item.id}/download`}
                                                download
                                            >
                                                <Download size={17} />
                                                {t('Herunterladen', 'Download')}
                                            </a>
                                            <Button onClick={() => setRestore(item)}>
                                                {t('Wiederherstellen', 'Restore')}
                                            </Button>
                                            <Button
                                                className="icon-button"
                                                aria-label={t('Backup entfernen', 'Delete backup')}
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
                    title={t('Backup endgültig entfernen?', 'Permanently delete backup?')}
                    text={String(remove.relativePath)}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(`system/backups/${remove.id}`, { method: 'DELETE' })}
                />
            )}
            {restore && (
                <Confirm
                    title={t('Backup wiederherstellen?', 'Restore backup?')}
                    text={`${String(restore.relativePath)}. ${t('Ersetzt Datenbank, Konfiguration und Schlüssel beim nächsten Start. Ludus wird beendet; Docker/systemd muss den Dienst neu starten. Aktuelle Einstellungen werden überschrieben, ROM-Dateien bleiben erhalten.', 'Replaces the database, configuration, and keys on next startup. Ludus will stop; Docker/systemd must restart it. Current settings are overwritten; ROM files are retained.')}`}
                    onClose={() => setRestore(undefined)}
                    onConfirm={async () => {
                        await api(`system/backups/${restore.id}/restore`, { method: 'POST' });
                        setRestoring(true);
                    }}
                />
            )}
            {restoring && <RestoreStatus />}
        </>
    );
}
function PlusIcon() {
    return (
        <span aria-hidden="true" className="plus-icon">
            +
        </span>
    );
}
function RestoreStatus() {
    const { t } = useLocale();
    const [ready, setReady] = useState(false);
    useEffect(() => {
        const controller = new AbortController();
        let inFlight = false;
        const start = Date.now();
        const timer = setInterval(async () => {
            if (inFlight || Date.now() - start < 5000) return;
            inFlight = true;
            try {
                const response = await fetch('/api/v1/system/status', {
                    cache: 'no-store',
                    signal: controller.signal,
                });
                if (response.ok && !controller.signal.aborted) setReady(true);
            } catch {
                /* The server is expected to be offline during restore. */
            } finally {
                inFlight = false;
            }
        }, 3000);
        return () => {
            clearInterval(timer);
            controller.abort();
        };
    }, []);
    return (
        <Modal title={t('Wiederherstellung vorbereitet', 'Restore staged')} busy onClose={() => {}}>
            <p>
                {ready
                    ? t(
                          'Der Server antwortet wieder. Melde dich mit den Zugangsdaten der Sicherung an.',
                          'The server is responding again. Sign in with the credentials from the backup.',
                      )
                    : t(
                          'Warte auf den Neustart. Falls kein Prozessmanager aktiv ist, starte Ludus auf dem Host neu.',
                          'Waiting for restart. If no process manager is active, restart Ludus on the host.',
                      )}
            </p>
            <Button variant="primary" disabled={!ready} onClick={() => location.reload()}>
                {t('Erneut anmelden', 'Sign in again')}
            </Button>
        </Modal>
    );
}
function Logs() {
    const { t } = useLocale();
    const [debug, setDebug] = useState(false);
    const [level, setLevel] = useState('');
    const [filter, setFilter] = useState('');
    const [auto, setAuto] = useState(false);
    const data = useResource<string[]>(
        `system/logs?${query({ debug: String(debug), level, limit: 1000 })}`,
        auto ? 10000 : 0,
    );
    return (
        <>
            <div className="section-heading">
                <h2>{t('Protokolle', 'Logs')}</h2>
                <div className="actions">
                    <Button onClick={data.reload}>
                        <RefreshCw size={17} />
                        {t('Aktualisieren', 'Refresh')}
                    </Button>
                    <a className="button" href="/api/v1/system/diagnostics" download>
                        <Download size={17} />
                        {t('Diagnose-ZIP', 'Diagnostics ZIP')}
                    </a>
                </div>
            </div>
            <div className="filters">
                <select
                    aria-label={t('Protokolldatei', 'Log file')}
                    value={debug ? 'debug' : 'normal'}
                    onChange={(event) => setDebug(event.target.value === 'debug')}
                >
                    <option value="normal">ludus.txt</option>
                    <option value="debug">ludus.debug.txt</option>
                </select>
                <select
                    aria-label={t('Protokollstufe', 'Log level')}
                    value={level}
                    onChange={(event) => setLevel(event.target.value)}
                >
                    <option value="">{t('Alle Stufen', 'All levels')}</option>
                    {['DBG', 'INF', 'WRN', 'ERR', 'FTL'].map((item) => (
                        <option key={item}>{item}</option>
                    ))}
                </select>
                <input
                    aria-label={t('Logs filtern', 'Filter logs')}
                    placeholder={t('Text filtern …', 'Filter text…')}
                    value={filter}
                    onChange={(event) => setFilter(event.target.value)}
                />
                <label className="check">
                    <input
                        type="checkbox"
                        checked={auto}
                        onChange={(event) => setAuto(event.target.checked)}
                    />
                    {t('Live aktualisieren', 'Auto refresh')}
                </label>
            </div>
            <ErrorBox error={data.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : (
                <pre className="log-view" tabIndex={0} aria-label={t('Protokolleinträge', 'Log entries')}>
                    {data.data
                        ?.filter((line) => line.toLocaleLowerCase().includes(filter.toLocaleLowerCase()))
                        .join('\n') || t('Keine passenden Einträge.', 'No matching entries.')}
                </pre>
            )}
            <p className="hint">
                {t(
                    'Die letzten 1.000 passenden Einträge. Der Diagnose-Export enthält bereinigte Logs ohne gespeicherte Secrets.',
                    'The last 1,000 matching entries. Diagnostics include redacted logs without stored secrets.',
                )}
            </p>
        </>
    );
}
