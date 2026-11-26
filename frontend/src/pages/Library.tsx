import { useEffect, useRef, useState } from 'react';
import { Grid2X2, List, Plus, ScanLine, SlidersHorizontal, X } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, query } from '../api';
import { useDebounce, useResource } from '../hooks';
import { useLocale, useLabel } from '../i18n';
import { Badge, Button, Cover, ErrorBox, Field, Loading, Modal, Pager, useToast } from '../ui';
import type { Game, Page, Platform, RootFolder } from '../types';

const pageSize = 48;
const filterKeys = ['platformId', 'rootFolderId', 'status', 'verification'] as const;

export default function LibraryPage() {
    const { t } = useLocale();
    const label = useLabel();
    const [params, setParams] = useSearchParams();
    const search = params.get('query') ?? '';
    const debounced = useDebounce(search);
    const [view, setView] = useState<'grid' | 'table'>(() => {
        try {
            return localStorage.getItem('ludus.view') === 'table' ? 'table' : 'grid';
        } catch {
            return 'grid';
        }
    });
    const activeFilters = filterKeys.filter((key) => params.get(key)).length;
    const hasCriteria = activeFilters > 0 || Boolean(search);
    const [scan, setScan] = useState(false);
    const pageNumber = Number(params.get('page'));
    const page =
        Number.isSafeInteger(pageNumber) &&
        pageNumber > 0 &&
        pageNumber <= Math.floor(2147483647 / pageSize) + 1
            ? pageNumber
            : 1;
    const library = useRef<HTMLElement>(null);
    useEffect(() => {
        library.current?.closest('main')?.scrollTo(0, 0);
    }, [page]);
    const platforms = useResource<Platform[]>('platforms');
    const roots = useResource<RootFolder[]>('root-folders');
    const data = useResource<Page<Game>>(
        `games?${query({ query: debounced, platformId: params.get('platformId'), rootFolderId: params.get('rootFolderId'), status: params.get('status'), verification: params.get('verification'), page, pageSize })}`,
        60000,
    );
    const filter = (key: string, value: string) =>
        setParams((previous) => {
            const next = new URLSearchParams(previous);
            value ? next.set(key, value) : next.delete(key);
            next.delete('page');
            return next;
        });
    function changeView(next: 'grid' | 'table') {
        setView(next);
        try {
            localStorage.setItem('ludus.view', next);
        } catch {
            // Storage can be disabled while the current view remains usable.
        }
    }
    const scanLabel = t('Bibliothek scannen', 'Scan library');
    const addLabel = t('Spiel hinzufügen', 'Add game');
    const resetLabel = t('Filter zurücksetzen', 'Reset filters');
    const filterLabel = t('Filter', 'Filters');
    const games = data.data;
    return (
        <section className="library-page" ref={library} aria-labelledby="library-title">
            <h1 id="library-title" className="sr-only">
                {t('Bibliothek', 'Library')}
            </h1>
            <div
                className="library-toolbar"
                role="group"
                aria-label={t('Bibliotheksaktionen', 'Library actions')}
            >
                <div className="library-tools">
                    <Button aria-label={scanLabel} title={scanLabel} onClick={() => setScan(true)}>
                        <ScanLine size={18} />
                        <span>{t('Scannen', 'Scan')}</span>
                    </Button>
                    <Link className="button" aria-label={addLabel} title={addLabel} to="/add">
                        <Plus size={18} />
                        <span>{t('Hinzufügen', 'Add game')}</span>
                    </Link>
                </div>
                <div className="library-tools">
                    <div className="segmented" role="group" aria-label={t('Ansicht', 'View')}>
                        <Button
                            aria-label={t('Poster-Ansicht', 'Poster view')}
                            title={t('Poster-Ansicht', 'Poster view')}
                            aria-pressed={view === 'grid'}
                            onClick={() => changeView('grid')}
                        >
                            <Grid2X2 size={17} />
                        </Button>
                        <Button
                            aria-label={t('Tabellen-Ansicht', 'Table view')}
                            title={t('Tabellen-Ansicht', 'Table view')}
                            aria-pressed={view === 'table'}
                            onClick={() => changeView('table')}
                        >
                            <List size={17} />
                        </Button>
                    </div>
                    <Button className="filter-toggle" popoverTarget="library-filters" title={filterLabel}>
                        <SlidersHorizontal size={17} />
                        {filterLabel}
                        {activeFilters > 0 && <span className="filter-count">{activeFilters}</span>}
                    </Button>
                </div>
            </div>
            <div
                id="library-filters"
                popover="auto"
                className="library-filters"
                role="group"
                aria-label={filterLabel}
            >
                <div className="filter-heading">
                    <strong>{filterLabel}</strong>
                    <Button
                        className="icon-button"
                        popoverTarget="library-filters"
                        popoverTargetAction="hide"
                        aria-label={t('Filter schließen', 'Close filters')}
                    >
                        <X size={17} />
                    </Button>
                </div>
                <Field label={t('Plattform', 'Platform')}>
                    <select
                        value={params.get('platformId') ?? ''}
                        onChange={(event) => filter('platformId', event.target.value)}
                    >
                        <option value="">{t('Alle Plattformen', 'All platforms')}</option>
                        {platforms.data?.map((p) => (
                            <option key={p.id} value={p.id}>
                                {p.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label="Root Folder">
                    <select
                        value={params.get('rootFolderId') ?? ''}
                        onChange={(event) => filter('rootFolderId', event.target.value)}
                    >
                        <option value="">{t('Alle Root Folders', 'All root folders')}</option>
                        {roots.data?.map((root) => (
                            <option key={root.id} value={root.id}>
                                {root.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label="Status">
                    <select
                        value={params.get('status') ?? ''}
                        onChange={(event) => filter('status', event.target.value)}
                    >
                        {[
                            ['', t('Jeder Status', 'Any status')],
                            ['missing', t('Fehlend', 'Missing')],
                            ['downloading', t('Herunterladend', 'Downloading')],
                            ['available', t('Vorhanden', 'Available')],
                            ['cutoffUnmet', t('Cutoff nicht erreicht', 'Cutoff unmet')],
                            ['unmonitored', t('Nicht überwacht', 'Unmonitored')],
                        ].map(([value, text]) => (
                            <option key={value} value={value}>
                                {text}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label={t('Verifikation', 'Verification')}>
                    <select
                        value={params.get('verification') ?? ''}
                        onChange={(event) => filter('verification', event.target.value)}
                    >
                        <option value="">{t('Jede Verifikation', 'Any verification')}</option>
                        {['Unverified', 'VerifiedGoodDump', 'Modified'].map((value) => (
                            <option key={value} value={value}>
                                {label(value)}
                            </option>
                        ))}
                    </select>
                </Field>

                {hasCriteria && (
                    <Button
                        popoverTarget="library-filters"
                        popoverTargetAction="hide"
                        onClick={() => setParams({})}
                    >
                        {resetLabel}
                    </Button>
                )}
            </div>
            <div className="library-results">
                <ErrorBox
                    error={data.error || platforms.error || roots.error}
                    retry={() => {
                        data.reload();
                        platforms.reload();
                        roots.reload();
                    }}
                />
                {search !== debounced || (data.loading && !games) ? (
                    <Loading />
                ) : (
                    games &&
                    (games.total === 0 ? (
                        <div className="library-empty">
                            <h2>
                                {hasCriteria
                                    ? t('Keine passenden Spiele', 'No matching games')
                                    : t('Deine Bibliothek ist leer', 'Your library is empty')}
                            </h2>
                            <p>
                                {hasCriteria
                                    ? t(
                                          'Ändere deine Suche oder setze die Filter zurück.',
                                          'Change your search or reset the filters.',
                                      )
                                    : t(
                                          'Füge ein Spiel hinzu oder scanne deine vorhandenen ROMs.',
                                          'Add a game or scan your existing ROMs.',
                                      )}
                            </p>
                            {hasCriteria ? (
                                <Button onClick={() => setParams({})}>{resetLabel}</Button>
                            ) : (
                                <div className="actions">
                                    <Link className="button primary" to="/add">
                                        <Plus size={16} />
                                        {addLabel}
                                    </Link>
                                    <Button onClick={() => setScan(true)}>
                                        <ScanLine size={16} />
                                        {scanLabel}
                                    </Button>
                                </div>
                            )}
                        </div>
                    ) : view === 'grid' ? (
                        <PosterGrid games={games.items} platforms={platforms.data ?? []} />
                    ) : (
                        <GameTable games={games.items} />
                    ))
                )}
                {games && (games.total > 0 || page > 1) && (
                    <div hidden={games.total === 0}>
                        <Pager
                            page={page}
                            total={games.total}
                            pageSize={pageSize}
                            onChange={(next) =>
                                setParams(
                                    (previous) => {
                                        const value = new URLSearchParams(previous);
                                        if (next === 1) value.delete('page');
                                        else value.set('page', String(next));
                                        return value;
                                    },
                                    { replace: page > Math.max(1, Math.ceil(games.total / pageSize)) },
                                )
                            }
                        />
                    </div>
                )}
            </div>
            {scan && <ScanDialog onClose={() => setScan(false)} />}
        </section>
    );
}
function PosterGrid({ games, platforms }: { games: Game[]; platforms: Platform[] }) {
    const { t } = useLocale();
    return (
        <div className="poster-grid">
            {games.map((game) => (
                <Link className="game-card" key={game.id} to={`/games/${game.id}`}>
                    <Cover
                        path={game.coverRelativePath}
                        title={game.title}
                        accent={platforms.find((p) => p.id === game.platformId)?.accentColor}
                    />
                    <div className="poster-status">
                        <Badge value={game.status} />
                    </div>
                    <h2 title={game.title}>{game.title}</h2>
                    <p title={game.platform}>
                        {game.platform}
                        {game.releaseYear && <> · {game.releaseYear}</>}
                    </p>
                    {!game.monitored && <small>{t('Nicht überwacht', 'Unmonitored')}</small>}
                </Link>
            ))}
        </div>
    );
}
function GameTable({ games }: { games: Game[] }) {
    const { t } = useLocale();
    return (
        <div className="table-wrap library-table">
            <table aria-label={t('Spiele in der Bibliothek', 'Games in library')}>
                <thead>
                    <tr>
                        <th>{t('Spiel', 'Game')}</th>
                        <th>{t('Plattform', 'Platform')}</th>
                        <th>{t('Jahr', 'Year')}</th>
                        <th>Status</th>
                        <th>{t('Überwacht', 'Monitored')}</th>
                        <th>{t('Dateien', 'Files')}</th>
                    </tr>
                </thead>
                <tbody>
                    {games.map((game) => (
                        <tr key={game.id}>
                            <td>
                                <Link to={`/games/${game.id}`}>{game.title}</Link>
                            </td>
                            <td>{game.platform}</td>
                            <td>{game.releaseYear ?? '—'}</td>
                            <td>
                                <Badge value={game.status} />
                            </td>
                            <td>{game.monitored ? t('Ja', 'Yes') : t('Nein', 'No')}</td>
                            <td>{game.fileCount}</td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
export function ScanDialog({ onClose }: { onClose: () => void }) {
    const { t } = useLocale();
    const toast = useToast();
    const [root, setRoot] = useState('');
    const [platform, setPlatform] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    const roots = useResource<RootFolder[]>('root-folders');
    const platforms = useResource<Platform[]>('platforms');
    return (
        <Modal
            title={t('Vorhandene Bibliothek scannen', 'Scan existing library')}
            onClose={onClose}
            busy={busy}
        >
            <p className="muted">
                {t(
                    'Prüft Hashes und erkennt verschobene Dateien. Wähle bei mehrdeutigen Dateiendungen die Plattform aus. Große Bibliotheken benötigen einige Zeit.',
                    'Verifies hashes and locates moved files. Select a platform for ambiguous file extensions. Large libraries may take a while.',
                )}
            </p>
            <form
                onSubmit={async (event) => {
                    event.preventDefault();
                    setBusy(true);
                    setError(undefined);
                    try {
                        const result = await api<{
                            files: number;
                            added: number;
                            relocated: number;
                            missing: number;
                            skipped: number;
                        }>('system/library/scan', {
                            method: 'POST',
                            body: {
                                rootFolderId: root ? Number(root) : null,
                                platformId: platform ? Number(platform) : null,
                            },
                        });
                        toast(
                            `${t('Scan abgeschlossen', 'Scan complete')}: ${result.files} ${t('Dateien', 'files')} · ${result.added} ${t('neu', 'new')} · ${result.relocated} ${t('wiedergefunden', 'relocated')} · ${result.missing} ${t('fehlend', 'missing')} · ${result.skipped} ${t('übersprungen', 'skipped')}`,
                        );
                        onClose();
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                <Field label="Root Folder">
                    <select value={root} onChange={(event) => setRoot(event.target.value)}>
                        <option value="">{t('Alle', 'All')}</option>
                        {roots.data?.map((item) => (
                            <option key={item.id} value={item.id}>
                                {item.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <Field label={t('Plattform', 'Platform')}>
                    <select value={platform} onChange={(event) => setPlatform(event.target.value)}>
                        <option value="">{t('Automatisch erkennen', 'Detect automatically')}</option>
                        {platforms.data?.map((item) => (
                            <option key={item.id} value={item.id}>
                                {item.name}
                            </option>
                        ))}
                    </select>
                </Field>
                <ErrorBox error={error || roots.error || platforms.error} />
                <div className="dialog-actions">
                    <Button disabled={busy} onClick={onClose}>
                        {t('Abbrechen', 'Cancel')}
                    </Button>
                    <Button type="submit" variant="primary" busy={busy}>
                        {t('Scan starten', 'Start scan')}
                    </Button>
                </div>
            </form>
        </Modal>
    );
}
