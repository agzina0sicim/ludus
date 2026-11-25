import { useState } from 'react';
import { Grid2X2, List, Plus, ScanLine, Search } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, query } from '../api';
import { useDebounce, useResource } from '../hooks';
import { useLocale, useLabel } from '../i18n';
import {
    Badge,
    Button,
    Cover,
    Empty,
    ErrorBox,
    Field,
    Heading,
    Loading,
    Modal,
    Pager,
    useToast,
} from '../ui';
import type { Game, Page, Platform, RootFolder } from '../types';

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
    const [scan, setScan] = useState(false);
    const page = Math.max(1, Number(params.get('page')) || 1);
    const platforms = useResource<Platform[]>('platforms');
    const roots = useResource<RootFolder[]>('root-folders');
    const data = useResource<Page<Game>>(
        `games?${query({ query: debounced, platformId: params.get('platformId'), rootFolderId: params.get('rootFolderId'), status: params.get('status'), verification: params.get('verification'), page, pageSize: 48 })}`,
        60000,
    );
    const filter = (key: string, value: string) =>
        setParams(
            (previous) => {
                const next = new URLSearchParams(previous);
                value ? next.set(key, value) : next.delete(key);
                next.delete('page');
                return next;
            },
            { replace: key === 'query' },
        );
    return (
        <>
            <Heading
                title={t('Bibliothek', 'Library')}
                subtitle={t(
                    'Deine Spiele. Plattformübergreifend an einem Ort.',
                    'Your games. Every platform, one collection.',
                )}
                actions={
                    <>
                        <Button onClick={() => setScan(true)}>
                            <ScanLine size={18} />
                            {t('Bibliothek scannen', 'Scan library')}
                        </Button>
                        <Link className="button primary" to="/add">
                            <Plus size={18} />
                            {t('Spiel hinzufügen', 'Add game')}
                        </Link>
                    </>
                }
            />
            <section className="library-toolbar" aria-label={t('Bibliothek filtern', 'Filter library')}>
                <div className="search-input">
                    <Search size={19} />
                    <input
                        aria-label={t('Spiele suchen', 'Search games')}
                        placeholder={t('Sammlung durchsuchen …', 'Search your collection…')}
                        value={search}
                        onChange={(event) => {
                            filter('query', event.target.value);
                        }}
                    />
                </div>
                <div className="segmented">
                    <Button
                        aria-label={t('Poster-Ansicht', 'Poster view')}
                        aria-pressed={view === 'grid'}
                        onClick={() => {
                            setView('grid');
                            try {
                                localStorage.setItem('ludus.view', 'grid');
                            } catch {
                                /* Optional preference. */
                            }
                        }}
                    >
                        <Grid2X2 size={18} />
                    </Button>
                    <Button
                        aria-label={t('Tabellen-Ansicht', 'Table view')}
                        aria-pressed={view === 'table'}
                        onClick={() => {
                            setView('table');
                            try {
                                localStorage.setItem('ludus.view', 'table');
                            } catch {
                                /* Optional preference. */
                            }
                        }}
                    >
                        <List size={18} />
                    </Button>
                </div>
            </section>
            <div className="filters">
                <select
                    aria-label={t('Plattform', 'Platform')}
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
                <select
                    aria-label="Root Folder"
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
                <select
                    aria-label="Status"
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
                <select
                    aria-label={t('Verifikation', 'Verification')}
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
                {params.size > 0 && (
                    <Button
                        variant="ghost"
                        onClick={() => {
                            setParams({});
                        }}
                    >
                        {t('Zurücksetzen', 'Reset')}
                    </Button>
                )}
            </div>
            <ErrorBox error={data.error || platforms.error || roots.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : data.data?.items.length === 0 ? (
                <Empty
                    title={t('Platz für deine Klassiker.', 'Make room for your classics.')}
                    text={t(
                        'Füge ein Spiel hinzu, scanne vorhandene ROMs oder passe deine Filter an.',
                        'Add a game, scan existing ROMs, or adjust your filters.',
                    )}
                >
                    <Link className="button primary" to="/add">
                        <Plus size={18} />
                        {t('Spiel hinzufügen', 'Add game')}
                    </Link>
                </Empty>
            ) : view === 'grid' ? (
                <div className="poster-grid">
                    {data.data?.items.map((game) => (
                        <Link className="game-card" key={game.id} to={`/games/${game.id}`}>
                            <div className="poster-wrap">
                                <Cover
                                    path={game.coverRelativePath}
                                    title={game.title}
                                    accent={
                                        platforms.data?.find((p) => p.id === game.platformId)?.accentColor
                                    }
                                />
                                <span className="poster-status">
                                    <Badge value={game.status} />
                                </span>
                                {!game.monitored && (
                                    <span className="unmonitored-label">
                                        {t('Nicht überwacht', 'Unmonitored')}
                                    </span>
                                )}
                            </div>
                            <h2>{game.title}</h2>
                            <p>
                                {game.platform}
                                <span>{game.releaseYear ?? '—'}</span>
                            </p>
                        </Link>
                    ))}
                </div>
            ) : (
                <div className="table-wrap">
                    <table>
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
                            {data.data?.items.map((game) => (
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
            )}
            {data.data && (
                <Pager
                    page={page}
                    total={data.data.total}
                    pageSize={48}
                    onChange={(next) =>
                        setParams((previous) => {
                            const value = new URLSearchParams(previous);
                            value.set('page', String(next));
                            return value;
                        })
                    }
                />
            )}
            {scan && <ScanDialog onClose={() => setScan(false)} />}
        </>
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
