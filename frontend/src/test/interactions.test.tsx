import { render, screen, waitFor, renderHook, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { LocaleProvider } from '../i18n';
import { InteractiveSearch } from '../pages/GameDetails';
import { Fields, payload } from '../forms';
import { providerFields } from '../settings-schema';
import { useState } from 'react';
import { useResource } from '../hooks';
import { resetCsrf } from '../api';
import { Field } from '../ui';

const json = (body: unknown) =>
    new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });

it('labels selects independently of their options and help text', () => {
    render(
        <Field label="Plattform" hint="Wähle eine Plattform">
            <select>
                <option>NES</option>
                <option>SNES</option>
            </select>
        </Field>,
    );
    expect(screen.getByLabelText('Plattform', { exact: true })).toBe(
        screen.getByRole('combobox', { name: 'Plattform' }),
    );
    expect(screen.getByRole('combobox')).toHaveAccessibleDescription('Wähle eine Plattform');
});
beforeEach(() => {
    localStorage.setItem('ludus.locale', 'de');
    resetCsrf();
});
it('shows rejected releases with reasons and only allows accepted grabs', async () => {
    const make = (accepted: boolean, title: string) => ({
        release: { indexerId: 1, releaseId: title, title, protocol: 'Torrent', size: 100, seeders: 5 },
        parsed: { regions: ['Europe'], extension: '.nes' },
        ticket: title,
        decision: {
            score: accepted ? 100 : -10000,
            accepted,
            formats: [],
            rejections: accepted
                ? []
                : [{ code: 'Beta', messageDe: 'Beta abgelehnt', messageEn: 'Beta rejected' }],
        },
    });
    const fetch = vi.fn(async (url: string) =>
        url.endsWith('/search')
            ? json({
                  releases: [make(false, 'Rejected beta'), make(true, 'Accepted game')],
                  failures: [],
                  truncated: false,
              })
            : url.endsWith('/csrf')
              ? json({ token: 't' })
              : json({ downloadId: 1 }),
    );
    vi.stubGlobal('fetch', fetch);
    render(
        <LocaleProvider>
            <MemoryRouter>
                <InteractiveSearch gameId={1} />
            </MemoryRouter>
        </LocaleProvider>,
    );
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Releases suchen' }));
    expect(await screen.findByText('Rejected beta')).toBeVisible();
    expect(screen.getByTitle('Beta abgelehnt')).toBeVisible();
    const buttons = screen.getAllByRole('button', { name: 'Laden' });
    expect(buttons[0]).toBeDisabled();
    expect(buttons[1]).toBeEnabled();
    await user.click(buttons[0]);
    expect(fetch.mock.calls.filter(([url]) => url.endsWith('/grab'))).toHaveLength(0);
    await user.click(buttons[1]);
    await waitFor(() => expect(fetch.mock.calls.filter(([url]) => url.endsWith('/grab'))).toHaveLength(1));
});
it('edits a stored secret only through an explicit replace or clear action', async () => {
    let last: Record<string, unknown> = {};
    function Harness() {
        const [values, setValues] = useState({ clientSecretConfigured: true } as Record<string, unknown>);
        last = payload(providerFields, values);
        return (
            <Fields
                fields={providerFields.filter((field) => field.key === 'clientSecret')}
                values={values}
                onChange={setValues}
            />
        );
    }
    render(
        <LocaleProvider>
            <Harness />
        </LocaleProvider>,
    );
    const user = userEvent.setup();
    expect(last.clientSecret).toBeNull();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    await user.selectOptions(screen.getByRole('combobox'), 'replace');
    await user.type(
        screen.getByLabelText('Client-Secret / Entwickler-Passwort', { selector: 'input' }),
        'new-secret',
    );
    expect(last.clientSecret).toBe('new-secret');
    await user.selectOptions(screen.getByRole('combobox'), 'keep');
    expect(last.clientSecret).toBeNull();
    await user.selectOptions(screen.getByRole('combobox'), 'clear');
    expect(last.clientSecret).toBe('');
});
it('never displays an old search response after the search parameters change', async () => {
    let finishOld!: (response: Response) => void;
    vi.stubGlobal(
        'fetch',
        vi.fn((url: string) =>
            url.endsWith('old')
                ? new Promise<Response>((resolve) => {
                      finishOld = resolve;
                  })
                : Promise.resolve(json({ name: 'new result' })),
        ),
    );
    const { result, rerender } = renderHook(({ path }) => useResource<{ name: string }>(path), {
        initialProps: { path: 'old' },
    });
    rerender({ path: 'new' });
    await waitFor(() => expect(result.current.data?.name).toBe('new result'));
    await act(async () => finishOld(json({ name: 'stale result' })));
    expect(result.current.data?.name).toBe('new result');
});
