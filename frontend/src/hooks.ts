import { useEffect, useState } from 'react';
import { api } from './api';
export function useResource<T>(path: string | null, pollMs = 0) {
  const [state, setState] = useState<{ path: string | null; data?: T; error?: unknown; loading: boolean }>({
    path,
    loading: !!path,
  });
  const [revision, setRevision] = useState(0);
  const reload = () => setRevision((value) => value + 1);
  useEffect(() => {
    if (!path) {
      setState({ path, loading: false });
      return;
    }
    let stopped = false;
    let running = false;
    let queued = false;
    let controller: AbortController | undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;
    async function refresh() {
      if (stopped) return;
      if (running) {
        queued = true;
        return;
      }
      running = true;
      controller = new AbortController();
      setState((previous) => ({
        ...previous,
        path,
        data: previous.path === path ? previous.data : undefined,
        loading: true,
        error: undefined,
      }));
      try {
        const data = await api<T>(path!, { signal: controller.signal });
        if (!stopped) setState({ path, data, loading: false });
      } catch (error) {
        if (!stopped && !controller.signal.aborted)
          setState((previous) => ({ ...previous, error, loading: false }));
      } finally {
        running = false;
        if (queued && !stopped) {
          queued = false;
          void refresh();
        }
      }
    }
    const onRefresh = () => {
      clearTimeout(timer);
      timer = setTimeout(() => void refresh(), 250);
    };
    void refresh();
    window.addEventListener('ludus:refresh', onRefresh);
    const poll = pollMs
      ? setInterval(() => {
          if (!document.hidden) void refresh();
        }, pollMs)
      : undefined;
    return () => {
      stopped = true;
      controller?.abort();
      clearTimeout(timer);
      clearInterval(poll);
      window.removeEventListener('ludus:refresh', onRefresh);
    };
  }, [path, pollMs, revision]);
  return {
    ...state,
    data: state.path === path ? state.data : undefined,
    loading: state.path !== path ? !!path : state.loading,
    reload,
  };
}
export function useDebounce<T>(value: T, delay = 350) {
  const [result, setResult] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setResult(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return result;
}
