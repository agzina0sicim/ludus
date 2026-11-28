import { useState, type ReactNode } from 'react';
import { Plus, Pencil, Trash2, PlugZap, LayoutGrid, List } from 'lucide-react';
import { SettingsActions } from './SettingsToolbar';
import { api } from './api';
import { useResource } from './hooks';
import { useLabel, useLocale } from './i18n';
import { Action, Badge, Button, Confirm, Empty, ErrorBox, Field, Loading, Modal, useToast } from './ui';
import type { Row } from './types';

export type Values = Record<string, unknown>;
export type FormField = {
    key: string;
    label: [string, string];
    type?:
        | 'number'
        | 'nullableNumber'
        | 'boolean'
        | 'secret'
        | 'select'
        | 'list'
        | 'numbers'
        | 'textarea'
        | 'color'
        | 'url';
    options?: string[];
    required?: boolean;
    min?: number;
    max?: number;
    maxLength?: number;
    pattern?: string;
    hint?: [string, string];
};
export function payload(fields: FormField[], values: Values): Values {
    return Object.fromEntries(
        fields.map((field) => {
            const value = values[field.key];
            if (field.type === 'secret')
                return [field.key, value === undefined || value === null ? null : String(value)];
            if (field.type === 'nullableNumber')
                return [field.key, value === '' || value == null ? null : Number(value)];
            if (field.type === 'number') return [field.key, Number(value)];
            if (field.type === 'list' || field.type === 'numbers') {
                const list = Array.isArray(value)
                    ? value
                    : String(value ?? '')
                          .split(/[\n,]+/)
                          .map((item) => item.trim())
                          .filter(Boolean);
                return [field.key, field.type === 'numbers' ? list.map(Number) : list];
            }
            if (field.type === 'boolean') return [field.key, Boolean(value)];
            return [field.key, value ?? ''];
        }),
    );
}
export function Fields({
    fields,
    values,
    onChange,
}: {
    fields: FormField[];
    values: Values;
    onChange: (values: Values) => void;
}) {
    const { t } = useLocale();
    const label = useLabel();
    return (
        <div className="form-grid">
            {fields.map((field) => {
                const value = values[field.key];
                const update = (next: unknown) => onChange({ ...values, [field.key]: next });
                if (field.type === 'boolean')
                    return (
                        <label className="check field-check" key={field.key}>
                            <input
                                type="checkbox"
                                checked={Boolean(value)}
                                onChange={(event) => update(event.target.checked)}
                            />
                            <span>
                                {t(...field.label)}
                                {field.hint && <small>{t(...field.hint)}</small>}
                            </span>
                        </label>
                    );
                if (field.type === 'secret')
                    return (
                        <SecretField
                            key={field.key}
                            field={field}
                            value={value}
                            configured={Boolean(values[field.key + 'Configured'])}
                            update={update}
                        />
                    );
                const string = Array.isArray(value) ? value.join(', ') : String(value ?? '');
                return (
                    <Field key={field.key} label={t(...field.label)} hint={field.hint && t(...field.hint)}>
                        {field.type === 'select' ? (
                            <select
                                value={string}
                                required={field.required}
                                onChange={(event) => update(event.target.value)}
                            >
                                {field.options?.map((option) => (
                                    <option key={option} value={option}>
                                        {label(option)}
                                    </option>
                                ))}
                            </select>
                        ) : field.type === 'textarea' ? (
                            <textarea
                                value={string}
                                required={field.required}
                                maxLength={field.maxLength ?? 4096}
                                onChange={(event) => update(event.target.value)}
                            />
                        ) : (
                            <input
                                type={
                                    field.type === 'number' || field.type === 'nullableNumber'
                                        ? 'number'
                                        : field.type === 'color'
                                          ? 'color'
                                          : field.type === 'url'
                                            ? 'url'
                                            : 'text'
                                }
                                value={string}
                                required={field.required}
                                min={field.min}
                                max={field.max}
                                maxLength={field.maxLength ?? 4096}
                                pattern={field.pattern}
                                step={1}
                                onChange={(event) => update(event.target.value)}
                            />
                        )}
                    </Field>
                );
            })}
        </div>
    );
}
function SecretField({
    field,
    value,
    configured,
    update,
}: {
    field: FormField;
    value: unknown;
    configured: boolean;
    update: (next: unknown) => void;
}) {
    const { t } = useLocale();
    const [mode, setMode] = useState(configured ? 'keep' : 'replace');
    return (
        <Field
            label={t(...field.label)}
            hint={
                configured
                    ? t('Verschlüsselt gespeichert', 'Stored encrypted')
                    : t('Noch nicht hinterlegt', 'Not configured')
            }
        >
            <select
                aria-label={`${t(...field.label)}: ${t('Aktion', 'action')}`}
                value={mode}
                onChange={(event) => {
                    setMode(event.target.value);
                    update(event.target.value === 'keep' ? null : '');
                }}
            >
                <option value="keep">{t('Unverändert lassen', 'Keep unchanged')}</option>
                <option value="replace">{t('Neuen Wert setzen', 'Set new value')}</option>
                <option value="clear">{t('Gespeicherten Wert löschen', 'Clear stored value')}</option>
            </select>
            {mode === 'replace' && (
                <input
                    aria-label={t(...field.label)}
                    type="password"
                    autoComplete="new-password"
                    value={String(value ?? '')}
                    maxLength={4096}
                    required={field.required || configured}
                    onChange={(event) => update(event.target.value)}
                />
            )}
        </Field>
    );
}
export function Editor({
    title,
    initial,
    fields,
    onSave,
    onClose,
    children,
}: {
    title: string;
    initial: Values;
    fields: FormField[];
    onSave: (values: Values) => Promise<unknown>;
    onClose: () => void;
    children?: (values: Values, setValues: (next: Values) => void) => ReactNode;
}) {
    const { t } = useLocale();
    const [values, setValues] = useState(initial);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<unknown>();
    return (
        <Modal title={title} onClose={onClose} busy={busy} wide>
            <form
                onSubmit={async (event) => {
                    event.preventDefault();
                    setBusy(true);
                    setError(undefined);
                    try {
                        await onSave(children ? values : payload(fields, values));
                        onClose();
                    } catch (err) {
                        setError(err);
                    } finally {
                        setBusy(false);
                    }
                }}
            >
                <fieldset disabled={busy}>
                    <Fields fields={fields} values={values} onChange={setValues} />
                    {children?.(values, setValues)}
                </fieldset>
                <ErrorBox error={error} />
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
export function CrudSection({
    title,
    endpoint,
    fields,
    defaults,
    columns,
    testEndpoint,
    extra,
    cards = false,
    allowCreate = true,
    idKey = 'id',
}: {
    title: string;
    endpoint: string;
    fields: FormField[];
    defaults: Values;
    columns: { key: string; label: string }[];
    testEndpoint?: (row: Row) => string;
    extra?: (row: Row) => ReactNode;
    allowCreate?: boolean;
    cards?: boolean;
    idKey?: string;
}) {
    const { t } = useLocale();
    const label = useLabel();
    const toast = useToast();
    const data = useResource<Row[]>(endpoint);
    const [edit, setEdit] = useState<Values>();
    const [remove, setRemove] = useState<Row>();
    const [tableView, setTableView] = useState(false);
    const showCards = cards && !tableView;
    const rowActions = (row: Row) => (
        <div className="actions">
            {extra?.(row)}
            {testEndpoint && (
                <Action
                    run={async () => {
                        await api(testEndpoint(row), { method: 'POST' });
                        toast(t('Verbindung erfolgreich getestet.', 'Connection test succeeded.'));
                    }}
                >
                    <PlugZap size={17} />
                    {t('Testen', 'Test')}
                </Action>
            )}
            <Button
                className="icon-button"
                aria-label={`${t('Bearbeiten', 'Edit')}: ${String(row.name ?? row.provider ?? row.host ?? row.id)}`}
                onClick={() => setEdit({ ...row })}
            >
                <Pencil size={17} />
            </Button>
            {allowCreate && (
                <Button
                    className="icon-button"
                    aria-label={`${t('Entfernen', 'Remove')}: ${String(row.name ?? row.host ?? row.id)}`}
                    onClick={() => setRemove(row)}
                >
                    <Trash2 size={17} />
                </Button>
            )}
        </div>
    );
    const cellValue = (value: unknown) =>
        typeof value === 'boolean' ? (
            <Badge tone={value ? 'good' : ''}>
                {value ? t('Aktiv', 'Enabled') : t('Inaktiv', 'Disabled')}
            </Badge>
        ) : Array.isArray(value) ? (
            (value as unknown[]).join(', ')
        ) : (
            label(String(value ?? '—'))
        );
    return (
        <section className="settings-section">
            {cards && (
                <SettingsActions>
                    <Button aria-pressed={tableView} onClick={() => setTableView(!tableView)}>
                        {tableView ? <LayoutGrid /> : <List />}
                        {tableView ? t('Karten', 'Cards') : t('Verwalten', 'Manage')}
                    </Button>
                </SettingsActions>
            )}
            <div className="section-heading">
                <div>
                    <h2>{title}</h2>
                </div>
                {allowCreate && !showCards && (
                    <Button onClick={() => setEdit({ ...defaults })}>
                        <Plus size={17} />
                        {t('Hinzufügen', 'Add')}
                    </Button>
                )}
            </div>
            <ErrorBox error={data.error} retry={data.reload} />
            {data.loading && !data.data ? (
                <Loading />
            ) : showCards && data.data ? (
                <div className="provider-grid">
                    {data.data.map((row) => (
                        <article className="provider-card" key={String(row[idKey])}>
                            <h3>{cellValue(row[columns[0].key])}</h3>
                            <dl>
                                {columns.slice(1).map((column) => (
                                    <div key={column.key}>
                                        <dt>{column.label}</dt>
                                        <dd>{cellValue(row[column.key])}</dd>
                                    </div>
                                ))}
                            </dl>
                            {rowActions(row)}
                        </article>
                    ))}
                    {allowCreate && (
                        <button
                            className="provider-add"
                            aria-label={t('Hinzufügen', 'Add')}
                            title={t('Hinzufügen', 'Add')}
                            onClick={() => setEdit({ ...defaults })}
                        >
                            <Plus />
                        </button>
                    )}
                </div>
            ) : data.data?.length === 0 ? (
                <Empty title={t('Noch keine Einträge', 'No entries yet')} />
            ) : (
                <div className="table-wrap">
                    <table>
                        <thead>
                            <tr>
                                {columns.map((column) => (
                                    <th key={column.key}>{column.label}</th>
                                ))}
                                <th>{t('Aktionen', 'Actions')}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.data?.map((row) => (
                                <tr key={String(row[idKey])}>
                                    {columns.map((column) => (
                                        <td key={column.key}>{cellValue(row[column.key])}</td>
                                    ))}
                                    <td>{rowActions(row)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            {edit && (
                <Editor
                    title={title}
                    fields={fields}
                    initial={edit}
                    onClose={() => setEdit(undefined)}
                    onSave={async (values) => {
                        const existing = edit[idKey] !== undefined;
                        await api(existing ? `${endpoint}/${edit[idKey]}` : endpoint, {
                            method: existing ? 'PUT' : 'POST',
                            body: values,
                        });
                        toast(t('Gespeichert.', 'Saved.'));
                    }}
                />
            )}
            {remove && (
                <Confirm
                    title={t('Eintrag entfernen?', 'Remove entry?')}
                    text={t(
                        'Verknüpfte Einträge können das Entfernen verhindern.',
                        'Linked records may prevent removal.',
                    )}
                    onClose={() => setRemove(undefined)}
                    onConfirm={() => api(`${endpoint}/${remove[idKey]}`, { method: 'DELETE' })}
                />
            )}
        </section>
    );
}
