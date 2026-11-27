import { useState } from 'react';
import { Library, ListOrdered, LogOut, Menu, Search, Settings, ShieldCheck, User } from 'lucide-react';
import { Link, NavLink, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useLocale } from './i18n';
import { useSettingsSections } from './settings-sections';
import { Button } from './ui';
import type { ReactNode } from 'react';

function CollectionSearch() {
    const { t } = useLocale();
    const { pathname } = useLocation();
    const [params, setParams] = useSearchParams();
    const navigate = useNavigate();
    const [draft, setDraft] = useState('');
    const inLibrary = pathname === '/';
    const search = inLibrary ? (params.get('query') ?? '') : draft;
    function updateLibraryQuery(value: string) {
        const next = new URLSearchParams(params);
        if (value) next.set('query', value);
        else next.delete('query');
        next.delete('page');
        setParams(next, { replace: true });
    }
    return (
        <form
            className="header-search"
            role="search"
            onSubmit={(event) => {
                event.preventDefault();
                if (!inLibrary) {
                    const search = draft ? new URLSearchParams({ query: draft }).toString() : '';
                    navigate({ pathname: '/', search });
                }
            }}
        >
            <button type="submit" aria-label={t('Sammlung durchsuchen', 'Search collection')}>
                <Search size={17} />
            </button>
            <input
                type="search"
                aria-label={t('Spiele suchen', 'Search games')}
                placeholder={t('Sammlung durchsuchen …', 'Search collection…')}
                value={search}
                onChange={(event) =>
                    inLibrary ? updateLibraryQuery(event.target.value) : setDraft(event.target.value)
                }
            />
        </form>
    );
}

export function ShellHeader({
    brand,
    languageSwitch,
    connected,
    menuOpen,
    openMenu,
    signOut,
}: {
    brand: ReactNode;
    languageSwitch: ReactNode;
    connected: boolean;
    menuOpen: boolean;
    openMenu: () => void;
    signOut: () => void;
}) {
    const { t } = useLocale();
    const { pathname } = useLocation();
    const connection = connected
        ? t('Live verbunden', 'Live connected')
        : t('Verbindung wird aufgebaut', 'Connecting');
    return (
        <header className="shell-header" inert={menuOpen}>
            <div className="header-identity">
                <Button
                    className="mobile-menu icon-button"
                    onClick={openMenu}
                    aria-controls="sidebar"
                    aria-expanded={menuOpen}
                    aria-label={t('Navigation öffnen', 'Open navigation')}
                >
                    <Menu size={20} />
                </Button>
                <Link to="/" className="header-brand" aria-label={t('Ludus Startseite', 'Ludus home')}>
                    {brand}
                </Link>
            </div>
            <CollectionSearch key={pathname} />
            <div className="header-tools">
                <span className="live-state" title={connection}>
                    <span className={`live-dot ${connected ? 'connected' : ''}`} />
                    <span className="connection-label">{connection}</span>
                </span>
                <button
                    className="account-toggle"
                    type="button"
                    popoverTarget="account-panel"
                    aria-label={t('Benutzermenü', 'Account menu')}
                >
                    <User size={19} />
                </button>
                <div id="account-panel" popover="auto" className="account-panel">
                    <div>
                        {t('Sprache', 'Language')}
                        {languageSwitch}
                    </div>
                    <button
                        type="button"
                        popoverTarget="account-panel"
                        popoverTargetAction="hide"
                        onClick={signOut}
                    >
                        <LogOut size={16} />
                        {t('Abmelden', 'Sign out')}
                    </button>
                </div>
            </div>
        </header>
    );
}

export function SidebarNavigation({
    healthCount,
    closeMenu,
}: {
    healthCount: number;
    closeMenu: () => void;
}) {
    const { t } = useLocale();
    const { pathname } = useLocation();
    const settings = useSettingsSections();
    const system = [
        ['', t('Status & Zustand', 'Status & health')],
        ['tasks', t('Aufgaben', 'Tasks')],
        ['backups', 'Backups'],
        ['logs', 'Logs'],
    ];
    const inGame = pathname.startsWith('/games/');
    const inLibrary = inGame || pathname === '/' || pathname === '/add';
    const inActivity = pathname.startsWith('/activity');
    return (
        <nav
            aria-label={t('Hauptnavigation', 'Main navigation')}
            onClick={(event) => {
                if (event.target instanceof Element && event.target.closest('a')) closeMenu();
            }}
        >
            <div className={inLibrary ? 'nav-group current' : 'nav-group'}>
                <Link to="/" aria-current={pathname === '/' ? 'page' : inLibrary ? 'location' : undefined}>
                    <Library />
                    {t('Bibliothek', 'Library')}
                </Link>
                {inLibrary && (
                    <NavLink className="nav-child" to="/add">
                        {t('Spiel hinzufügen', 'Add game')}
                    </NavLink>
                )}
            </div>
            <div className={inActivity ? 'nav-group current' : 'nav-group'}>
                <Link to="/activity" aria-current={inActivity ? 'location' : undefined}>
                    <ListOrdered />
                    {t('Aktivität', 'Activity')}
                </Link>
                {inActivity && (
                    <>
                        <NavLink className="nav-child" end to="/activity">
                            {t('Warteschlange', 'Queue')}
                        </NavLink>
                        <NavLink className="nav-child" to="/activity/history">
                            {t('Verlauf', 'History')}
                        </NavLink>
                        <NavLink className="nav-child" to="/activity/blocklist">
                            {t('Blocklist & Cooldowns', 'Blocklist & cooldowns')}
                        </NavLink>
                    </>
                )}
            </div>
            <div className={pathname.startsWith('/settings') ? 'nav-group current' : 'nav-group'}>
                <NavLink end to="/settings">
                    <Settings />
                    {t('Einstellungen', 'Settings')}
                </NavLink>
                {pathname.startsWith('/settings') &&
                    settings.map(([slug, label]) => (
                        <NavLink className="nav-child" key={slug} to={`/settings/${slug}`}>
                            {label}
                        </NavLink>
                    ))}
            </div>
            <div className={pathname.startsWith('/system') ? 'nav-group current' : 'nav-group'}>
                <Link to="/system">
                    <ShieldCheck />
                    System{healthCount > 0 && <span className="health-count">{healthCount}</span>}
                </Link>
                {pathname.startsWith('/system') &&
                    system.map(([slug, label]) => (
                        <NavLink
                            end
                            className="nav-child"
                            key={slug}
                            to={slug ? `/system/${slug}` : '/system'}
                        >
                            {label}
                        </NavLink>
                    ))}
            </div>
        </nav>
    );
}
