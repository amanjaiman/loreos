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

export interface RailSummary {
  /** Memories awaiting the user's judgment — the rail's only badge. */
  staged: number;
  /** Statements of the last few memories Lore kept, newest first. */
  recent: string[];
}

/**
 * The small, rail-scoped read behind the staged badge and the ambient trace (v2-004).
 * Kept separate from the Today view's fetch so the rail stays live on every route.
 */
export function useRailSummary(pollMs = 20000): RailSummary {
  const [summary, setSummary] = useState<RailSummary>({
    staged: 0,
    recent: [],
  });

  useEffect(() => {
    let alive = true;
    const tick = async (): Promise<void> => {
      try {
        // The badge reads the counts endpoint rather than `listMemories(...).items.length`,
        // which silently under-reported past its page size and pulled whole rows to render
        // one integer.
        const [stats, decisions] = await Promise.all([
          api.memoryStats(),
          api.decisions(40),
        ]);
        if (!alive) {
          return;
        }
        const recent = decisions.items
          .filter(
            (d) =>
              d.action === 'promoted' ||
              d.action === 'revised' ||
              d.action === 'user_promoted',
          )
          .map((d) => d.statement)
          .filter((s) => typeof s === 'string' && s.length > 0)
          .slice(0, 3);
        setSummary({ staged: stats.staged, recent });
      } catch {
        // Offline is a normal state here: leave the last good summary in place rather
        // than blanking the rail while the agent restarts.
      }
    };
    void tick();
    const id = window.setInterval(() => void tick(), pollMs);
    return () => {
      alive = false;
      window.clearInterval(id);
    };
  }, [pollMs]);

  return summary;
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
