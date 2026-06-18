import { useCallback, useEffect, useState } from 'react';

import {
  api,
  LoreOfflineError,
  type LoreConfigShape,
  type SystemStatus,
} from '../api';

// Small read hooks over api.ts (the only seam). They poll/refresh status and config so
// the chrome stays live; views hold no business logic, just render what these return.

export interface SystemStatusState {
  status: SystemStatus | null;
  /** Client-measured round-trip of the status call — the honest local latency. */
  latencyMs: number | null;
  offline: boolean;
  loading: boolean;
}

/** Polls GET /system/status and times the round-trip. Offline when Lore isn't running. */
export function useSystemStatus(pollMs = 5000): SystemStatusState {
  const [state, setState] = useState<SystemStatusState>({
    status: null,
    latencyMs: null,
    offline: false,
    loading: true,
  });

  useEffect(() => {
    let alive = true;
    const tick = async (): Promise<void> => {
      const started = performance.now();
      try {
        const status = await api.systemStatus();
        const latencyMs = Math.round(performance.now() - started);
        if (alive) {
          setState({ status, latencyMs, offline: false, loading: false });
        }
      } catch (e) {
        if (alive) {
          setState((s) => ({
            ...s,
            offline: e instanceof LoreOfflineError,
            loading: false,
          }));
        }
      }
    };
    void tick();
    const id = window.setInterval(() => void tick(), pollMs);
    return () => {
      alive = false;
      window.clearInterval(id);
    };
  }, [pollMs]);

  return state;
}

export interface ConfigState {
  config: LoreConfigShape | null;
  offline: boolean;
  loading: boolean;
  refresh: () => Promise<void>;
}

/** Reads GET /config once (with a manual refresh). */
export function useConfig(): ConfigState {
  const [config, setConfig] = useState<LoreConfigShape | null>(null);
  const [offline, setOffline] = useState(false);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async (): Promise<void> => {
    setLoading(true);
    try {
      setConfig(await api.getConfig());
      setOffline(false);
    } catch (e) {
      if (e instanceof LoreOfflineError) {
        setOffline(true);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return { config, offline, loading, refresh };
}
