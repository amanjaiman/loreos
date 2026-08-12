import { useCallback, useEffect, useState } from 'react';

/**
 * How long to keep showing "Starting…" before giving up on it.
 *
 * A cold start spawns the agent, which spawns memoryd and waits for it to become healthy —
 * seconds, not milliseconds. But if it never comes up (a crash loop, a port already taken),
 * the button must stop claiming progress and let the user try again rather than spinning
 * forever.
 */
const START_TIMEOUT_MS = 30_000;

export interface AgentControl {
  /** Whether a Start control should be offered at all. False in a dev build. */
  canStart: boolean;
  /** A start has been requested and the agent hasn't answered yet. */
  starting: boolean;
  start: () => void;
}

/**
 * The app's own Start control for a stopped agent (spec v2-006).
 *
 * Every other control in the app writes through `api.ts`, but this one cannot: "stopped"
 * means nothing is listening on :7842, so the only route is the preload bridge to the main
 * process, which owns the agent's process handle. Same verb as the tray's Start item.
 *
 * @param offline whether the caller currently sees the API as unreachable — the signal
 *                that tells us the start succeeded.
 */
export function useAgentControl(offline: boolean): AgentControl {
  const [canStart, setCanStart] = useState(false);
  const [starting, setStarting] = useState(false);

  useEffect(() => {
    const probe = window.lore?.canStartLore;
    if (typeof probe !== 'function') {
      return; // plain-browser render, or a build without the bridge
    }
    void probe().then(setCanStart);
  }, []);

  useEffect(() => {
    if (!starting) {
      return undefined;
    }
    // The agent answered: it's up, and the rest of the app's polling takes over from here.
    if (!offline) {
      setStarting(false);
      return undefined;
    }
    const timer = window.setTimeout(() => setStarting(false), START_TIMEOUT_MS);
    return () => window.clearTimeout(timer);
  }, [starting, offline]);

  const start = useCallback((): void => {
    if (typeof window.lore?.startLore !== 'function') {
      return;
    }
    setStarting(true);
    window.lore.startLore();
  }, []);

  return { canStart, starting, start };
}
