import { useEffect, useRef, useState } from 'react';
import { Plus, ScanLine } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, query } from '../api';
import { useDebounce, useResource } from '../hooks';
import { date, useLocale } from '../i18n';
import { Badge, Button, Cover, ErrorBox, Field, Loading, Modal, Pager, useToast } from '../ui';
import type { Game, Page, Platform, RootFolder } from '../types';

import LibraryControls from './LibraryControls';
import {
    columns,
    columnNames,
    initialOptions,
    initialView,
    persist,
    readFilters,
    filterKeys,
    sortNames,
    type Sort,
    type Column,
    type View,
    type Options,
    type Filters,
} from './library-preferences';

const pageSize = 48;

export default function LibraryPage() {
    const { t } = useLocale();
    const [params, setParams] = useSearchParams();
    const search = params.get('query') ?? '';
    const debounced = useDebounce(search);
    const [view, setView] = useState(initialView);
    const [options, setOptions] = useState(initialOptions);
    const filters = readFilters(params);
    const requestedSort = params.get('sort') ?? 'title';
    const sort = Object.hasOwn(sortNames, requestedSort) ? (requestedSort as Sort) : 'title';
    const descending = params.get('direction') === 'desc';
    const activeFilters = Object.keys(filters).length;
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
        `games?${query({ query: debounced, ...filters, sort, descending: descending ? 'true' : undefined, page, pageSize })}`,
        60000,
    );
    function updateQuery(change: (next: URLSearchParams) => void) {
        setParams((previous) => {
            const next = new URLSearchParams(previous);
            change(next);
            next.delete('page');
            return next;
        });
    }
    function applyFilters(filters: Filters) {
        updateQuery((next) => {
            for (const key of filterKeys) {
                const value = filters[key];
                if (value) next.set(key, value);
                else next.delete(key);
            }
        });
    }
    function changeSort(field: Sort) {
        updateQuery((next) => {
            if (field === 'title') next.delete('sort');
            else next.set('sort', field);
            if (field === sort && !descending) next.set('direction', 'desc');
            else next.delete('direction');
        });
    }
    function changeView(next: View) {
        setView(next);
        persist('ludus.view', next);
    }
    function changeOptions(next: Options) {
        setOptions(next);
        persist('ludus.library.options', JSON.stringify(next));
    }
    const scanLabel = t('Bibliothek scannen', 'Scan library');
    const addLabel = t('Spiel hinzufügen', 'Add game');
    const resetLabel = t('Filter zurücksetzen', 'Reset filters');
    const games = data.data;
    return (
        <section className="library-page" ref={library} aria-labelledby="library-title">
            <h1 id="library-title" className="sr-only">
                {t('Bibliothek', 'Library')}
            </h1>
            <div
                className="view-toolbar library-toolbar"
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
                <LibraryControls
                    view={view}
                    changeView={changeView}
                    options={options}
                    changeOptions={changeOptions}
                    sort={sort}
                    descending={descending}
                    changeSort={changeSort}
                    filters={filters}
                    applyFilters={applyFilters}
                    platforms={platforms.data ?? []}
                    roots={roots.data ?? []}
                />
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
                            <h2 className={hasCriteria ? undefined : 'sr-only'}>
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
                                <Button
                                    onClick={() =>
                                        updateQuery((next) => {
                                            filterKeys.forEach((key) => next.delete(key));
                                            next.delete('query');
                                        })
                                    }
                                >
                                    {resetLabel}
                                </Button>
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
                        <PosterGrid games={games.items} platforms={platforms.data ?? []} options={options} />
                    ) : view === 'overview' ? (
                        <Overview games={games.items} description={options.overviewDescription} />
                    ) : (
                        <GameTable games={games.items} visible={columns.filter((k) => options.table[k])} />
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
function PosterGrid({
    games,
    platforms,
    options,
}: {
    games: Game[];
    platforms: Platform[];
    options: Options;
}) {
    const { t, locale } = useLocale();
    return (
        <div className={'poster-grid poster-size-' + options.size}>
            {games.map((game) => (
                <Link
                    className="game-card"
                    aria-label={game.title}
                    title={game.title}
                    key={game.id}
                    to={`/games/${game.id}`}
                >
                    <Cover
                        path={game.coverRelativePath}
                        title={game.title}
                        accent={platforms.find((p) => p.id === game.platformId)?.accentColor}
                    />
                    <div
                        className={options.poster.statusText ? 'poster-status' : 'poster-status strip-only'}
                        title={game.status}
                    >
                        <Badge value={game.status} />
                    </div>
                    <div className="poster-caption">
                        {options.poster.title && <h2 title={game.title}>{game.title}</h2>}
                        <p>
                            {[
                                options.poster.platform && game.platform,
                                options.poster.platform && releaseDate(game, locale),
                                options.poster.monitored &&
                                    (game.monitored
                                        ? t('Überwacht', 'Monitored')
                                        : t('Nicht überwacht', 'Unmonitored')),
                            ]
                                .filter(Boolean)
                                .join(' · ')}
                        </p>
                    </div>
                </Link>
            ))}
        </div>
    );
}
function Overview({ games, description }: { games: Game[]; description: boolean }) {
    const { locale } = useLocale();
    return (
        <div className="library-overview">
            {games.map((game) => (
                <Link key={game.id} to={'/games/' + game.id}>
                    <Cover path={game.coverRelativePath} title={game.title} />
                    <div>
                        <h2>{game.title}</h2>
                        <p>{[game.platform, releaseDate(game, locale)].filter(Boolean).join(' · ')}</p>
                        {description && game.description && <p className="clamp-3">{game.description}</p>}
                        <Badge value={game.status} />
                    </div>
                </Link>
            ))}
        </div>
    );
}
function GameTable({ games, visible }: { games: Game[]; visible: Column[] }) {
    const { t, locale } = useLocale();
    function cell(game: Game, column: Column) {
        switch (column) {
            case 'platform':
                return game.platform;
            case 'year':
                return releaseDate(game, locale);
            case 'status':
                return <Badge value={game.status} />;
            case 'monitored':
                return game.monitored ? t('Ja', 'Yes') : t('Nein', 'No');
            case 'files':
                return game.fileCount;
        }
    }
    return (
        <div className="table-wrap library-table">
            <table aria-label={t('Spiele in der Bibliothek', 'Games in library')}>
                <thead>
                    <tr>
                        <th>{t('Spiel', 'Game')}</th>
                        {visible.map((k) => (
                            <th key={k}>{t(...columnNames[k])}</th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {games.map((game) => (
                        <tr key={game.id}>
                            <td>
                                <Link to={'/games/' + game.id}>{game.title}</Link>
                            </td>
                            {visible.map((k) => (
                                <td key={k}>{cell(game, k)}</td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
function releaseDate(game: Game, locale: string) {
    if (!game.releaseDateUtc) return game.releaseYear ?? '—';
    if (game.releaseDatePrecision === 'Year')
        return `≈ ${game.releaseYear ?? new Date(game.releaseDateUtc).getFullYear()}`;
    if (game.releaseDatePrecision === 'Month')
        return `≈ ${new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric' }).format(new Date(game.releaseDateUtc))}`;
    return date(game.releaseDateUtc, locale, 'short');
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
