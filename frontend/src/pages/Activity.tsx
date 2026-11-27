import { useState } from 'react';
import { Link, Navigate, Route, Routes } from 'react-router-dom';
import { Download, RefreshCw, Trash2 } from 'lucide-react';
import { api } from '../api';
import { useResource } from '../hooks';
import { bytes, date, useLocale } from '../i18n';
import { Badge, Button, Confirm, ErrorBox, Loading, Pager } from '../ui';
import type { History, Page, QueueItem, Row } from '../types';

export default function ActivityPage() {
    return (
        <section className="admin-page compact-page">
            <Routes>
                <Route index element={<Queue />} />
                <Route path="history" element={<HistoryTable />} />
                <Route path="blocklist" element={<Blocklist />} />
                <Route path="*" element={<Navigate replace to="/activity" />} />
            </Routes>
        </section>
    );
}
function ListToolbar({
    title,
    reload,
    embedded = false,
}: {
    title: string;
    reload: () => void;
    embedded?: boolean;
}) {
    const { t } = useLocale();
    return (
        <header className={embedded ? 'section-heading' : 'view-toolbar'}>
            {embedded ? <h3>{title}</h3> : <h1 className="sr-only">{title}</h1>}
            <Button onClick={reload}>
                <RefreshCw size={16} />
                {t('Aktualisieren', 'Refresh')}
            </Button>
        </header>
    );
}
function Queue() {
    const { t, locale } = useLocale();
    const data = useResource<QueueItem[]>('activity/queue', 15000);
    const [remove, setRemove] = useState<QueueItem>();
    return (
        <>
            <ListToolbar title={t('Warteschlange', 'Queue')} reload={data.reload} />
            <ErrorBox error={data.error} retry={data.reload} />
            {!data.data && data.loading ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <p className="compact-empty">{t('Die Warteschlange ist leer.', 'The queue is empty.')}</p>
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>{t('Release', 'Release')}</th>
                                <th>Status</th>
                                <th>{t('Fortschritt', 'Progress')}</th>
                                <th>{t('Geschwindigkeit', 'Speed')}</th>
                                <th>ETA</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.map((item) => (
                                <tr key={item.id}>
                                    <td className="title-cell">
                                        <Link to={`/games/${item.gameId}`}>{item.releaseTitle}</Link>
                                        {item.failureCode && (
                                            <small className="danger-text">{item.failureCode}</small>
                                        )}
                                        {item.cleanupPending && (
                                            <small>{t('Bereinigung ausstehend', 'Cleanup pending')}</small>
                                        )}
                                        {item.replacementSearchPending && (
                                            <small>
                                                {t('Ersatzsuche ausstehend', 'Replacement search pending')}
                                            </small>
                                        )}
                                    </td>
                                    <td>
                                        <Badge value={item.status} />
                                    </td>
                                    <td>
                                        <progress
                                            aria-label={t('Download-Fortschritt', 'Download progress')}
                                            max={Math.max(1, item.size)}
                                            value={Math.max(0, Math.min(item.downloadedBytes, item.size))}
                                        />
                                        <small>
                                            {bytes(item.downloadedBytes, locale)} / {bytes(item.size, locale)}
                                        </small>
                                    </td>
                                    <td>{bytes(item.speedBytesPerSecond, locale)}/s</td>
                                    <td>
                                        {item.etaSeconds == null || item.etaSeconds < 0
                                            ? '—'
                                            : `${Math.floor(item.etaSeconds / 3600)}h ${Math.floor((item.etaSeconds % 3600) / 60)}m`}
                                    </td>
                                    <td>
                                        {item.status === 'Completed' && (
                                            <Button
                                                aria-label={t('Manuell importieren', 'Import manually')}
                                                onClick={() =>
                                                    api(`activity/queue/${item.id}/import`, {
                                                        method: 'POST',
                                                    })
                                                }
                                            >
                                                <Download size={18} />
                                                {t('Importieren', 'Import')}
                                            </Button>
                                        )}
                                        <Button
                                            className="icon-button"
                                            aria-label={t('Download entfernen', 'Remove download')}
                                            disabled={item.status === 'Importing'}
                                            onClick={() => setRemove(item)}
                                        >
                                            <Trash2 size={18} />
                                        </Button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {remove && (
                <Confirm
                    title={t('Download entfernen?', 'Remove download?')}
                    text={`${remove.releaseTitle}. ${t('Entfernt den Auftrag aus dem Client und Ludus. Bereits heruntergeladene Dateien bleiben erhalten.', 'Removes the job from the client and Ludus. Downloaded files are retained.')}`}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(`activity/queue/${remove.id}`, { method: 'DELETE' })}
                />
            )}
        </>
    );
}
export function HistoryTable({ gameId }: { gameId?: number }) {
    const { t, locale } = useLocale();
    const [page, setPage] = useState(1);
    const data = useResource<Page<History>>(
        `activity/history?page=${page}&pageSize=50${gameId ? `&gameId=${gameId}` : ''}`,
        30000,
    );
    return (
        <>
            <ListToolbar
                title={gameId ? t('Ereignisse', 'Events') : t('Verlauf', 'History')}
                reload={data.reload}
                embedded={!!gameId}
            />
            <ErrorBox error={data.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : data.data?.items.length === 0 ? (
                <p className="compact-empty">{t('Noch kein Verlauf', 'No history yet')}</p>
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>{t('Ereignis', 'Event')}</th>
                                <th>{t('Spiel / Release', 'Game / release')}</th>
                                <th>Score</th>
                                <th>{t('Zeitpunkt', 'Time')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.items.map((item) => (
                                <tr key={item.id}>
                                    <td>
                                        <Badge value={item.event} />
                                    </td>
                                    <td className="title-cell">
                                        {item.gameId ? (
                                            <Link to={`/games/${item.gameId}`}>{item.gameTitle}</Link>
                                        ) : (
                                            item.gameTitle
                                        )}
                                        <small>{item.releaseTitle}</small>
                                        {item.message && (
                                            <details>
                                                <summary>{t('Details', 'Details')}</summary>
                                                <p>{item.message}</p>
                                            </details>
                                        )}
                                    </td>
                                    <td>{item.score ?? '—'}</td>
                                    <td>{date(item.createdAtUtc, locale)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {data.data && (data.data.total > 0 || page > 1) && (
                <Pager page={page} total={data.data.total} pageSize={50} onChange={setPage} />
            )}
        </>
    );
}
function Blocklist() {
    const { t, locale } = useLocale();
    const data = useResource<Row[]>('activity/blocklist', 60000);
    const [remove, setRemove] = useState<Row>();
    return (
        <>
            <ListToolbar title={t('Blocklist & Cooldowns', 'Blocklist & cooldowns')} reload={data.reload} />
            <ErrorBox error={data.error} retry={data.reload} />
            {!data.data && data.loading ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <p className="compact-empty">{t('Keine gesperrten Releases', 'No blocked releases')}</p>
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>Release</th>
                                <th>{t('Sperre', 'Restriction')}</th>
                                <th>{t('Grund', 'Reason')}</th>
                                <th>{t('Läuft ab', 'Expires')}</th>
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.map((item) => (
                                <tr key={item.id}>
                                    <td className="title-cell">{String(item.releaseTitle)}</td>
                                    <td>
                                        <Badge value={String(item.kind)} />
                                    </td>
                                    <td>{String(item.reasonCode)}</td>
                                    <td>
                                        {item.expiresAtUtc ? (
                                            <>
                                                {date(item.expiresAtUtc, locale)}
                                                {new Date(String(item.expiresAtUtc)).valueOf() <
                                                    Date.now() && <small>{t('Abgelaufen', 'Expired')}</small>}
                                            </>
                                        ) : (
                                            t('Nie', 'Never')
                                        )}
                                    </td>
                                    <td>
                                        <Button onClick={() => setRemove(item)}>
                                            {t('Entfernen', 'Remove')}
                                        </Button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {remove && (
                <Confirm
                    title={t('Sperre entfernen?', 'Remove restriction?')}
                    text={t(
                        'Das Release kann anschließend erneut automatisch ausgewählt werden.',
                        'This release can be selected automatically again.',
                    )}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(`activity/blocklist/${remove.id}`, { method: 'DELETE' })}
                />
            )}
        </>
    );
}
