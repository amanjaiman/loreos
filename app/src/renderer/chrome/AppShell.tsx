import { useCallback, useRef, useState, type CSSProperties } from 'react';

import { api } from '../api';
import { useConfig, useRailSummary, useSystemStatus } from '../lib/hooks';
import { useRouter, type Route } from '../lib/router';
import { Activity } from '../views/Activity';
import { Memory } from '../views/Memory';
import { Settings } from '../views/settings/Settings';
import { Today } from '../views/Today';
import { type AmbientStatus } from './LiveElement';
import { Rail } from './Rail';
import { UpdateBanner } from './UpdateBanner';
import { WindowControls } from './WindowControls';

const VIEWS = {
  today: Today,
  memory: Memory,
  activity: Activity,
  settings: Settings,
} as const;

/** Rail order, so a route change knows which way the user travelled. */
const ORDER: Route[] = ['today', 'memory', 'activity', 'settings'];

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

  const { status: system, offline } = useSystemStatus();
  const { config, refresh } = useConfig();
  const { staged, recent } = useRailSummary();

  // What Lore is watching, straight off /system/status (v2-005 R2). The agent nulls this
  // for excluded or blocked windows, so the renderer never has to decide what is safe to
  // show. `enabled` is intentionally NOT read from here — config is the write path, so
  // reading it back from config keeps the pause button responsive instead of waiting on
  // the next status poll.
  const watching = system?.capture?.window_title ?? null;
  const [busy, setBusy] = useState(false);
  const [scrolled, setScrolled] = useState(false);

  // Travel direction through the rail: moving down enters from below, up from above.
  // Keyed off the previous route rather than history, since the router has none.
  const previous = useRef<Route>(route);
  const descending = ORDER.indexOf(route) >= ORDER.indexOf(previous.current);
  previous.current = route;

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
      <div className="app-strip">
        <WindowControls />
      </div>
      <Rail
        status={status}
        staged={staged}
        recent={recent}
        watching={watching}
        onToggleCapture={() => void toggleCapture()}
        busy={busy}
      />
      <main className="app-card" data-scrolled={scrolled ? 'true' : 'false'}>
        <div
          className="app-card__scroll"
          style={{ '--dir': descending ? '12px' : '-12px' } as CSSProperties}
          // Drives the page header's condense-on-scroll. Read off the event target
          // rather than a ref so it survives the keyed remount below.
          // Any scroll at all condenses the header. The old 28px threshold existed to
          // avoid twitching, but it meant pages with only a little overflow never
          // condensed at all — and because the header is now out of flow, there is no
          // scroll-height feedback to guard against. It also closes the window where a
          // still-transparent expanded header would have content sliding under it.
          onScroll={(event) => setScrolled(event.currentTarget.scrollTop > 0)}
        >
          {/* Keyed on the route so the entrance animation replays on every change. */}
          <View key={route} />
        </div>
      </main>
      {/* Sits above the ground plane; only renders once an update is downloaded and ready. */}
      <UpdateBanner />
    </div>
  );
}
