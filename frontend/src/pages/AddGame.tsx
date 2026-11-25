import { useState } from 'react';
import { Search, Plus } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { api, query } from '../api';
import { useDebounce, useResource } from '../hooks';
import { useLocale } from '../i18n';
import { Badge, Button, Cover, Empty, ErrorBox, Field, Heading, Loading, Modal, useToast } from '../ui';
import type { Metadata, Platform, Profile, RootFolder } from '../types';

export default function AddGamePage() {
    const { t } = useLocale();
    const navigate = useNavigate();
    const toast = useToast();
    const platforms = useResource<Platform[]>('platforms');
    const roots = useResource<RootFolder[]>('root-folders');
    const profiles = useResource<Profile[]>('profiles');
    const [platform, setPlatform] = useState('');
    const [provider, setProvider] = useState('');
    const [search, setSearch] = useState('');
    const term = useDebounce(search);
    const [selected, setSelected] = useState<Metadata | 'manual'>();
    const [title, setTitle] = useState('');
    const [root, setRoot] = useState('');
    const [profile, setProfile] = useState('');
    const [monitored, setMonitored] = useState(true);
    const [baseTitleId, setBaseTitleId] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    const results = useResource<Metadata[]>(
        platform && term.trim().length >= 2
            ? `metadata/search?${query({ query: term.trim(), platformId: platform, provider })}`
            : null,
    );
    function choose(item: Metadata | 'manual') {
        setSelected(item);
        setTitle(item === 'manual' ? search : item.title);
        setError(undefined);
    }
    const rootId = root || String(roots.data?.[0]?.id ?? '');
    const profileId = profile || String(profiles.data?.[0]?.profile.id ?? '');
    return (
        <>
            <Heading
                title={t('Spiel hinzufügen', 'Add game')}
                subtitle={t(
                    'Entdecke Titel und nimm sie in deine Sammlung auf.',
                    'Find titles and make them part of your collection.',
                )}
            />
            <div className="panel">
                <div className="form-grid">
                    <Field label={t('Plattform', 'Platform')}>
                        <select value={platform} onChange={(event) => setPlatform(event.target.value)}>
                            <option value="">{t('Plattform auswählen', 'Choose platform')}</option>
                            {platforms.data?.map((item) => (
                                <option key={item.id} value={item.id}>
                                    {item.name}
                                </option>
                            ))}
                        </select>
                    </Field>
                    <Field label={t('Metadaten-Provider', 'Metadata provider')}>
                        <select value={provider} onChange={(event) => setProvider(event.target.value)}>
                            <option value="">
                                {t('Plattform-Standard mit Fallback', 'Platform default with fallback')}
                            </option>
                            <option value="Igdb">IGDB</option>
                            <option value="ScreenScraper">ScreenScraper.fr</option>
                        </select>
                    </Field>
                </div>
                <div className="search-input large">
                    <Search size={21} />
                    <input
                        aria-label={t('Spieltitel suchen', 'Search game titles')}
                        placeholder={t('Nach einem Spiel suchen …', 'Search for a game…')}
                        maxLength={200}
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                    />
                </div>
                <div className="inline-note">
                    <span className="muted">
                        {t(
                            'Provider-Zugangsdaten werden unter Metadaten eingerichtet.',
                            'Configure provider credentials in Metadata settings.',
                        )}
                    </span>
                    <Link to="/settings/metadata">{t('Metadaten konfigurieren', 'Configure metadata')}</Link>
                </div>
            </div>
            <ErrorBox
                error={results.error || platforms.error || roots.error || profiles.error}
                retry={results.reload}
            />
            {results.loading ? (
                <Loading />
            ) : results.data?.length ? (
                <div className="metadata-results">
                    {results.data.map((item) => (
                        <article className="metadata-card" key={`${item.provider}-${item.externalId}`}>
                            <Cover path={item.coverRelativePath} title={item.title} />
                            <div>
                                <Badge value={item.provider} />
                                <h2>{item.title}</h2>
                                <p className="muted">
                                    {item.year ?? '—'} ·{' '}
                                    {item.developer ?? t('Entwickler unbekannt', 'Unknown developer')}
                                </p>
                                <p className="clamp-3">{item.description}</p>
                                <Button variant="primary" onClick={() => choose(item)}>
                                    <Plus size={17} />
                                    {t('Hinzufügen', 'Add')}
                                </Button>
                            </div>
                        </article>
                    ))}
                </div>
            ) : (
                <Empty
                    title={
                        platform && term.length >= 2
                            ? t('Keine Treffer', 'No results')
                            : t('Dein nächster Klassiker wartet.', 'Your next classic awaits.')
                    }
                    text={t(
                        'Wähle eine Plattform und suche nach mindestens zwei Zeichen. Du kannst einen Titel auch manuell erfassen.',
                        'Choose a platform and enter at least two characters. You can also add a title manually.',
                    )}
                />
            )}
            <div className="section-footer">
                <Button disabled={!platform} onClick={() => choose('manual')}>
                    <Plus size={17} />
                    {t('Manuell hinzufügen', 'Add manually')}
                </Button>
            </div>
            {selected && (
                <Modal
                    title={t('Zur Bibliothek hinzufügen', 'Add to library')}
                    onClose={() => setSelected(undefined)}
                    busy={busy}
                >
                    <form
                        onSubmit={async (event) => {
                            event.preventDefault();
                            setBusy(true);
                            setError(undefined);
                            try {
                                const game = await api<{ id: number }>('games', {
                                    method: 'POST',
                                    body: {
                                        title,
                                        platformId: Number(platform),
                                        rootFolderId: Number(rootId),
                                        qualityProfileId: Number(profileId),
                                        monitored,
                                        ...(selected === 'manual'
                                            ? {}
                                            : {
                                                  provider: selected.provider,
                                                  externalId: selected.externalId,
                                              }),
                                        baseTitleId: baseTitleId || null,
                                    },
                                });
                                toast(t('Spiel hinzugefügt.', 'Game added.'));
                                navigate(`/games/${game.id}`);
                            } catch (err) {
                                setError(err);
                            } finally {
                                setBusy(false);
                            }
                        }}
                    >
                        <Field label={t('Titel', 'Title')}>
                            <input
                                value={title}
                                required
                                maxLength={512}
                                onChange={(event) => setTitle(event.target.value)}
                            />
                        </Field>
                        <Field label="Root Folder">
                            <select value={rootId} required onChange={(event) => setRoot(event.target.value)}>
                                <option value="" disabled>
                                    {t('Root Folder auswählen', 'Choose root folder')}
                                </option>
                                {roots.data?.map((item) => (
                                    <option key={item.id} value={item.id}>
                                        {item.name} — {item.path}
                                    </option>
                                ))}
                            </select>
                        </Field>
                        {roots.data?.length === 0 && (
                            <Link to="/settings/media">
                                {t('Zuerst einen Root Folder anlegen', 'Create a root folder first')}
                            </Link>
                        )}
                        <Field label={t('Qualitätsprofil', 'Quality profile')}>
                            <select
                                required
                                value={profileId}
                                onChange={(event) => setProfile(event.target.value)}
                            >
                                <option value="" disabled>
                                    {t('Profil auswählen', 'Choose profile')}
                                </option>
                                {profiles.data?.map((item) => (
                                    <option key={item.profile.id} value={item.profile.id}>
                                        {item.profile.name} · Cutoff {item.profile.upgradeUntilScore}
                                    </option>
                                ))}
                            </select>
                        </Field>
                        <Field
                            label={t('Switch Base Title-ID (optional)', 'Switch base title ID (optional)')}
                        >
                            <input
                                value={baseTitleId}
                                pattern="[0-9a-fA-F]{13}000"
                                maxLength={16}
                                onChange={(event) => setBaseTitleId(event.target.value)}
                            />
                        </Field>
                        <label className="check">
                            <input
                                type="checkbox"
                                checked={monitored}
                                onChange={(event) => setMonitored(event.target.checked)}
                            />
                            {t('Spiel überwachen', 'Monitor game')}
                        </label>
                        <ErrorBox error={error} />
                        <div className="dialog-actions">
                            <Button disabled={busy} onClick={() => setSelected(undefined)}>
                                {t('Abbrechen', 'Cancel')}
                            </Button>
                            <Button
                                type="submit"
                                variant="primary"
                                busy={busy}
                                disabled={!rootId || !profileId}
                            >
                                {t('Hinzufügen', 'Add game')}
                            </Button>
                        </div>
                    </form>
                </Modal>
            )}
        </>
    );
}
