import { Component, lazy, Suspense, useEffect, useRef, useState, type ReactNode } from 'react';
import { Activity, Gamepad2, Library, LogOut, Menu, Plus, Settings, ShieldCheck, X } from 'lucide-react';
import { Link, NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { api, login, resetCsrf } from './api';
import { useResource } from './hooks';
import { useLocale } from './i18n';
import { useRealtime } from './realtime';
import { Button, ErrorBox, Field, Loading } from './ui';
import type { HealthIssue, Session } from './types';

const LibraryPage = lazy(() => import('./pages/Library'));
const AddPage = lazy(() => import('./pages/AddGame'));
const GamePage = lazy(() => import('./pages/GameDetails'));
const ActivityPage = lazy(() => import('./pages/Activity'));
const SettingsPage = lazy(() => import('./pages/Settings'));
const SystemPage = lazy(() => import('./pages/System'));

class ErrorBoundary extends Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
    state = { failed: false };
    static getDerivedStateFromError() {
        return { failed: true };
    }
    render() {
        return this.state.failed ? this.props.fallback : this.props.children;
    }
}
export function App() {
    const session = useResource<Session>('auth/session');
    const [expired, setExpired] = useState(false);
    const { t } = useLocale();
    useEffect(() => {
        const invalidate = () => {
            resetCsrf();
            setExpired(true);
            session.reload();
        };
        window.addEventListener('ludus:unauthorized', invalidate);
        return () => window.removeEventListener('ludus:unauthorized', invalidate);
    }, []);
    if (!session.data)
        return (
            <div className="boot">
                <Brand />
                <ErrorBox error={session.error} retry={session.reload} />
                {session.loading && <Loading />}
            </div>
        );
    return (
        <ErrorBoundary
            fallback={
                <div className="boot">
                    <Brand />
                    <p>{t('Die Ansicht konnte nicht geladen werden.', 'This view could not be loaded.')}</p>
                    <Button onClick={() => location.reload()}>{t('Neu laden', 'Reload')}</Button>
                </div>
            }
        >
            {session.data.authenticated && !expired ? (
                <Shell
                    logout={() => {
                        setExpired(true);
                        session.reload();
                    }}
                />
            ) : (
                <Login
                    forms={session.data.formsAuthenticationEnabled}
                    onLogin={() => {
                        setExpired(false);
                        session.reload();
                    }}
                />
            )}
        </ErrorBoundary>
    );
}
function Brand() {
    return (
        <div className="brand">
            <span className="brand-mark">
                <Gamepad2 size={24} />
            </span>
            <strong>
                ludus<span>.</span>
            </strong>
        </div>
    );
}
export function LanguageSwitch() {
    const { locale, setLocale, t } = useLocale();
    return (
        <button
            className="language"
            type="button"
            aria-label={t('Sprache: Deutsch. Zu Englisch wechseln.', 'Language: English. Switch to German.')}
            onClick={() => setLocale(locale === 'de' ? 'en' : 'de')}
        >
            {locale.toUpperCase()}
        </button>
    );
}
function Login({ forms, onLogin }: { forms: boolean; onLogin: () => void }) {
    const { t } = useLocale();
    const [method, setMethod] = useState(forms ? 'password' : 'key');
    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [apiKey, setApiKey] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    useEffect(() => setMethod(forms ? 'password' : 'key'), [forms]);
    return (
        <div className="login-screen">
            <div className="login-decoration" aria-hidden="true">
                <div className="cartridge">
                    <Gamepad2 size={90} strokeWidth={1} />
                    <span>LUDUS</span>
                    <span className="cartridge-lines" />
                </div>
                <p>
                    {t('DEINE SPIELE.', 'YOUR GAMES.')}
                    <br />
                    {t('DEINE SAMMLUNG.', 'YOUR COLLECTION.')}
                </p>
            </div>
            <main className="login-panel">
                <div className="login-top">
                    <Brand />
                    <LanguageSwitch />
                </div>
                <div className="login-content">
                    <p className="eyebrow">{t('DEINE RETRO-BIBLIOTHEK', 'YOUR RETRO LIBRARY')}</p>
                    <h1>{t('Willkommen zurück.', 'Welcome back.')}</h1>
                    <p className="muted">
                        {t('Ein Zuhause für deine Spielesammlung.', 'A home for your game collection.')}
                    </p>
                    {forms && (
                        <div className="segmented">
                            <Button
                                aria-pressed={method === 'password'}
                                onClick={() => setMethod('password')}
                            >
                                {t('Benutzerkonto', 'Account')}
                            </Button>
                            <Button aria-pressed={method === 'key'} onClick={() => setMethod('key')}>
                                API-Key
                            </Button>
                        </div>
                    )}
                    <form
                        onSubmit={async (event) => {
                            event.preventDefault();
                            setBusy(true);
                            setError(undefined);
                            try {
                                await login(
                                    method === 'key' ? { apiKey: apiKey.trim() } : { username, password },
                                );
                                setPassword('');
                                setApiKey('');
                                onLogin();
                            } catch (err) {
                                setError(err);
                            } finally {
                                setBusy(false);
                            }
                        }}
                    >
                        {method === 'key' ? (
                            <>
                                <Field label="API-Key">
                                    <input
                                        type="password"
                                        autoComplete="off"
                                        required
                                        maxLength={64}
                                        value={apiKey}
                                        onChange={(event) => setApiKey(event.target.value)}
                                    />
                                </Field>
                                <p className="hint">
                                    {t(
                                        'Beim ersten Start liegt der Schlüssel in der Datei bootstrap-api-key im Konfigurationsordner (im Container: /config/bootstrap-api-key).',
                                        'On first startup, the key is in the bootstrap-api-key file in the configuration directory (in Docker: /config/bootstrap-api-key).',
                                    )}
                                </p>
                            </>
                        ) : (
                            <>
                                <Field label={t('Benutzername', 'Username')}>
                                    <input
                                        autoComplete="username"
                                        required
                                        maxLength={128}
                                        value={username}
                                        onChange={(event) => setUsername(event.target.value)}
                                    />
                                </Field>
                                <Field label={t('Passwort', 'Password')}>
                                    <input
                                        type="password"
                                        autoComplete="current-password"
                                        required
                                        maxLength={1024}
                                        value={password}
                                        onChange={(event) => setPassword(event.target.value)}
                                    />
                                </Field>
                            </>
                        )}
                        <ErrorBox error={error} />
                        <Button type="submit" variant="primary" className="full" busy={busy}>
                            {t('Anmelden', 'Sign in')}
                        </Button>
                    </form>
                    <p className="login-foot">
                        <ShieldCheck size={17} />
                        {t(
                            'Privat gehostet. Deine Sammlung bleibt bei dir.',
                            'Self-hosted. Your collection stays yours.',
                        )}
                    </p>
                </div>
            </main>
        </div>
    );
}
function Shell({ logout }: { logout: () => void }) {
    const { t, applyDefaultLocale } = useLocale();
    const location = useLocation();
    const [menu, setMenu] = useState(false);
    const [logoutError, setLogoutError] = useState<unknown>();
    const health = useResource<HealthIssue[]>('system/health', 60000);
    const settings = useResource<{ language: 'de' | 'en'; instanceName: string }>('settings');
    const [mobile, setMobile] = useState(() => window.matchMedia('(max-width: 760px)').matches);
    const sidebar = useRef<HTMLElement>(null);
    const live = useRealtime();
    useEffect(() => {
        if (settings.data) {
            applyDefaultLocale(settings.data.language);
            document.title = settings.data.instanceName;
        }
    }, [settings.data?.language, settings.data?.instanceName]);
    useEffect(() => {
        const media = window.matchMedia('(max-width: 760px)');
        const update = () => {
            setMobile(media.matches);
            if (!media.matches) setMenu(false);
        };
        media.addEventListener('change', update);
        return () => media.removeEventListener('change', update);
    }, []);
    useEffect(() => {
        if (!mobile || !menu) return;
        const previous = document.activeElement as HTMLElement | null;
        const overflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        sidebar.current?.querySelector<HTMLButtonElement>('button')?.focus();
        const handleKey = (event: KeyboardEvent) => {
            if (event.key === 'Escape') {
                event.preventDefault();
                setMenu(false);
            }
            if (event.key !== 'Tab') return;
            const controls = sidebar.current?.querySelectorAll<HTMLElement>('a[href], button:not(:disabled)');
            if (!controls?.length) return;
            const first = controls[0];
            const last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };
        document.addEventListener('keydown', handleKey);
        return () => {
            document.body.style.overflow = overflow;
            document.removeEventListener('keydown', handleKey);
            previous?.focus();
        };
    }, [mobile, menu]);
    useEffect(() => {
        setMenu(false);
        window.scrollTo(0, 0);
    }, [location.pathname]);
    const navigation = [
        ['/', Library, t('Bibliothek', 'Library')],
        ['/add', Plus, t('Spiel hinzufügen', 'Add game')],
        ['/activity', Activity, t('Aktivität', 'Activity')],
        ['/settings', Settings, t('Einstellungen', 'Settings')],
        ['/system', ShieldCheck, t('System', 'System')],
    ] as const;
    return (
        <div className="app-shell">
            <a className="skip-link" href="#main">
                {t('Zum Inhalt', 'Skip to content')}
            </a>
            <div className="mobile-top">
                <Brand />
                <Button
                    aria-label={t('Navigation öffnen', 'Open navigation')}
                    aria-expanded={menu}
                    aria-controls="sidebar"
                    className="icon-button"
                    onClick={() => setMenu(true)}
                >
                    <Menu />
                </Button>
            </div>
            {menu && (
                <button
                    className="nav-shade"
                    tabIndex={-1}
                    aria-label={t('Navigation schließen', 'Close navigation')}
                    onClick={() => setMenu(false)}
                />
            )}
            <aside
                ref={sidebar}
                id="sidebar"
                className={`sidebar ${menu ? 'open' : ''}`}
                inert={mobile && !menu}
                aria-hidden={mobile && !menu}
                role={mobile ? 'dialog' : undefined}
                aria-modal={mobile && menu ? true : undefined}
                aria-label={mobile ? t('Navigation', 'Navigation') : undefined}
            >
                <div className="sidebar-brand">
                    <Brand />
                    <Button
                        className="mobile-close icon-button"
                        aria-label={t('Navigation schließen', 'Close navigation')}
                        onClick={() => setMenu(false)}
                    >
                        <X />
                    </Button>
                </div>
                <p className="sidebar-caption">{t('DEINE SAMMLUNG', 'YOUR COLLECTION')}</p>
                <nav aria-label={t('Hauptnavigation', 'Main navigation')}>
                    {navigation.map(([path, Icon, label]) => (
                        <NavLink key={path} to={path} end={path === '/'} onClick={() => setMenu(false)}>
                            <Icon size={20} />
                            <span>{label}</span>
                            {path === '/system' && !!health.data?.length && (
                                <span className="health-count">{health.data.length}</span>
                            )}
                        </NavLink>
                    ))}
                </nav>
                <div className="sidebar-bottom">
                    <div className="live-state">
                        <span className={`live-dot ${live.state}`} />
                        {live.state === 'connected'
                            ? t('Live verbunden', 'Live connected')
                            : t('Verbindung wird aufgebaut', 'Connecting')}
                    </div>
                    <div className="sidebar-footer">
                        <LanguageSwitch />
                        <Button
                            className="icon-button"
                            aria-label={t('Abmelden', 'Sign out')}
                            onClick={async () => {
                                try {
                                    await api('auth/logout', { method: 'POST' });
                                    resetCsrf();
                                    logout();
                                } catch (error) {
                                    setLogoutError(error);
                                }
                            }}
                        >
                            <LogOut size={19} />
                        </Button>
                    </div>
                </div>
            </aside>
            <main id="main" className="main-content" inert={mobile && menu}>
                <ErrorBox error={logoutError} />
                {live.event && ['scanUpdated', 'importStatus', 'rssUpdated'].includes(live.event.name) && (
                    <div className="live-notice" role="status">
                        {live.event.name === 'scanUpdated'
                            ? t('Bibliothek-Scan aktualisiert', 'Library scan updated')
                            : live.event.name === 'rssUpdated'
                              ? t('RSS-Synchronisierung aktualisiert', 'RSS sync updated')
                              : t('Import-Status aktualisiert', 'Import status updated')}
                    </div>
                )}
                <Suspense fallback={<Loading />}>
                    <Routes>
                        <Route path="/" element={<LibraryPage />} />
                        <Route path="/add" element={<AddPage />} />
                        <Route path="/games/:id" element={<GamePage />} />
                        <Route path="/activity/*" element={<ActivityPage />} />
                        <Route path="/settings/*" element={<SettingsPage />} />
                        <Route path="/system/*" element={<SystemPage />} />
                        <Route path="/library" element={<Navigate replace to="/" />} />
                        <Route
                            path="*"
                            element={
                                <div className="empty">
                                    <h1>404</h1>
                                    <p>{t('Diese Seite gibt es nicht.', 'This page does not exist.')}</p>
                                    <Link className="button primary" to="/">
                                        {t('Zur Bibliothek', 'Open library')}
                                    </Link>
                                </div>
                            }
                        />
                    </Routes>
                </Suspense>
            </main>
        </div>
    );
}
