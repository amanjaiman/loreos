import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
} from 'react';

import { api } from '../api';
import { useConfig, useRailSummary, useSystemStatus } from '../lib/hooks';
import { useRouter, type Route } from '../lib/router';
import { useAgentControl } from '../lib/useAgentControl';
import { Activity } from '../views/Activity';
import { Memory } from '../views/Memory';
import { Settings } from '../views/settings/Settings';
import { Today } from '../views/Today';
import { type AmbientStatus } from './LiveElement';
import { Rail } from './Rail';
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

  // Stopped is a state the user can enter from the tray but, without this, could only
  // leave from the tray. The rail offers the way back (v2-006).
  const agentControl = useAgentControl(offline);

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
      // Nudge the tray so its icon changes with the click rather than on its next poll
      // (v2-006 R4). It re-reads the agent itself; this only says "look again".
      window.lore?.notifyLifecycleChanged?.();
    } catch {
      // Offline or rejected: the next config poll re-reports the real state.
    } finally {
      setBusy(false);
    }
  }, [paused, refresh]);

  // The same switch can be thrown from the tray menu while the window is open. Re-read
  // config when the main process says the state moved, so the rail doesn't sit stale for
  // up to a poll interval showing the opposite of what the tray shows (v2-006 R4 AC 2).
  useEffect(() => {
    const subscribe = window.lore?.onLifecycleState;
    return typeof subscribe === 'function'
      ? subscribe(() => void refresh())
      : undefined;
  }, [refresh]);

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
        onStart={agentControl.canStart ? agentControl.start : undefined}
        starting={agentControl.starting}
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
    </div>
  );
}
