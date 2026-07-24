import { useCallback, useEffect, useState } from 'react';

import { api, LoreOfflineError } from './api';
import { AppShell } from './chrome/AppShell';
import { OfflineNotice } from './chrome/OfflineNotice';
import { Spinner } from './design-system';
import { RouterProvider } from './lib/router';
import { Onboarding } from './views/onboarding/Onboarding';

type Phase = 'loading' | 'offline' | 'onboarding' | 'ready';

// How often to quietly re-check while offline. Agent startup (memoryd cold start,
// the health gate, provider configuration) routinely takes 10-30s and commonly
// finishes after the window has already loaded — without this, a single
// unlucky-timing check latches the app into "offline" forever with no way out
// but a manual click, even once the agent is fully healthy.
const OFFLINE_RETRY_MS = 2000;

/**
 * App root. Decides what to show on launch by reading config through api.ts: the calm
 * offline state if Lore isn't running, first-run onboarding until it's complete, else the
 * shell. Onboarding gating lives here so onboarding owns the whole window when shown.
 */
export function App(): JSX.Element {
  const [phase, setPhase] = useState<Phase>('loading');

  const settle = useCallback(async (): Promise<Phase> => {
    try {
      const config = await api.getConfig();
      return config.onboarding?.completed === true ? 'ready' : 'onboarding';
    } catch (e) {
      // Can't reach Lore → offline. Any other error → treat as not-yet-onboarded.
      return e instanceof LoreOfflineError ? 'offline' : 'onboarding';
    }
  }, []);

  // The initial check and the manual "Try again" button both show the loading
  // spinner while in flight — an explicit, user-initiated recheck.
  const load = useCallback((): void => {
    setPhase('loading');
    void settle().then(setPhase);
  }, [settle]);

  useEffect(() => {
    void load();
  }, [load]);

  // While offline, keep quietly rechecking in the background so the app recovers
  // on its own once the agent finishes starting — no flash back to the loading
  // spinner, and no need to click "Try again".
  useEffect(() => {
    if (phase !== 'offline') {
      return undefined;
    }

    const id = window.setInterval(() => {
      void settle().then((next) => {
        if (next !== 'offline') {
          setPhase(next);
        }
      });
    }, OFFLINE_RETRY_MS);
    return () => window.clearInterval(id);
  }, [phase, settle]);

  if (phase === 'loading') {
    return (
      <div className="app-splash">
        <Spinner size={28} />
      </div>
    );
  }
  if (phase === 'offline') {
    return <OfflineNotice onRetry={load} />;
  }
  if (phase === 'onboarding') {
    return <Onboarding onComplete={() => setPhase('ready')} />;
  }
  return (
    <RouterProvider initial="today">
      <AppShell />
    </RouterProvider>
  );
}
