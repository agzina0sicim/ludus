import { createContext, useContext, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

const ToolbarTarget = createContext<HTMLElement | null>(null);

export function SettingsLayout({ title, children }: { title: string; children: ReactNode }) {
    const [target, setTarget] = useState<HTMLDivElement | null>(null);
    return (
        <ToolbarTarget.Provider value={target}>
            <header className="view-toolbar settings-toolbar">
                <h1>{title}</h1>
                <div className="toolbar-actions" ref={setTarget} />
            </header>
            {children}
        </ToolbarTarget.Provider>
    );
}

export function SettingsActions({ children }: { children: ReactNode }) {
    const target = useContext(ToolbarTarget);
    return target ? createPortal(children, target) : null;
}
