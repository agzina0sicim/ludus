import { useState } from 'react';
import { Link, NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { Trash2 } from 'lucide-react';
import { api } from '../api';
import { useResource } from '../hooks';
import { bytes, date, useLocale } from '../i18n';
import { Badge, Button, Confirm, Empty, ErrorBox, Heading, Loading, Pager } from '../ui';
import type { History, Page, QueueItem, Row } from '../types';

export default function ActivityPage() {
    const { t } = useLocale();
    return (
        <>
            <Heading
                title={t('Aktivität', 'Activity')}
                subtitle={t(
                    'Downloads, Importe und die Geschichte deiner Sammlung.',
                    'Downloads, imports, and the history of your collection.',
                )}
            />
            <nav className="tabs" aria-label={t('Aktivitätsansichten', 'Activity views')}>
                <NavLink end to="/activity">
                    {t('Warteschlange', 'Queue')}
                </NavLink>
                <NavLink to="/activity/history">{t('Verlauf', 'History')}</NavLink>
                <NavLink to="/activity/blocklist">
                    {t('Blocklist & Cooldowns', 'Blocklist & cooldowns')}
                </NavLink>
            </nav>
            <Routes>
                <Route index element={<Queue />} />
                <Route path="history" element={<HistoryTable />} />
                <Route path="blocklist" element={<Blocklist />} />
                <Route path="*" element={<Navigate replace to="/activity" />} />
            </Routes>
        </>
    );
}
function Queue() {
    const { t, locale } = useLocale();
    const data = useResource<QueueItem[]>('activity/queue', 15000);
    const [remove, setRemove] = useState<QueueItem>();
    return (
        <>
            <ErrorBox error={data.error} retry={data.reload} />
            {!data.data && data.loading ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <Empty
                    title={t('Alles im grünen Bereich.', 'All caught up.')}
                    text={t(
                        'Hier erscheinen Downloads aus der Kategorie ludus mit Live-Fortschritt.',
                        'Downloads in the ludus category appear here with live progress.',
                    )}
                />
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
            <ErrorBox error={data.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : data.data?.items.length === 0 ? (
                <Empty
                    title={t('Noch kein Verlauf', 'No history yet')}
                    text={t(
                        'Downloads, Verifikationen und Importe erscheinen hier.',
                        'Downloads, verifications, and imports will appear here.',
                    )}
                />
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
            {data.data && <Pager page={page} total={data.data.total} pageSize={50} onChange={setPage} />}
        </>
    );
}
function Blocklist() {
    const { t, locale } = useLocale();
    const data = useResource<Row[]>('activity/blocklist', 60000);
    const [remove, setRemove] = useState<Row>();
    return (
        <>
            <ErrorBox error={data.error} retry={data.reload} />
            {!data.data && data.loading ? (
                <Loading />
            ) : data.data?.length === 0 ? (
                <Empty
                    title={t('Keine gesperrten Releases', 'No blocked releases')}
                    text={t(
                        'Temporäre Fehler erhalten einen Cooldown. Defekte Dateien werden dauerhaft blockiert.',
                        'Temporary failures receive a cooldown. Corrupt files are permanently blocked.',
                    )}
                />
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
