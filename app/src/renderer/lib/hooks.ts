import { useCallback, useEffect, useRef, useState } from 'react';

import {
  api,
  LoreOfflineError,
  MEMORY_CHANGED,
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
    // Re-read immediately when the user acts on a memory, so the badge never disagrees
    // with the screen that just changed.
    const onChange = (): void => void tick();
    window.addEventListener(MEMORY_CHANGED, onChange);
    return () => {
      alive = false;
      window.clearInterval(id);
      window.removeEventListener(MEMORY_CHANGED, onChange);
    };
  }, [pollMs]);

  return summary;
}

/**
 * Keeps a view's own read live, the way `useSystemStatus` and `useRailSummary` keep the
 * chrome live.
 *
 * Every content view used to fetch exactly once on mount, so the only thing that
 * refreshed a page was leaving it and coming back — `AppShell` keys the view on the
 * route, so navigating remounts it and re-runs that one-shot effect. The rail's staged
 * badge polled underneath a page that didn't, which is the inverse of what you want.
 *
 * `load` is called once immediately, then on every tick. Its identity is the effect's
 * dependency: a view whose read depends on state (Memory's query) should wrap it in a
 * `useCallback` over that state, and changing it re-reads at once rather than waiting
 * for the next tick.
 *
 * Deliberately NOT listening for `MEMORY_CHANGED`: every view that mutates a memory
 * already refreshes itself afterwards, on its own timing — Home waits out the row's
 * collapse animation first, and an event-driven refetch would cut that short. The event
 * exists for the rail, which is mounted alongside the page and has no other way to know.
 */
export function usePolledData(
  load: () => Promise<void>,
  pollMs: number,
  /** Hold the interval — for a view with an editor open over the data it would replace. */
  paused = false,
): { refresh: () => void } {
  const pausedRef = useRef(paused);
  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  // Lets `refresh` reach the live effect's guarded runner without re-subscribing.
  const runRef = useRef<() => void>(() => undefined);

  useEffect(() => {
    let inFlight = false;
    let queued = false;
    let disposed = false;
    /**
     * Reads never overlap. The agent is one local process, and two in-flight reads
     * finish in whichever order they finish — the page would render the loser.
     *
     * A tick that lands on a slow read is simply dropped; the next one is 15s away.
     * A `refresh` is a user action ("I just kept this memory"), so it can't be dropped
     * — it waits its turn and runs after, which also guarantees it reads state from
     * after the mutation rather than racing the poll that preceded it.
     */
    const run = (force: boolean): void => {
      if (inFlight) {
        queued = queued || force;
        return;
      }
      inFlight = true;
      void load().finally(() => {
        inFlight = false;
        // Not after the view moved on: this closure's `load` is the superseded one.
        if (queued && !disposed) {
          queued = false;
          run(false);
        }
      });
    };
    runRef.current = () => run(true);
    run(false);

    const visible = (): boolean => document.visibilityState === 'visible';
    const id = window.setInterval(() => {
      // Sitting in the tray is Lore's normal resting state, so an unpaused interval
      // would spend the agent's CPU on a window nobody is looking at.
      if (visible() && !pausedRef.current) {
        run(false);
      }
    }, pollMs);

    // Returning to the window is where staleness is most visible — don't make the user
    // sit out the remainder of an interval that was suspended while they were away.
    const onVisibility = (): void => {
      if (visible()) {
        run(false);
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      disposed = true;
      window.clearInterval(id);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [load, pollMs]);

  return { refresh: useCallback(() => runRef.current(), []) };
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
