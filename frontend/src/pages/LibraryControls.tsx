import { useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import {
    ArrowDown,
    ArrowUp,
    ArrowUpDown,
    Check,
    ChevronDown,
    Eye,
    Funnel,
    Grid2X2,
    Trash2,
    type LucideIcon,
} from 'lucide-react';
import { useLabel, useLocale } from '../i18n';
import { Button, Field, Modal } from '../ui';
import type { Platform, RootFolder } from '../types';
import {
    columnNames,
    columns,
    filterKeys,
    initialPresets,
    persist,
    posterNames,
    sortNames,
    type Filters,
    type Options,
    type PosterField,
    type Preset,
    type Sort,
    type View,
} from './library-preferences';

const viewNames = {
    grid: ['Poster', 'Posters'],
    table: ['Tabelle', 'Table'],
    overview: ['Übersicht', 'Overview'],
} satisfies Record<string, [string, string]>;
const quickNames = {
    '': ['Alle Spiele', 'All games'],
    monitored: ['Überwacht', 'Monitored'],
    unmonitored: ['Nicht überwacht', 'Unmonitored'],
    missing: ['Fehlend', 'Missing'],
    downloading: ['Wird heruntergeladen', 'Downloading'],
    available: ['Vorhanden', 'Available'],
} satisfies Record<string, [string, string]>;
function sameFilters(a: Filters, b: Filters) {
    return filterKeys.every((k) => (a[k] || '') === (b[k] || ''));
}

function Menu({
    id,
    title,
    icon: Icon,
    children,
    active = false,
}: {
    id: string;
    title: string;
    icon: LucideIcon;
    children: ReactNode;
    active?: boolean;
}) {
    return (
        <div className="library-menu">
            <Button
                id={id + '-trigger'}
                popoverTarget={id}
                style={{ anchorName: '--' + id }}
                className={active ? 'has-filter' : ''}
            >
                <Icon />
                <span>{title}</span>
                <ChevronDown className="menu-caret" size={10} />
            </Button>
            <div
                id={id}
                popover="auto"
                className="library-dropdown"
                style={{ positionAnchor: '--' + id }}
                role="group"
                aria-label={title}
                onKeyDown={(event) => {
                    if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
                    const items = Array.from(event.currentTarget.querySelectorAll('button'));
                    const i = items.indexOf(document.activeElement as HTMLButtonElement);
                    const next =
                        event.key === 'Home'
                            ? 0
                            : event.key === 'End'
                              ? items.length - 1
                              : (i + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
                    event.preventDefault();
                    items[next]?.focus();
                }}
            >
                {children}
            </div>
        </div>
    );
}
function Choice({
    menu,
    children,
    selected,
    descending,
    onClick,
}: {
    menu: string;
    children: ReactNode;
    selected?: boolean;
    descending?: boolean;
    onClick: () => void;
}) {
    const { t } = useLocale();
    const Mark = descending === undefined ? Check : descending ? ArrowDown : ArrowUp;
    return (
        <button
            type="button"
            popoverTarget={menu}
            popoverTargetAction="hide"
            aria-pressed={selected}
            aria-description={
                selected && descending !== undefined
                    ? descending
                        ? t('Absteigend', 'Descending')
                        : t('Aufsteigend', 'Ascending')
                    : undefined
            }
            onClick={onClick}
        >
            <span className="selection-mark">{selected && <Mark size={15} />}</span>
            {children}
        </button>
    );
}
function Toggle({
    label,
    checked,
    change,
}: {
    label: string;
    checked: boolean;
    change: (v: boolean) => void;
}) {
    return (
        <label className="library-option">
            <span>{label}</span>
            <input type="checkbox" checked={checked} onChange={(e) => change(e.target.checked)} />
        </label>
    );
}

type Props = {
    view: View;
    changeView: (v: View) => void;
    options: Options;
    changeOptions: (v: Options) => void;
    filters: Filters;
    applyFilters: (filters: Filters) => void;
    sort: Sort;
    descending: boolean;
    changeSort: (s: Sort) => void;
    platforms: Platform[];
    roots: RootFolder[];
};
export default function LibraryControls({
    view,
    changeView,
    options,
    changeOptions,
    filters,
    applyFilters,
    sort,
    descending,
    changeSort,
    platforms,
    roots,
}: Props) {
    const { t } = useLocale();
    const [dialog, setDialog] = useState<'options' | 'filters'>();
    const [presets, setPresets] = useState(initialPresets);
    function updatePresets(next: Preset[]) {
        setPresets(next);
        persist('ludus.library.filters', JSON.stringify(next));
    }
    function close() {
        const trigger = document.getElementById(
            dialog === 'filters' ? 'lib-filter-trigger' : 'library-options',
        );
        setDialog(undefined);
        requestAnimationFrame(() => trigger?.focus());
    }
    return (
        <>
            <div className="library-tools library-controls">
                <Button id="library-options" onClick={() => setDialog('options')}>
                    <Grid2X2 />
                    {t('Optionen', 'Options')}
                </Button>
                <Menu id="lib-view" title={t('Ansicht', 'View')} icon={Eye}>
                    {(['table', 'grid', 'overview'] as const).map((v) => (
                        <Choice key={v} menu="lib-view" selected={v === view} onClick={() => changeView(v)}>
                            {t(...viewNames[v])}
                        </Choice>
                    ))}
                </Menu>
                <Menu id="lib-sort" title={t('Sortierung', 'Sort')} icon={ArrowUpDown}>
                    {(Object.keys(sortNames) as Sort[]).map((s) => (
                        <Choice
                            key={s}
                            menu="lib-sort"
                            selected={s === sort}
                            descending={descending}
                            onClick={() => changeSort(s)}
                        >
                            {t(...sortNames[s])}
                        </Choice>
                    ))}
                </Menu>
                <Menu
                    id="lib-filter"
                    title={t('Filter', 'Filters')}
                    icon={Funnel}
                    active={Object.keys(filters).length > 0}
                >
                    {Object.entries(quickNames).map(([status, labels]) => {
                        const next = status ? { status } : {};
                        return (
                            <Choice
                                key={status}
                                menu="lib-filter"
                                selected={sameFilters(filters, next)}
                                onClick={() => applyFilters(next)}
                            >
                                {t(...labels)}
                            </Choice>
                        );
                    })}
                    {presets.length > 0 && <hr />}
                    {presets.map((p) => (
                        <Choice
                            key={p.name}
                            menu="lib-filter"
                            selected={sameFilters(filters, p.filters)}
                            onClick={() => applyFilters(p.filters)}
                        >
                            {p.name}
                        </Choice>
                    ))}
                    <hr />
                    <Choice menu="lib-filter" onClick={() => setDialog('filters')}>
                        {t('Erweiterte Filter …', 'Advanced filters…')}
                    </Choice>
                </Menu>
            </div>
            {dialog === 'options' &&
                createPortal(
                    <Modal title={t('Optionen', 'Options') + ' · ' + t(...viewNames[view])} onClose={close}>
                        <div className="library-options">
                            {view === 'grid' && (
                                <>
                                    <Field label={t('Postergröße', 'Poster size')}>
                                        <select
                                            value={options.size}
                                            onChange={(e) =>
                                                changeOptions({
                                                    ...options,
                                                    size: e.target.value as Options['size'],
                                                })
                                            }
                                        >
                                            <option value="small">{t('Klein', 'Small')}</option>
                                            <option value="medium">{t('Mittel', 'Medium')}</option>
                                            <option value="large">{t('Groß', 'Large')}</option>
                                        </select>
                                    </Field>
                                    {(Object.keys(posterNames) as PosterField[]).map((k) => (
                                        <Toggle
                                            key={k}
                                            label={t(...posterNames[k])}
                                            checked={options.poster[k]}
                                            change={(v) =>
                                                changeOptions({
                                                    ...options,
                                                    poster: { ...options.poster, [k]: v },
                                                })
                                            }
                                        />
                                    ))}
                                </>
                            )}
                            {view === 'table' &&
                                columns.map((k) => (
                                    <Toggle
                                        key={k}
                                        label={t(...columnNames[k])}
                                        checked={options.table[k]}
                                        change={(v) =>
                                            changeOptions({ ...options, table: { ...options.table, [k]: v } })
                                        }
                                    />
                                ))}
                            {view === 'overview' && (
                                <Toggle
                                    label={t('Kurzbeschreibung', 'Description')}
                                    checked={options.overviewDescription}
                                    change={(v) => changeOptions({ ...options, overviewDescription: v })}
                                />
                            )}
                        </div>
                        <div className="dialog-actions">
                            <Button onClick={close}>{t('Fertig', 'Done')}</Button>
                        </div>
                    </Modal>,
                    document.body,
                )}
            {dialog === 'filters' &&
                createPortal(
                    <AdvancedFilters
                        filters={filters}
                        platforms={platforms}
                        roots={roots}
                        presets={presets}
                        updatePresets={updatePresets}
                        onClose={close}
                        apply={(next) => {
                            applyFilters(next);
                            close();
                        }}
                    />,
                    document.body,
                )}
        </>
    );
}

function AdvancedFilters({
    filters,
    platforms,
    roots,
    presets,
    updatePresets,
    onClose,
    apply,
}: {
    filters: Filters;
    platforms: Platform[];
    roots: RootFolder[];
    presets: Preset[];
    updatePresets: (p: Preset[]) => void;
    onClose: () => void;
    apply: (f: Filters) => void;
}) {
    const { t } = useLocale();
    const label = useLabel();
    const [draft, setDraft] = useState(filters);
    const [name, setName] = useState('');
    const duplicate = presets.some((p) => p.name.toLowerCase() === name.trim().toLowerCase());
    const fields = [
        [
            'platformId',
            t('Plattform', 'Platform'),
            [['', t('Alle Plattformen', 'All platforms')], ...platforms.map((p) => [String(p.id), p.name])],
        ],
        [
            'rootFolderId',
            'Root Folder',
            [['', t('Alle Root Folders', 'All root folders')], ...roots.map((r) => [String(r.id), r.name])],
        ],
        [
            'status',
            'Status',
            [
                ...Object.entries(quickNames).map(([k, v]) => [k, t(...v)]),
                ['cutoffUnmet', t('Cutoff nicht erreicht', 'Cutoff unmet')],
            ],
        ],
        [
            'verification',
            t('Verifikation', 'Verification'),
            [
                ['', t('Jede Verifikation', 'Any verification')],
                ...['Unverified', 'VerifiedGoodDump', 'Modified'].map((v) => [v, label(v)]),
            ],
        ],
    ] as const;
    return (
        <Modal title={t('Erweiterte Filter', 'Advanced filters')} onClose={onClose}>
            <form
                onSubmit={(e) => {
                    e.preventDefault();
                    apply(draft);
                }}
            >
                {fields.map(([key, title, choices]) => (
                    <Field key={key} label={title}>
                        <select
                            value={draft[key] ?? ''}
                            onChange={(e) => {
                                const next = { ...draft };
                                if (e.target.value) next[key] = e.target.value;
                                else delete next[key];
                                setDraft(next);
                            }}
                        >
                            {choices.map(([value, text]) => (
                                <option key={value} value={value}>
                                    {text}
                                </option>
                            ))}
                        </select>
                    </Field>
                ))}
                <Field label={t('Filtername', 'Filter name')}>
                    <input value={name} maxLength={80} onChange={(e) => setName(e.target.value)} />
                </Field>
                {duplicate && (
                    <p className="hint">
                        {t('Dieser Name ist bereits vergeben.', 'This name is already in use.')}
                    </p>
                )}
                {presets.length > 0 && (
                    <ul className="saved-filter-list">
                        {presets.map((p) => (
                            <li key={p.name}>
                                <span>{p.name}</span>
                                <Button
                                    className="icon-button"
                                    aria-label={t('Filter entfernen', 'Remove filter') + ' ' + p.name}
                                    onClick={() =>
                                        updatePresets(presets.filter((item) => item.name !== p.name))
                                    }
                                >
                                    <Trash2 size={15} />
                                </Button>
                            </li>
                        ))}
                    </ul>
                )}
                <div className="dialog-actions">
                    <Button
                        disabled={!name.trim() || duplicate || !Object.keys(draft).length}
                        onClick={() => {
                            updatePresets([...presets, { name: name.trim(), filters: draft }]);
                            apply(draft);
                        }}
                    >
                        {t('Filter speichern', 'Save filter')}
                    </Button>
                    <Button variant="primary" type="submit">
                        {t('Anwenden', 'Apply')}
                    </Button>
                </div>
            </form>
        </Modal>
    );
}
