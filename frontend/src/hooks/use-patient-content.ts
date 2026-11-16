'use client';

import { useEffect, useState } from 'react';
import { accessDenied } from '@/lib/patient-sharing-api';

// Clinical payloads live only in the mounted view, never in a persistent cache.
export function usePatientContent<T>(load: (signal: AbortSignal) => Promise<T>) {
  const [nonce, setNonce] = useState(0);
  const [denied, setDenied] = useState(false);
  const [state, setState] = useState<{
    data: T | null;
    error: unknown;
    generation: number;
    loader: typeof load | null;
  }>({ data: null, error: null, generation: -1, loader: null });
  useEffect(() => {
    if (denied) return;
    let stopped = false;
    let request: AbortController | null = null;
    async function read() {
      if (request) return;
      const controller = new AbortController();
      request = controller;
      try {
        const data = await load(controller.signal);
        if (!stopped) setState({ data, error: null, generation: nonce, loader: load });
      } catch (error) {
        if (!stopped && !controller.signal.aborted) {
          setState({ data: null, error, generation: nonce, loader: load });
          if (accessDenied(error)) setDenied(true);
        }
      } finally {
        if (request === controller) request = null;
      }
    }
    void read();
    const timer = window.setInterval(() => {
      if (!document.hidden) void read();
    }, 30000);
    const focus = () => {
      void read();
    };
    const visible = () => {
      if (!document.hidden) void read();
    };
    window.addEventListener('focus', focus);
    document.addEventListener('visibilitychange', visible);
    return () => {
      stopped = true;
      request?.abort();
      window.clearInterval(timer);
      window.removeEventListener('focus', focus);
      document.removeEventListener('visibilitychange', visible);
    };
  }, [load, nonce, denied]);
  const reload = () => {
    setDenied(false);
    setNonce((old) => old + 1);
  };
  const loseAccess = (error?: unknown) => {
    setDenied(true);
    setState({
      data: null,
      error: error ?? { isAxiosError: true, response: { status: 404 } },
      generation: nonce,
      loader: load,
    });
  };
  const current = state.generation === nonce && state.loader === load;
  return {
    data: denied || !current ? null : state.data,
    error: current ? state.error : null,
    loading: !denied && !current,
    reload,
    loseAccess,
  };
}
