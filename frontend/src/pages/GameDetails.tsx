import { useEffect, useState } from 'react';
import { AlertCircle, Download, Pencil, RefreshCw, Search, Trash2 } from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, mediaUrl } from '../api';
import { useResource } from '../hooks';
import { bytes, date, useLocale } from '../i18n';
import {
    Action,
    Badge,
    Button,
    Confirm,
    Cover,
    Empty,
    ErrorBox,
    Field,
    Heading,
    Loading,
    Modal,
    useToast,
} from '../ui';
import type { GameDetails, MediaLink, Platform, Profile, RootFolder, SearchResults } from '../types';
import { HistoryTable } from './Activity';

export default function GameDetailsPage() {
    const { id } = useParams();
    const { t, locale } = useLocale();
    const navigate = useNavigate();
    const [tab, setTab] = useState('files');
    const data = useResource<GameDetails>(/^\d+$/.test(id ?? '') ? `games/${id}` : null, 60000);
    const platforms = useResource<Platform[]>('platforms');
    const profiles = useResource<Profile[]>('profiles');
    const [edit, setEdit] = useState(false);
    const [remove, setRemove] = useState(false);
    const [attach, setAttach] = useState(false);
    useEffect(() => {
        setTab('files');
        setEdit(false);
        setRemove(false);
        setAttach(false);
    }, [id]);
    const game = data.data;
    const toast = useToast();
    if (!/^\d+$/.test(id ?? '')) return <Empty title={t('Ungültiges Spiel', 'Invalid game')} />;
    if (!game)
        return (
            <>
                <ErrorBox error={data.error} retry={data.reload} />
                {data.loading && <Loading />}
            </>
        );
    const platform = platforms.data?.find((item) => item.id === game.platformId);
    const profile = profiles.data?.find((item) => item.profile.id === game.qualityProfileId)?.profile;
    const baseFiles = game.files.filter((file) => file.contentKind === 'Base' && !file.missing);
    const score = baseFiles.length ? Math.max(...baseFiles.map((file) => file.score)) : undefined;
    return (
        <>
            <Link className="back-link" to="/">
                {t('Bibliothek', 'Library')} / {platform?.name}
            </Link>
            <Heading
                title={game.title}
                actions={
                    <>
                        <Button onClick={() => setEdit(true)}>
                            <Pencil size={17} />
                            {t('Bearbeiten', 'Edit')}
                        </Button>
                        <Button
                            className="icon-button"
                            aria-label={t('Spiel entfernen', 'Remove game')}
                            onClick={() => setRemove(true)}
                        >
                            <Trash2 size={18} />
                        </Button>
                    </>
                }
            />
            <ErrorBox error={data.error} retry={data.reload} />
            <section className="game-hero">
                <Cover path={game.coverRelativePath} title={game.title} accent={platform?.accentColor} />
                <div className="game-description">
                    <div className="actions">
                        <Badge value={game.status} />
                        <Badge>{platform?.name ?? '—'}</Badge>
                        <Badge>
                            {game.monitored
                                ? t('Überwacht', 'Monitored')
                                : t('Nicht überwacht', 'Unmonitored')}
                        </Badge>
                    </div>
                    <p className="game-description-text">
                        {game.description ||
                            t(
                                'Noch keine Beschreibung. Verknüpfe Metadaten, um weitere Informationen zu laden.',
                                'No description yet. Link metadata to load more information.',
                            )}
                    </p>
                    <dl className="facts">
                        <div>
                            <dt>{t('Veröffentlichung', 'Released')}</dt>
                            <dd>{game.releaseYear ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>{t('Entwickler', 'Developer')}</dt>
                            <dd>{game.developer ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>Publisher</dt>
                            <dd>{game.publisher ?? '—'}</dd>
                        </div>
                        <div>
                            <dt>{t('Profil / Cutoff', 'Profile / cutoff')}</dt>
                            <dd>
                                {profile?.name ?? '—'} · {score ?? '—'} / {profile?.upgradeUntilScore ?? '—'}
                            </dd>
                        </div>
                    </dl>
                    <div className="actions">
                        {game.genres.map((genre) => (
                            <Badge key={genre}>{genre}</Badge>
                        ))}
                    </div>
                    {(game.serials.length > 0 || game.baseTitleId) && (
                        <p className="mono muted">
                            {[...game.serials, game.baseTitleId].filter(Boolean).join(' · ')}
                        </p>
                    )}
                    <div className="actions">
                        <Button onClick={() => setAttach(true)}>
                            {t('Metadaten verknüpfen', 'Link metadata')}
                        </Button>
                        <Action
                            disabled={!game.metadataReferences.length}
                            run={async () => {
                                await api(`games/${id}/refresh-metadata`, { method: 'POST' });
                                toast(t('Metadaten aktualisiert.', 'Metadata refreshed.'));
                            }}
                        >
                            <RefreshCw size={16} />
                            {t('Aktualisieren', 'Refresh')}
                        </Action>
                    </div>
                </div>
            </section>
            <div className="tabs" role="tablist" aria-label={t('Spielansichten', 'Game views')}>
                {[
                    ['files', t('Dateien & Verifikation', 'Files & verification')],
                    ['history', t('Verlauf', 'History')],
                    ['search', t('Interaktive Suche', 'Interactive search')],
                ].map(([value, text]) => (
                    <button
                        role="tab"
                        id={`tab-${value}`}
                        aria-controls="game-panel"
                        aria-selected={tab === value}
                        className={tab === value ? 'active' : ''}
                        key={value}
                        onClick={() => setTab(value)}
                    >
                        {text}
                    </button>
                ))}
            </div>
            <div id="game-panel" role="tabpanel" aria-labelledby={`tab-${tab}`}>
                {tab === 'history' ? (
                    <HistoryTable gameId={game.id} />
                ) : tab === 'search' ? (
                    <InteractiveSearch gameId={game.id} />
                ) : !game.files.length ? (
                    <Empty
                        title={t('Noch keine ROM vorhanden', 'No ROM yet')}
                        text={t(
                            'Starte eine interaktive Suche oder scanne deine vorhandene Bibliothek.',
                            'Run an interactive search or scan your existing library.',
                        )}
                    >
                        <Button onClick={() => setTab('search')}>
                            <Search size={18} />
                            {t('Releases suchen', 'Search releases')}
                        </Button>
                    </Empty>
                ) : (
                    <div className="file-list">
                        {game.files.map((file) => (
                            <article className="panel file-card" key={file.id}>
                                <div className="section-heading">
                                    <div>
                                        <div className="actions">
                                            <Badge value={file.contentKind} />
                                            <Badge value={file.verificationStatus} />
                                            {file.missing && (
                                                <Badge tone="bad">{t('Datei fehlt', 'File missing')}</Badge>
                                            )}
                                        </div>
                                        <h3>{file.originalFileName}</h3>
                                        <p className="mono muted break-all">{file.relativePath}</p>
                                    </div>
                                    <Action
                                        disabled={file.missing || file.role === 'Directory'}
                                        run={async () => {
                                            const link = await api<MediaLink>('media/links', {
                                                method: 'POST',
                                                body: { kind: 'rom', fileId: file.id },
                                            });
                                            const anchor = document.createElement('a');
                                            anchor.href = mediaUrl(link.url);
                                            anchor.download = '';
                                            anchor.click();
                                        }}
                                    >
                                        <Download size={17} />
                                        {t('Herunterladen', 'Download')}
                                    </Action>
                                </div>
                                <div className="file-meta">
                                    <span>{bytes(file.size, locale)}</span>
                                    <span>Score {file.score}</span>
                                    <Badge value={file.role} />
                                    <span>{file.regions.join(', ')}</span>
                                    <span>{date(file.importedAtUtc, locale)}</span>
                                </div>
                                <details>
                                    <summary>{t('Prüfsummen anzeigen', 'Show checksums')}</summary>
                                    <dl className="hashes">
                                        {(['crc32', 'md5', 'sha1'] as const).map((hash) => (
                                            <div key={hash}>
                                                <dt>{hash.toUpperCase()}</dt>
                                                <dd className="mono">{file[hash] ?? '—'}</dd>
                                            </div>
                                        ))}
                                    </dl>
                                </details>
                            </article>
                        ))}
                    </div>
                )}
            </div>
            {edit && <EditGame game={game} onClose={() => setEdit(false)} />}
            {attach && <AttachMetadata game={game} onClose={() => setAttach(false)} />}
            {remove && (
                <Confirm
                    title={t('Spiel entfernen?', 'Remove game?')}
                    text={t(
                        'Entfernt das Spiel und seine Dateizuordnungen aus Ludus. ROM-Dateien bleiben auf dem Datenträger erhalten.',
                        'Removes the game and its file records from Ludus. ROM files remain on disk.',
                    )}
                    onClose={() => setRemove(false)}
                    onConfirm={async () => {
                        await api(`games/${id}`, { method: 'DELETE' });
                        navigate('/');
                    }}
                />
            )}
        </>
    );
}
function EditGame({ game, onClose }: { game: GameDetails; onClose: () => void }) {
    const { t } = useLocale();
    const roots = useResource<RootFolder[]>('root-folders');
    const profiles = useResource<Profile[]>('profiles');
    const [draft, setDraft] = useState({
        title: game.title,
        rootFolderId: game.rootFolderId,
        qualityProfileId: game.qualityProfileId,
        monitored: game.monitored,
    });
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <Modal title={t('Spiel bearbeiten', 'Edit game')} onClose={onClose} busy={busy}>
            <form
                onSubmit={async (event) => {
                    event.preventDefault();
                    setBusy(true);
                    try {
                        await api(`games/${game.id}`, { method: 'PUT', body: draft });
                        onClose();
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                <Field label={t('Titel', 'Title')}>
                    <input
                        required
                        maxLength={512}
                        value={draft.title}
                        onChange={(event) => setDraft({ ...draft, title: event.target.value })}
                    />
                </Field>
                <Field
                    label="Root Folder"
                    hint={
                        game.files.length
                            ? t(
                                  'Bei vorhandenen Dateizuordnungen bleibt der Root Folder fest.',
                                  'Root folder is fixed while file records exist.',
                              )
                            : undefined
                    }
                >
                    <select
                        value={draft.rootFolderId}
                        disabled={!!game.files.length}
                        onChange={(event) => setDraft({ ...draft, rootFolderId: Number(event.target.value) })}
                    >
                        {roots.data?.map((item) => (
                            <option key={item.id} value={item.id}>
                                {item.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label={t('Qualitätsprofil', 'Quality profile')}>
                    <select
                        value={draft.qualityProfileId}
                        onChange={(event) =>
                            setDraft({ ...draft, qualityProfileId: Number(event.target.value) })
                        }
                    >
                        {profiles.data?.map((item) => (
                            <option key={item.profile.id} value={item.profile.id}>
                                {item.profile.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <label className="check">
                    <input
                        type="checkbox"
                        checked={draft.monitored}
                        onChange={(event) => setDraft({ ...draft, monitored: event.target.checked })}
                    />
                    {t('Überwacht', 'Monitored')}
                </label>
                <ErrorBox error={error || roots.error || profiles.error} />
                <div className="dialog-actions">
                    <Button disabled={busy} onClick={onClose}>
                        {t('Abbrechen', 'Cancel')}
                    </Button>
                    <Button type="submit" variant="primary" busy={busy}>
                        {t('Speichern', 'Save')}
                    </Button>
                </div>
            </form>
        </Modal>
    );
}
function AttachMetadata({ game, onClose }: { game: GameDetails; onClose: () => void }) {
    const { t } = useLocale();
    const [provider, setProvider] = useState(game.metadataReferences[0]?.provider ?? 'Igdb');
    const [externalId, setExternalId] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <Modal title={t('Metadaten verknüpfen', 'Link metadata')} onClose={onClose} busy={busy}>
            <p className="muted">
                {t(
                    'Gib die Spiel-ID des Providers an. Beschreibung und Cover werden geladen.',
                    'Enter the provider’s game ID to load its description and cover.',
                )}
            </p>
            <form
                onSubmit={async (event) => {
                    event.preventDefault();
                    setBusy(true);
                    try {
                        await api(`games/${game.id}/metadata`, {
                            method: 'PUT',
                            body: { provider, externalId },
                        });
                        onClose();
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                <Field label="Provider">
                    <select value={provider} onChange={(event) => setProvider(event.target.value)}>
                        <option value="Igdb">IGDB</option>
                        <option value="ScreenScraper">ScreenScraper.fr</option>
                    </select>
                </Field>
                <Field label={t('Externe Spiel-ID', 'External game ID')}>
                    <input
                        required
                        maxLength={128}
                        value={externalId}
                        onChange={(event) => setExternalId(event.target.value)}
                    />
                </Field>
                <ErrorBox error={error} />
                <div className="dialog-actions">
                    <Button disabled={busy} onClick={onClose}>
                        {t('Abbrechen', 'Cancel')}
                    </Button>
                    <Button type="submit" variant="primary" busy={busy}>
                        {t('Verknüpfen', 'Link')}
                    </Button>
                </div>
            </form>
        </Modal>
    );
}
export function InteractiveSearch({ gameId }: { gameId: number }) {
    const { t, locale } = useLocale();
    const toast = useToast();
    const [data, setData] = useState<SearchResults>();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <>
            <div className="section-heading">
                <p className="muted">
                    {t(
                        'Alle Releases, einschließlich abgelehnter Treffer. Such-Tickets sind 10 Minuten gültig.',
                        'All releases, including rejected results. Search tickets are valid for 10 minutes.',
                    )}
                </p>
                <Button
                    busy={busy}
                    onClick={async () => {
                        setBusy(true);
                        setError(undefined);
                        setData(undefined);
                        try {
                            setData(await api<SearchResults>(`games/${gameId}/search`));
                        } catch (err) {
                            setError(err);
                        } finally {
                            setBusy(false);
                        }
                    }}
                >
                    <Search size={17} />
                    {t('Releases suchen', 'Search releases')}
                </Button>
            </div>
            <ErrorBox error={error} />
            {data && data.failures.length > 0 && (
                <div className="notice warning">
                    {t(
                        'Einige Indexer konnten nicht abgefragt werden:',
                        'Some indexers could not be queried:',
                    )}{' '}
                    {data.failures.map((item) => `#${item.indexerId} ${item.code ?? ''}`).join(', ')}
                </div>
            )}
            {data?.truncated && (
                <div className="notice warning">
                    {t('Die Ergebnismenge wurde begrenzt.', 'The result set was truncated.')}
                </div>
            )}
            {data?.releases.length === 0 && (
                <Empty title={t('Keine Releases gefunden', 'No releases found')} />
            )}
            {data && data.releases.length > 0 && (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                <th>Release</th>
                                <th>Score</th>
                                <th>{t('Größe', 'Size')}</th>
                                <th>{t('Quelle', 'Source')}</th>
                                <th>{t('Bewertung', 'Decision')}</th>
                                <th>{t('Aktion', 'Action')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.releases.map((item, index) => (
                                <tr key={`${item.release.indexerId}-${item.release.releaseId}-${index}`}>
                                    <td className="title-cell">
                                        {item.release.title}
                                        <small>
                                            {date(item.release.publishedAtUtc, locale)} ·{' '}
                                            {item.parsed.regions.join(', ')} · {item.parsed.extension}
                                        </small>
                                    </td>
                                    <td>
                                        <strong className="score">{item.decision.score}</strong>
                                        {item.decision.formats.length > 0 && (
                                            <details>
                                                <summary>{t('Formate', 'Formats')}</summary>
                                                {item.decision.formats.map((format, i) => (
                                                    <p key={i}>
                                                        {format.name}: {format.score > 0 ? '+' : ''}
                                                        {format.score}
                                                    </p>
                                                ))}
                                            </details>
                                        )}
                                    </td>
                                    <td>{bytes(item.release.size, locale)}</td>
                                    <td>
                                        {item.release.protocol}
                                        <small>
                                            #{item.release.indexerId} · {item.release.seeders ?? '—'} Seeders
                                        </small>
                                    </td>
                                    <td>
                                        {item.decision.accepted ? (
                                            <Badge tone="good">{t('Akzeptiert', 'Accepted')}</Badge>
                                        ) : (
                                            <details className="rejections">
                                                <summary
                                                    title={item.decision.rejections
                                                        .map((reason) =>
                                                            locale === 'de'
                                                                ? reason.messageDe
                                                                : reason.messageEn,
                                                        )
                                                        .join('\n')}
                                                >
                                                    <AlertCircle size={19} />
                                                    {t('Abgelehnt', 'Rejected')} (
                                                    {item.decision.rejections.length})
                                                </summary>
                                                <ul>
                                                    {item.decision.rejections.map((reason, i) => (
                                                        <li key={i}>
                                                            {locale === 'de'
                                                                ? reason.messageDe
                                                                : reason.messageEn}
                                                            {reason.detail && <small>{reason.detail}</small>}
                                                        </li>
                                                    ))}
                                                </ul>
                                            </details>
                                        )}
                                    </td>
                                    <td>
                                        <Action
                                            disabled={!item.decision.accepted || !item.ticket}
                                            run={async () => {
                                                await api(`games/${gameId}/grab`, {
                                                    method: 'POST',
                                                    body: { ticket: item.ticket },
                                                });
                                                toast(t('Download gestartet.', 'Download started.'));
                                            }}
                                        >
                                            <Download size={17} />
                                            {t('Laden', 'Grab')}
                                        </Action>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </>
    );
}
