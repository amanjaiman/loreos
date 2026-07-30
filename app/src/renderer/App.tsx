import { useCallback, useEffect, useState } from 'react';

import { api, LoreOfflineError } from './api';
import { AppShell } from './chrome/AppShell';
import { Spinner } from './design-system';
import { RouterProvider } from './lib/router';
import { Onboarding } from './views/onboarding/Onboarding';

type Gate = 'loading' | 'onboarding' | 'shell';

// How often to re-probe config while we don't yet have a config-backed answer.
// Agent startup (memoryd cold start, the health gate, provider configuration)
// routinely takes 10-30s and commonly finishes after the window has loaded.
const PROBE_RETRY_MS = 2000;

/**
 * App root. Decides what to show on launch by reading config through api.ts.
 *
 * The agent being down does NOT block the app: onboarding aside, we always render the
 * shell, which is fully offline-resilient (the header shows a "Lore isn't running" badge
 * and every view degrades to a calm inline notice). The user can still browse captures,
 * settings, and the rest while the agent starts — rather than being stranded on a
 * full-window error. Onboarding gating lives here so onboarding owns the whole window
 * when shown; a first-run user who launched before the agent was up still lands in
 * onboarding once it responds, because we keep probing until the decision is config-backed.
 */
export function App(): JSX.Element {
  const [gate, setGate] = useState<Gate>('loading');
  // True once we've made a definitive, config-backed decision. Until then we keep
  // probing so a first-run user who launched while the agent was still starting is
  // routed to onboarding once it responds, instead of being left in the shell with
  // an unconfigured provider.
  const [confirmed, setConfirmed] = useState(false);

  const probe = useCallback(async (): Promise<void> => {
    try {
      const config = await api.getConfig();
      setConfirmed(true);
      setGate(config.onboarding?.completed === true ? 'shell' : 'onboarding');
    } catch (e) {
      if (e instanceof LoreOfflineError) {
        // Can't read onboarding state. Show the shell (offline-resilient) rather than
        // block the whole app, and keep probing — the decision stays provisional.
        setGate((prev) => (prev === 'loading' ? 'shell' : prev));
      } else {
        // Any other error → treat as not-yet-onboarded and stop probing.
        setConfirmed(true);
        setGate('onboarding');
      }
    }
  }, []);

  useEffect(() => {
    void probe();
  }, [probe]);

  // Keep probing in the background until we have a config-backed answer, then stop —
  // once the shell is up its own hooks track live offline status from there.
  useEffect(() => {
    if (confirmed) {
      return undefined;
    }
    const id = window.setInterval(() => void probe(), PROBE_RETRY_MS);
    return () => window.clearInterval(id);
  }, [confirmed, probe]);

  if (gate === 'loading') {
    return (
      <div className="app-splash">
        <Spinner size={28} />
      </div>
    );
  }
  if (gate === 'onboarding') {
    return <Onboarding onComplete={() => setGate('shell')} />;
  }
  return (
    <RouterProvider initial="today">
      <AppShell />
    </RouterProvider>
  );
}
