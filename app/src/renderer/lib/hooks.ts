import { useCallback, useEffect, useState } from 'react';

import {
  api,
  LoreOfflineError,
  type ActivityEntry,
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
  /** Title of the most recent window Lore actually captured, or null. */
  watching: string | null;
}

/**
 * The newest window title Lore *captured*.
 *
 * Only `Captured` rows qualify. `Filtered` and `Skipped` mean the blocklist or a privacy
 * filter excluded that window — echoing its title into the always-visible rail would leak
 * precisely what the blocklist exists to protect (constitution §1). When the newest row is
 * excluded we surface nothing and the live element falls back to generic state copy.
 */
function watchedTitle(items: ActivityEntry[]): string | null {
  const newest = items[0];
  if (newest === undefined || newest.decision !== 'Captured') {
    return null;
  }
  const title = newest.window_title.trim();
  return title.length > 0 ? title : null;
}

/**
 * The small, rail-scoped read behind the staged badge and the ambient trace (v2-004).
 * Kept separate from the Today view's fetch so the rail stays live on every route.
 */
export function useRailSummary(pollMs = 8000): RailSummary {
  const [summary, setSummary] = useState<RailSummary>({
    staged: 0,
    recent: [],
    watching: null,
  });

  useEffect(() => {
    let alive = true;
    const tick = async (): Promise<void> => {
      try {
        const [staged, decisions, activity] = await Promise.all([
          api.listMemories({ status: 'staged', limit: 50 }),
          api.decisions(40),
          api.activity(5),
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
        setSummary({
          staged: staged.items.length,
          recent,
          watching: watchedTitle(activity.items),
        });
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
