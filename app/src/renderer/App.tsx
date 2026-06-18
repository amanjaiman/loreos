import { useCallback, useEffect, useState } from 'react';

import { api, LoreOfflineError } from './api';
import { AppShell } from './chrome/AppShell';
import { OfflineNotice } from './chrome/OfflineNotice';
import { Spinner } from './design-system';
import { RouterProvider } from './lib/router';
import { Onboarding } from './views/onboarding/Onboarding';

type Phase = 'loading' | 'offline' | 'onboarding' | 'ready';

/**
 * App root. Decides what to show on launch by reading config through api.ts: the calm
 * offline state if Lore isn't running, first-run onboarding until it's complete, else the
 * shell. Onboarding gating lives here so onboarding owns the whole window when shown.
 */
export function App(): JSX.Element {
  const [phase, setPhase] = useState<Phase>('loading');

  const load = useCallback(async (): Promise<void> => {
    setPhase('loading');
    try {
      const config = await api.getConfig();
      setPhase(config.onboarding?.completed === true ? 'ready' : 'onboarding');
    } catch (e) {
      // Can't reach Lore → offline. Any other error → treat as not-yet-onboarded.
      setPhase(e instanceof LoreOfflineError ? 'offline' : 'onboarding');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

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
    <RouterProvider initial="home">
      <AppShell />
    </RouterProvider>
  );
}
