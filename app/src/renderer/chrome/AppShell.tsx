import { useCallback, useState } from 'react';

import { api } from '../api';
import { useConfig, useRailSummary, useSystemStatus } from '../lib/hooks';
import { useRouter } from '../lib/router';
import { useTitleBarSync } from '../lib/useTitleBarSync';
import { Activity } from '../views/Activity';
import { Memory } from '../views/Memory';
import { Settings } from '../views/settings/Settings';
import { Today } from '../views/Today';
import { type AmbientStatus } from './LiveElement';
import { Rail } from './Rail';

const VIEWS = {
  today: Today,
  memory: Memory,
  activity: Activity,
  settings: Settings,
} as const;

/**
 * The v2-004 shell: a frameless window whose background is one continuous ground plane,
 * with the rail sitting on it and the content floating above it in an inset card.
 *
 *   ┌ strip ─────────────────────────┐  44px of ground: the drag surface, and where
 *   │ rail │ ┌ card ───────────────┐ │  the native window controls live. Keeping it
 *   │      │ │                     │ │  ground (rather than bleeding the card to the
 *   │      │ └─────────────────────┘ │  top) is what guarantees the controls never
 *   └──────┴─────────────────────────┘  collide with content.
 *
 * The renderer still holds no business logic; the shell only routes and relays.
 */
export function AppShell(): JSX.Element {
  const { route } = useRouter();
  const View = VIEWS[route];

  useTitleBarSync();

  const { offline } = useSystemStatus();
  const { config, refresh } = useConfig();
  const { staged, recent } = useRailSummary();
  const [busy, setBusy] = useState(false);

  const paused = config?.capture?.enabled === false;
  const status: AmbientStatus = offline
    ? 'offline'
    : paused
      ? 'paused'
      : 'listening';

  // Pause/resume is a user-authority write, so it goes through the same config patch
  // path as Settings — the rail is a shortcut to it, not a second source of truth.
  const toggleCapture = useCallback(async (): Promise<void> => {
    setBusy(true);
    try {
      await api.patchConfig({ capture: { enabled: paused } });
      await refresh();
    } catch {
      // Offline or rejected: the next config poll re-reports the real state.
    } finally {
      setBusy(false);
    }
  }, [paused, refresh]);

  return (
    <div className="app-ground">
      <div className="app-strip" />
      <Rail
        status={status}
        staged={staged}
        recent={recent}
        onToggleCapture={() => void toggleCapture()}
        busy={busy}
      />
      <main className="app-card">
        <div className="app-card__scroll">
          <View />
        </div>
      </main>
    </div>
  );
}
