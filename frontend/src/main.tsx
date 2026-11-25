import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { LocaleProvider } from './i18n';
import { ToastProvider } from './ui';
import { App } from './App';
import './styles.css';

createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <LocaleProvider>
            <ToastProvider>
                <BrowserRouter>
                    <App />
                </BrowserRouter>
            </ToastProvider>
        </LocaleProvider>
    </StrictMode>,
);
