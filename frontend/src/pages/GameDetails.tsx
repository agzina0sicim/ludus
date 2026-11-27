import { useEffect, useState, type CSSProperties } from 'react';
import {
    AlertCircle,
    Download,
    FolderOpen,
    History,
    Link2,
    Pencil,
    RefreshCw,
    Search,
    Trash2,
} from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, mediaUrl } from '../api';
import { useResource } from '../hooks';
import { bytes, date, useLabel, useLocale } from '../i18n';
import {
    Action,
    Badge,
    Button,
    Confirm,
    Cover,
    Empty,
    ErrorBox,
    Field,
    Loading,
    Modal,
    useToast,
} from '../ui';
import type {
    GameDetails,
    GameFile,
    MediaLink,
    Platform,
    Profile,
    RootFolder,
    SearchResults,
} from '../types';
import { HistoryTable } from './Activity';

export default function GameDetailsPage() {
    const { id } = useParams();
    const { t } = useLocale();
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
    const accent = platform?.accentColor;
    return (
        <section className="game-page compact-page">
            <div
                className="view-toolbar game-tools"
                role="group"
                aria-label={t('Spielaktionen', 'Game actions')}
            >
                <div className="actions">
                    {(
                        [
                            ['files', t('Dateien', 'Files'), FolderOpen],
                            ['search', t('Interaktive Suche', 'Interactive search'), Search],
                            ['history', t('Spielverlauf', 'Game history'), History],
                        ] as const
                    ).map(([value, text, Icon]) => (
                        <Button key={value} aria-pressed={tab === value} onClick={() => setTab(value)}>
                            <Icon size={16} />
                            {text}
                        </Button>
                    ))}
                </div>
                <div className="actions">
                    <Button popoverTarget="game-metadata">
                        <Link2 size={16} />
                        {t('Metadaten', 'Metadata')}
                    </Button>
                    <Button onClick={() => setEdit(true)}>
                        <Pencil size={16} />
                        {t('Bearbeiten', 'Edit')}
                    </Button>
                    <Button
                        aria-label={t('Spiel entfernen', 'Remove game')}
                        title={t('Spiel entfernen', 'Remove game')}
                        onClick={() => setRemove(true)}
                    >
                        <Trash2 size={16} />
                        {t('Entfernen', 'Remove')}
                    </Button>
                </div>
            </div>
            <div id="game-metadata" popover="auto" className="game-metadata-menu">
                <Button
                    popoverTarget="game-metadata"
                    popoverTargetAction="hide"
                    onClick={() => setAttach(true)}
                >
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
            <ErrorBox error={data.error} retry={data.reload} />
            <header
                className="game-banner"
                style={
                    accent && /^#[0-9a-f]{6}$/i.test(accent)
                        ? ({ '--game-accent': accent } as CSSProperties)
                        : undefined
                }
            >
                <Cover path={game.coverRelativePath} title={game.title} accent={accent} />
                <div className="game-identity">
                    <Link to="/">{t('Bibliothek', 'Library')}</Link>
                    <h1>{game.title}</h1>
                    <p>{[platform?.name, game.releaseYear, ...game.genres].filter(Boolean).join(' · ')}</p>
                    <div className="actions">
                        <Badge value={game.status} />
                        <Badge>
                            {game.monitored
                                ? t('Überwacht', 'Monitored')
                                : t('Nicht überwacht', 'Unmonitored')}
                        </Badge>
                    </div>
                    <dl className="game-facts">
                        {game.developer && (
                            <div>
                                <dt>{t('Entwickler', 'Developer')}</dt>
                                <dd>{game.developer}</dd>
                            </div>
                        )}
                        {game.publisher && (
                            <div>
                                <dt>Publisher</dt>
                                <dd>{game.publisher}</dd>
                            </div>
                        )}
                        {profile && (
                            <div>
                                <dt>{t('Profil / Cutoff', 'Profile / cutoff')}</dt>
                                <dd>
                                    {profile.name} · {score ?? '—'} / {profile.upgradeUntilScore}
                                </dd>
                            </div>
                        )}
                        {(game.serials.length > 0 || game.baseTitleId) && (
                            <div>
                                <dt>{t('Spielkennung', 'Game identifiers')}</dt>
                                <dd className="mono">
                                    {[...game.serials, game.baseTitleId].filter(Boolean).join(' · ')}
                                </dd>
                            </div>
                        )}
                    </dl>
                </div>
            </header>
            <div className="detail-body">
                {game.description && (
                    <section className="game-overview">
                        <h2>{t('Beschreibung', 'Overview')}</h2>
                        <p>{game.description}</p>
                    </section>
                )}
                <section key={game.id}>
                    <h2>
                        {tab === 'history'
                            ? t('Spielverlauf', 'Game history')
                            : tab === 'search'
                              ? t('Interaktive Suche', 'Interactive search')
                              : t('Dateien & Verifikation', 'Files & verification')}
                    </h2>
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
                        <GameFiles files={game.files} />
                    )}
                </section>
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
        </section>
    );
}

function GameFiles({ files }: { files: GameFile[] }) {
    const label = useLabel();
    const { t } = useLocale();
    const groups = new Map<string, GameFile[]>();
    for (const file of files) {
        const group = groups.get(file.contentKind);
        if (group) group.push(file);
        else groups.set(file.contentKind, [file]);
    }
    return (
        <div className="game-files">
            {[...new Set(['Base', 'Update', 'Dlc', ...groups.keys()])].map((kind) => {
                const items = groups.get(kind);
                return (
                    items && (
                        <details
                            className="file-group"
                            key={kind}
                            open={kind === 'Base' || groups.size === 1}
                        >
                            <summary>
                                {kind === 'Update' ? t('Updates', 'Updates') : label(kind)}{' '}
                                <span>({items.length})</span>
                            </summary>
                            {items.map((file) => (
                                <FileRow key={file.id} file={file} />
                            ))}
                        </details>
                    )
                );
            })}
        </div>
    );
}

function FileRow({ file }: { file: GameFile }) {
    const { t, locale } = useLocale();
    return (
        <article className="file-row">
            <div className="file-heading">
                <div>
                    <h3>{file.originalFileName}</h3>
                    <div className="file-meta">
                        <Badge value={file.verificationStatus} />
                        {file.missing && <Badge tone="bad">{t('Datei fehlt', 'File missing')}</Badge>}
                        <span>{bytes(file.size, locale)}</span>
                        {file.regions.length > 0 && <span>{file.regions.join(', ')}</span>}
                    </div>
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
            <details>
                <summary>{t('Prüfsummen anzeigen', 'Show checksums')}</summary>
                <p className="mono break-all">{file.relativePath}</p>
                <div className="file-meta">
                    <Badge value={file.role} />
                    <span>Score {file.score}</span>
                    {file.discNumber != null && <span>Disc {file.discNumber}</span>}
                    <span>{date(file.importedAtUtc, locale)}</span>
                    {file.languages?.join(', ')}
                </div>
                <dl className="hashes">
                    {(['crc32', 'md5', 'sha1'] as const).map((key) => (
                        <div key={key}>
                            <dt>{key.toUpperCase()}</dt>
                            <dd className="mono">{file[key] ?? '—'}</dd>
                        </div>
                    ))}
                </dl>
            </details>
        </article>
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
