import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useEffect, useState } from 'react';
import { api, csrfToken } from './api';
import type { Session } from './types';

export function useRealtime() {
  const [event, setEvent] = useState<string>();
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const reconnect = () => setRevision((value) => value + 1);
    window.addEventListener('ludus:reconnect', reconnect);
    return () => window.removeEventListener('ludus:reconnect', reconnect);
  }, []);
  useEffect(() => {
    let disposed = false;
    let retry: ReturnType<typeof setTimeout>;
    let noticeTimer: ReturnType<typeof setTimeout>;
    let connection: HubConnection | undefined;
    async function start() {
      try {
        const session = await api<Session>('auth/session');
        if (disposed) return;
        if (!session.authenticated) {
          window.dispatchEvent(new Event('ludus:unauthorized'));
          return;
        }
        if (!connection) {
          const token = await csrfToken();
          if (disposed) return;
          connection = new HubConnectionBuilder()
            .withUrl('/hubs/activity', { withCredentials: true, headers: { 'X-CSRF-Token': token } })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(LogLevel.None)
            .build();
          [
            'queueUpdated',
            'importStatus',
            'rssUpdated',
            'taskUpdated',
            'healthWarning',
            'healthUpdated',
            'scanUpdated',
          ].forEach((name) =>
            connection!.on(name, () => {
              if (disposed) return;
              setEvent(name);
              clearTimeout(noticeTimer);
              noticeTimer = setTimeout(() => setEvent(undefined), 8000);
              window.dispatchEvent(new Event('ludus:refresh'));
            }),
          );
          connection.onreconnected(() => {
            if (!disposed) window.dispatchEvent(new Event('ludus:refresh'));
          });
          connection.onclose(() => {
            if (!disposed) retry = setTimeout(() => void start(), 5000);
          });
        }
        await connection.start();
        if (disposed) await connection.stop();
      } catch {
        if (!disposed) retry = setTimeout(() => void start(), 10000);
      }
    }
    void start();
    return () => {
      disposed = true;
      clearTimeout(retry);
      clearTimeout(noticeTimer);
      void connection?.stop();
    };
  }, [revision]);
  return event;
}
