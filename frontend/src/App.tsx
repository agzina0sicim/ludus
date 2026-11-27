import { Component, lazy, Suspense, useEffect, useRef, useState, type ReactNode } from 'react';
import { Gamepad2, X } from 'lucide-react';
import { Link, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { api, login, resetCsrf } from './api';
import { useResource } from './hooks';
import { configureDisplaySettings, useLocale } from './i18n';
import { ShellHeader, SidebarNavigation } from './ShellNavigation';
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
            {session.data.localSetupRequired ? (
                <LocalAccountSetup />
            ) : session.data.setupRequired ? (
                <AccountSetup
                    onComplete={() => {
                        setExpired(false);
                        session.reload();
                    }}
                />
            ) : session.data.authenticated && !expired ? (
                <Shell
                    logout={() => {
                        setExpired(true);
                        session.reload();
                    }}
                />
            ) : (
                <Login
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
function Login({ onLogin }: { onLogin: () => void }) {
    const { t } = useLocale();
    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <div className="login-screen">
            <main className="login-panel">
                <form
                    className="login-content"
                    aria-label={t('Anmelden', 'Sign in')}
                    onSubmit={async (event) => {
                        event.preventDefault();
                        setBusy(true);
                        setError(undefined);
                        try {
                            await login({ username, password });
                            setPassword('');
                            onLogin();
                        } catch (err) {
                            setError(err);
                        } finally {
                            setBusy(false);
                        }
                    }}
                >
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
                    <ErrorBox error={error} />
                    <Button type="submit" variant="primary" className="full" busy={busy}>
                        {t('Anmelden', 'Sign in')}
                    </Button>
                </form>
            </main>
        </div>
    );
}

function AccountSetup({ onComplete }: { onComplete: () => void }) {
    const { t } = useLocale();
    const [username, setUsername] = useState('administrator');
    const [password, setPassword] = useState('');
    const [confirmation, setConfirmation] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <div className="login-screen">
            <main className="login-panel setup-panel">
                <div className="login-top">
                    <Brand />
                    <LanguageSwitch />
                </div>
                <div className="login-content">
                    <p className="eyebrow">{t('ERSTE EINRICHTUNG', 'INITIAL SETUP')}</p>
                    <h1>{t('Administratorkonto einrichten', 'Set up administrator account')}</h1>
                    <p className="muted">
                        {t(
                            'Lege die Zugangsdaten für diese neue Ludus-Instanz fest.',
                            'Create credentials for this new Ludus instance.',
                        )}
                    </p>
                    <form
                        onSubmit={async (event) => {
                            event.preventDefault();
                            setError(undefined);
                            if (password !== confirmation) {
                                setError(
                                    new Error(
                                        t(
                                            'Die Passwörter stimmen nicht überein.',
                                            'The passwords do not match.',
                                        ),
                                    ),
                                );
                                return;
                            }
                            setBusy(true);
                            try {
                                resetCsrf();
                                await api('auth/setup', {
                                    method: 'POST',
                                    body: {
                                        username,
                                        password,
                                    },
                                });
                                resetCsrf();
                                onComplete();
                            } catch (err) {
                                setError(err);
                            } finally {
                                setBusy(false);
                            }
                        }}
                    >
                        <Field label={t('Benutzername', 'Username')}>
                            <input
                                autoComplete="username"
                                required
                                maxLength={128}
                                value={username}
                                onChange={(event) => setUsername(event.target.value)}
                            />
                        </Field>
                        <Field
                            label={t('Passwort (mindestens 12 Zeichen)', 'Password (at least 12 characters)')}
                        >
                            <input
                                type="password"
                                autoComplete="new-password"
                                required
                                minLength={12}
                                maxLength={1024}
                                value={password}
                                onChange={(event) => setPassword(event.target.value)}
                            />
                        </Field>
                        <Field label={t('Passwort wiederholen', 'Confirm password')}>
                            <input
                                type="password"
                                autoComplete="new-password"
                                required
                                minLength={12}
                                maxLength={1024}
                                value={confirmation}
                                onChange={(event) => setConfirmation(event.target.value)}
                            />
                        </Field>
                        <ErrorBox error={error} />
                        <Button type="submit" variant="primary" className="full" busy={busy}>
                            {t('Konto erstellen', 'Create account')}
                        </Button>
                    </form>
                </div>
            </main>
        </div>
    );
}

function LocalAccountSetup() {
    const { t } = useLocale();
    return (
        <div className="login-screen">
            <main className="login-panel setup-panel">
                <div className="login-top">
                    <Brand />
                    <LanguageSwitch />
                </div>
                <div className="login-content">
                    <p className="eyebrow">{t('LOKALE EINRICHTUNG', 'LOCAL SETUP')}</p>
                    <h1>
                        {t('Administratorkonto lokal einrichten', 'Set up administrator account locally')}
                    </h1>
                    <p className="muted">
                        {t(
                            'Diese aktualisierte Instanz kann nicht im Browser übernommen werden. Stoppe Ludus und führe auf dem Server „Ludus.Api account set“ aus.',
                            'This upgraded instance cannot be claimed in the browser. Stop Ludus and run “Ludus.Api account set” on the server.',
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
    const settings = useResource<{
        language: 'de' | 'en';
        instanceName: string;
        theme: 'Light' | 'Dark' | 'Auto';
        shortDateFormat: string;
        longDateFormat: string;
        use12HourTime: boolean;
    }>('settings');
    const [mobile, setMobile] = useState(() => window.matchMedia('(max-width: 760px)').matches);
    const sidebar = useRef<HTMLElement>(null);
    const main = useRef<HTMLElement>(null);
    const opener = useRef<HTMLElement | null>(null);
    const live = useRealtime();
    useEffect(() => {
        if (settings.data) {
            applyDefaultLocale(settings.data.language);
            document.title = settings.data.instanceName;
            configureDisplaySettings(settings.data);
        }
    }, [
        settings.data?.language,
        settings.data?.instanceName,
        settings.data?.shortDateFormat,
        settings.data?.longDateFormat,
        settings.data?.use12HourTime,
    ]);
    useEffect(() => {
        if (!settings.data) return;
        const media = window.matchMedia('(prefers-color-scheme: dark)');
        const apply = () => {
            document.documentElement.dataset.theme =
                settings.data!.theme === 'Auto'
                    ? media.matches
                        ? 'dark'
                        : 'light'
                    : settings.data!.theme.toLowerCase();
        };
        apply();
        media.addEventListener('change', apply);
        return () => media.removeEventListener('change', apply);
    }, [settings.data?.theme]);
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
        const previous = opener.current;
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
        if (main.current) main.current.scrollTop = 0;
    }, [location.pathname]);
    return (
        <div className="app-shell">
            <a className="skip-link" href="#main">
                {t('Zum Inhalt', 'Skip to content')}
            </a>
            <ShellHeader
                brand={<Brand />}
                languageSwitch={<LanguageSwitch />}
                connected={live.state === 'connected'}
                menuOpen={mobile && menu}
                openMenu={() => {
                    opener.current = document.activeElement as HTMLElement;
                    setMenu(true);
                }}
                signOut={async () => {
                    try {
                        await api('auth/logout', { method: 'POST' });
                        resetCsrf();
                        logout();
                    } catch (error) {
                        setLogoutError(error);
                    }
                }}
            />
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
                <Button
                    className="mobile-close icon-button"
                    aria-label={t('Navigation schließen', 'Close navigation')}
                    onClick={() => setMenu(false)}
                >
                    <X />
                </Button>
                <SidebarNavigation healthCount={health.data?.length ?? 0} closeMenu={() => setMenu(false)} />
            </aside>
            <main ref={main} tabIndex={-1} id="main" className="main-content" inert={mobile && menu}>
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
