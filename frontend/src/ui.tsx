import {
    Children,
    cloneElement,
    isValidElement,
    createContext,
    useContext,
    useEffect,
    useId,
    useRef,
    useState,
    type ButtonHTMLAttributes,
    type ReactNode,
    type ReactElement,
} from 'react';
import { AlertCircle, Check, Gamepad2, LoaderCircle, X } from 'lucide-react';
import { ApiError, api, mediaUrl } from './api';
import { useLabel, useLocale } from './i18n';
import type { MediaLink } from './types';

export function Button({
    children,
    variant = '',
    busy,
    className = '',
    disabled,
    ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: string; busy?: boolean }) {
    return (
        <button
            type="button"
            className={`button ${variant} ${className}`}
            disabled={busy || disabled}
            {...props}
        >
            {busy && <LoaderCircle className="spin" size={17} />}
            {children}
        </button>
    );
}
export function Heading({
    title,
    subtitle,
    actions,
}: {
    title: string;
    subtitle?: string;
    actions?: ReactNode;
}) {
    return (
        <header className="page-heading">
            <div>
                <p className="eyebrow">LUDUS / {title}</p>
                <h1>{title}</h1>
                {subtitle && <p className="muted">{subtitle}</p>}
            </div>
            <div className="actions">{actions}</div>
        </header>
    );
}
export function Empty({ title, text, children }: { title: string; text?: string; children?: ReactNode }) {
    return (
        <div className="empty">
            <div className="empty-icon">
                <Gamepad2 size={32} />
            </div>
            <h2>{title}</h2>
            {text && <p className="muted">{text}</p>}
            {children}
        </div>
    );
}
export function Loading() {
    const { t } = useLocale();
    return (
        <div className="loading" role="status">
            <LoaderCircle className="spin" size={21} />
            {t('Wird geladen …', 'Loading…')}
        </div>
    );
}
export function ErrorBox({ error, retry }: { error: unknown; retry?: () => void }) {
    const { t } = useLocale();
    if (!error) return null;
    let message = t(
        'Die Anfrage ist fehlgeschlagen. Bitte erneut versuchen.',
        'The request failed. Please try again.',
    );
    if (error instanceof ApiError) {
        if (error.status === 401)
            message = t(
                'Anmeldung fehlgeschlagen oder Sitzung abgelaufen.',
                'Login failed or session expired.',
            );
        else if (error.status === 409)
            message = t(
                'Die Aktion ist im aktuellen Zustand nicht möglich. Prüfe laufende Aufgaben und verknüpfte Einträge.',
                'This action conflicts with the current state. Check running tasks and linked records.',
            );
        else if (error.status === 400)
            message = t(
                'Bitte Eingaben und Konfiguration prüfen.',
                'Please check the inputs and configuration.',
            );
        else if (error.status === 429)
            message = t('Zu viele Anfragen. Bitte etwas warten.', 'Too many requests. Please wait a moment.');
        else if (error.status === 502 || error.status === 503)
            message = t(
                'Ein Dienst ist nicht erreichbar. Prüfe den Systemstatus und die Zugangsdaten.',
                'A service is unavailable. Check system health and credentials.',
            );
        else if (error.status === 404)
            message = t(
                'Der Eintrag oder das Such-Ticket ist nicht mehr verfügbar. Bitte aktualisieren.',
                'The record or search ticket is no longer available. Please refresh.',
            );
    }
    return (
        <div className="notice error" role="alert">
            <AlertCircle size={20} />
            <div>
                {message}
                {error instanceof ApiError && (
                    <small>
                        {error.code}
                        {error.fields.length > 0 ? ` · ${error.fields.join(', ')}` : ''}
                    </small>
                )}
            </div>
            {retry && <Button onClick={retry}>{t('Erneut versuchen', 'Retry')}</Button>}
        </div>
    );
}
export function Badge({ value, children, tone }: { value?: string; children?: ReactNode; tone?: string }) {
    const label = useLabel();
    const color =
        tone ??
        (['Available', 'VerifiedGoodDump', 'Succeeded', 'Imported'].includes(value ?? '')
            ? 'good'
            : ['Failed', 'Error', 'Modified', 'DownloadFailed'].includes(value ?? '')
              ? 'bad'
              : ['Downloading', 'Importing', 'Warning', 'Running'].includes(value ?? '')
                ? 'warm'
                : '');
    return <span className={`badge ${color}`}>{children ?? label(value ?? '')}</span>;
}
export function Modal({
    title,
    children,
    onClose,
    busy = false,
    wide = false,
}: {
    title: string;
    children: ReactNode;
    onClose: () => void;
    busy?: boolean;
    wide?: boolean;
}) {
    const { t } = useLocale();
    const ref = useRef<HTMLDialogElement>(null);
    const titleId = useId();
    useEffect(() => {
        const previous = document.activeElement as HTMLElement | null;
        ref.current?.showModal();
        return () => {
            previous?.focus();
        };
    }, []);
    return (
        <dialog
            ref={ref}
            className={wide ? 'wide' : ''}
            aria-labelledby={titleId}
            onCancel={(event) => {
                event.preventDefault();
                if (!busy) onClose();
            }}
        >
            <div className="dialog-heading">
                <h2 id={titleId}>{title}</h2>
                <Button
                    className="icon-button"
                    aria-label={t('Schließen', 'Close')}
                    disabled={busy}
                    onClick={onClose}
                >
                    <X size={20} />
                </Button>
            </div>
            {children}
        </dialog>
    );
}
export function Confirm({
    title,
    text,
    onConfirm,
    onClose,
}: {
    title: string;
    text: string;
    onConfirm: () => Promise<unknown>;
    onClose: () => void;
}) {
    const { t } = useLocale();
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <Modal title={title} onClose={onClose} busy={busy}>
            <p className="muted">{text}</p>
            <ErrorBox error={error} />
            <div className="dialog-actions">
                <Button disabled={busy} onClick={onClose}>
                    {t('Abbrechen', 'Cancel')}
                </Button>
                <Button
                    variant="danger"
                    busy={busy}
                    onClick={async () => {
                        setBusy(true);
                        setError(undefined);
                        try {
                            await onConfirm();
                            onClose();
                        } catch (err) {
                            setError(err);
                        } finally {
                            setBusy(false);
                        }
                    }}
                >
                    {t('Bestätigen', 'Confirm')}
                </Button>
            </div>
        </Modal>
    );
}
export function Pager({
    page,
    total,
    pageSize,
    onChange,
}: {
    page: number;
    total: number;
    pageSize: number;
    onChange: (page: number) => void;
}) {
    const { t } = useLocale();
    const pages = Math.max(1, Math.ceil(total / pageSize));
    useEffect(() => {
        if (page > pages) onChange(pages);
    }, [page, pages, onChange]);
    return (
        <div className="pager">
            <span className="muted">
                {total.toLocaleString()} {t('Einträge', 'items')} · {t('Seite', 'Page')} {page} / {pages}
            </span>
            <div className="actions">
                <Button disabled={page <= 1} onClick={() => onChange(page - 1)}>
                    {t('Zurück', 'Previous')}
                </Button>
                <Button disabled={page >= pages} onClick={() => onChange(page + 1)}>
                    {t('Nächste', 'Next')}
                </Button>
            </div>
        </div>
    );
}
export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
    const id = useId();
    const controls = Children.toArray(children);
    const isControl = (
        child: ReactNode,
    ): child is ReactElement<{ id?: string; 'aria-label'?: string; 'aria-describedby'?: string }> =>
        isValidElement(child) &&
        typeof child.type === 'string' &&
        ['input', 'select', 'textarea'].includes(child.type);
    const primary = controls.findIndex((child) => isControl(child) && !child.props['aria-label']);
    const index =
        primary >= 0 ? primary : controls.findIndex((child) => isControl(child) && child.type === 'input');
    const target = controls[index];
    const targetId = isControl(target) ? (target.props.id ?? `${id}-${index}`) : undefined;
    return (
        <div className="field">
            <label htmlFor={targetId}>{label}</label>
            {controls.map((child, i) =>
                isControl(child)
                    ? cloneElement(child, {
                          id: child.props.id ?? `${id}-${i}`,
                          'aria-describedby': hint ? `${id}-hint` : child.props['aria-describedby'],
                      })
                    : child,
            )}
            {hint && (
                <small id={`${id}-hint`} className="muted">
                    {hint}
                </small>
            )}
        </div>
    );
}
export function Cover({
    path,
    title,
    accent = '#ba925e',
}: {
    path?: string | null;
    title: string;
    accent?: string;
}) {
    const [url, setUrl] = useState<string>();
    const [visible, setVisible] = useState(false);
    const [retry, setRetry] = useState(0);
    const ref = useRef<HTMLDivElement>(null);
    const { t } = useLocale();
    useEffect(() => {
        if (!ref.current || !('IntersectionObserver' in window)) {
            setVisible(true);
            return;
        }
        const observer = new IntersectionObserver(
            (entries) => {
                if (entries.some((entry) => entry.isIntersecting)) {
                    setVisible(true);
                    observer.disconnect();
                }
            },
            { rootMargin: '200px' },
        );
        observer.observe(ref.current);
        return () => observer.disconnect();
    }, []);
    useEffect(() => {
        setUrl(undefined);
        if (!path || !visible) return;
        const controller = new AbortController();
        let timer: ReturnType<typeof setTimeout>;
        const refresh = async () => {
            try {
                const link = await api<MediaLink>('media/links', {
                    method: 'POST',
                    body: { kind: 'cover', coverRelativePath: path },
                    signal: controller.signal,
                });
                if (!controller.signal.aborted) {
                    setUrl(mediaUrl(link.url));
                    timer = setTimeout(
                        () => void refresh(),
                        Math.max(5000, new Date(link.expiresAtUtc).valueOf() - Date.now() - 10000),
                    );
                }
            } catch {
                if (!controller.signal.aborted) setUrl(undefined);
            }
        };
        void refresh();
        return () => {
            controller.abort();
            clearTimeout(timer);
        };
    }, [path, visible, retry]);
    return (
        <div
            ref={ref}
            className="cover"
            style={
                {
                    '--platform-color': /^#[0-9a-f]{6}$/i.test(accent) ? accent : '#ba925e',
                } as React.CSSProperties
            }
        >
            {url ? (
                <img
                    src={url}
                    alt={title}
                    loading="lazy"
                    onError={() => {
                        setUrl(undefined);
                        if (!retry) setRetry(1);
                    }}
                />
            ) : (
                <div className="cover-fallback" aria-label={t('Kein Cover verfügbar', 'No cover available')}>
                    <Gamepad2 size={44} strokeWidth={1.2} />
                    <span>{title}</span>
                    <small>{t('LUDUS SAMMLUNG', 'LUDUS COLLECTION')}</small>
                </div>
            )}
        </div>
    );
}
const ToastContext = createContext<(message: string) => void>(() => {});
export function ToastProvider({ children }: { children: ReactNode }) {
    const [message, setMessage] = useState('');
    const { t } = useLocale();
    useEffect(() => {
        if (message) {
            const timer = setTimeout(() => setMessage(''), 6000);
            return () => clearTimeout(timer);
        }
    }, [message]);
    return (
        <ToastContext value={setMessage}>
            {children}
            {message && (
                <div className="toast" role="status">
                    <Check size={20} />
                    <span>{message}</span>
                    <Button
                        className="icon-button"
                        aria-label={t('Schließen', 'Close')}
                        onClick={() => setMessage('')}
                    >
                        <X size={18} />
                    </Button>
                </div>
            )}
        </ToastContext>
    );
}
export const useToast = () => useContext(ToastContext);
export function Action({
    run,
    children,
    ...props
}: { run: () => Promise<unknown>; children: ReactNode } & Omit<Parameters<typeof Button>[0], 'onClick'>) {
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <>
            <Button
                {...props}
                busy={busy}
                onClick={async () => {
                    setBusy(true);
                    setError(undefined);
                    try {
                        await run();
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                {children}
            </Button>
            {error != null && <ErrorBox error={error} />}
        </>
    );
}
