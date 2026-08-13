// LoreLifecycle.ts — Lore as a background app: the tray, the state machine behind it, and
// the window's show/hide/quit verbs (spec v2-006 R1, R3, R4).
//
// Before this, the window *was* the process: closing it quit the app, which killed the
// agent, which killed memoryd — so an ambient capture agent stopped capturing the moment
// you closed its window. Now the window is just a view onto a process that outlives it,
// and the tray is the control surface for the process.
//
// One object owns all of it. State is derived here and nowhere else; the tray renders that
// state, the menu acts on it, and the renderer is told when it changes. Nothing else in
// the main process decides whether Lore is running.

import { app, BrowserWindow, Menu, nativeImage, Tray } from 'electron';
import type { NativeImage } from 'electron';

import { AgentProcess } from '../agentProcess';
import { readStatus, setCaptureEnabled } from './agentStatus';
import { getFlag, setFlag } from './prefs';
import { TRAY_ICONS, TRAY_ICON_SIZES } from './trayIcons';

/**
 * The three states, matching the rail's `AmbientStatus` exactly (`listening` there is
 * `running` here). Both are visible at once, so they must never disagree.
 */
export type LoreState = 'running' | 'paused' | 'stopped';

/** What the tray needs from whoever owns the window. */
export interface LifecycleHost {
  getWindow(): BrowserWindow | null;
  createWindow(): void;
  /** Apply the downloaded update and restart. Wired to the updater (autoUpdate.ts). */
  installUpdate(): void;
}

/**
 * The whole state rule, as a pure function.
 *
 * "Stopped" is derived from *reachability*, not from whether we spawned a child: an agent
 * killed in Task Manager, one that crashed, and one that was never ours (a dev build's
 * hand-run agent) are all correctly Stopped by the same rule.
 *
 * Exported and pure because it is the only branching logic in this module, and the app has
 * no test harness yet (plan → Testing) — this is the part worth testing when one lands.
 */
export function deriveState(snapshot: {
  reachable: boolean;
  captureEnabled: boolean;
}): LoreState {
  if (!snapshot.reachable) {
    return 'stopped';
  }
  return snapshot.captureEnabled ? 'running' : 'paused';
}

/** Matches the rail's own poll (`useSystemStatus(5000)`), so the two can't drift far. */
const POLL_MS = 5000;

/**
 * After an action, poll harder for a while instead of waiting out the interval. Stop is
 * near-instant; a start has to spawn the agent and wait for it to bind :7842, which is
 * seconds, so the burst runs out to ~15s before falling back to the steady poll.
 */
const NUDGE_MS = [300, 1000, 2500, 5000, 9000, 15000];

const TOOLTIPS: Record<LoreState, string> = {
  running: 'Lore — capturing',
  paused: 'Lore — paused',
  stopped: 'Lore — stopped',
};

const MENU_HEADERS: Record<LoreState, string> = {
  running: 'Lore — capturing',
  paused: 'Lore — paused',
  stopped: 'Lore — stopped',
};

/** Build the multi-resolution tray image for a state, so Windows picks the right DPI. */
function trayImage(state: LoreState): NativeImage {
  const sizes = TRAY_ICON_SIZES;
  const buffer = (size: number): Buffer =>
    Buffer.from(TRAY_ICONS[state][size], 'base64');
  const image = nativeImage.createFromBuffer(buffer(sizes[0]));
  // The base is 16px @1x; the rest are the same art at the scale factors Windows uses
  // for 125% / 150% / 200% display scaling.
  for (const size of sizes.slice(1)) {
    image.addRepresentation({
      scaleFactor: size / sizes[0],
      buffer: buffer(size),
    });
  }
  return image;
}

export class LoreLifecycle {
  private tray: Tray | null = null;
  private state: LoreState = 'stopped';
  /** Version of a downloaded, ready-to-install update, or null. Drives the tray's update row. */
  private updateVersion: string | null = null;
  /** Set on the first real quit; until then, closing the window only hides it. */
  private quitting = false;
  private pollTimer: NodeJS.Timeout | null = null;
  private nudgeTimers = new Set<NodeJS.Timeout>();
  /** Guards against overlapping polls when the agent is slow to answer. */
  private reading = false;

  constructor(
    private readonly agent: AgentProcess,
    private readonly host: LifecycleHost,
  ) {}

  /** Create the tray and begin tracking state. Call once, after `app.on('ready')`. */
  start(): void {
    this.createTray();
    void this.refresh();
    this.pollTimer = setInterval(() => void this.refresh(), POLL_MS);
  }

  /**
   * Whether a tray actually exists. If tray creation failed, the app must fall back to
   * quitting when its last window closes — otherwise it would keep running with no window
   * and no icon, i.e. be unkillable except through Task Manager.
   */
  hasTray(): boolean {
    return this.tray !== null;
  }

  /** True once a real quit is under way, so `close` handlers stop intercepting. */
  isQuitting(): boolean {
    return this.quitting;
  }

  /** Latch the quit. Wired to `before-quit`, so it covers the updater's quit too. */
  markQuitting(): void {
    this.quitting = true;
  }

  /**
   * Make the window's close button hide instead of destroy (R1). Hiding keeps the route,
   * scroll position and React state, so reopening from the tray is instant rather than a
   * cold boot of the renderer.
   */
  attachWindow(window: BrowserWindow): void {
    window.on('close', (event) => {
      if (this.quitting) {
        return; // a real quit — let it through
      }
      event.preventDefault();
      window.hide();
      this.announceFirstHide();
    });
  }

  /** Show and focus the window, recreating it if it was destroyed. */
  showWindow(): void {
    const window = this.host.getWindow();
    if (!window || window.isDestroyed()) {
      this.host.createWindow();
      return;
    }
    if (window.isMinimized()) {
      window.restore();
    }
    window.show();
    window.focus();
  }

  /** Stop the agent and exit. The tray's Quit item, and the only intended way out. */
  quit(): void {
    this.quitting = true;
    app.quit();
  }

  /** Release timers and the tray icon. Called on `will-quit`. */
  dispose(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
    for (const timer of this.nudgeTimers) {
      clearTimeout(timer);
    }
    this.nudgeTimers.clear();
    if (this.tray) {
      this.tray.destroy();
      this.tray = null;
    }
  }

  /**
   * Re-read state now. Public so the renderer can call it straight after its own
   * `PATCH /config`, which keeps the tray in step with the rail's pause button without
   * waiting out the poll (R4 acceptance 2).
   */
  async refresh(): Promise<void> {
    if (this.reading) {
      return;
    }
    this.reading = true;
    try {
      const next = deriveState(await readStatus());
      if (next !== this.state) {
        this.state = next;
        this.render();
        this.broadcast();
      }
    } finally {
      this.reading = false;
    }
  }

  // ---- actions -------------------------------------------------------------------

  /** Pause or resume capture through the same `PATCH /config` the rail uses (D2). */
  private async setPaused(paused: boolean): Promise<void> {
    // Deliberately not optimistic: if the agent rejects or never answers, the tray keeps
    // showing what is actually true rather than what was clicked.
    if (await setCaptureEnabled(!paused)) {
      await this.refresh();
      this.nudge();
    }
  }

  /** Stop the agent process (and with it memoryd), staying resident in the tray. */
  private stopAgent(): void {
    this.agent.stop();
    this.nudge();
  }

  /**
   * Bring the agent back after a Stop. Reachability follows a second or two later.
   *
   * Public because the app window offers this too: a stopped agent is exactly the case
   * where the renderer *cannot* ask over the local API (nothing is listening), so the
   * window's Start button has to come through here rather than through `api.ts`.
   */
  startAgent(): void {
    this.agent.start();
    this.nudge();
  }

  /**
   * Whether this process can start/stop an agent at all — false in a dev build, where the
   * agent is run by hand. The renderer asks so it can omit a Start button that could not
   * work, rather than showing one that silently does nothing.
   */
  canControlAgent(): boolean {
    return this.agent.isSupervising();
  }

  /**
   * Note that an update has downloaded and is ready to install. The tray then offers a
   * "Restart to update" row and says so in its tooltip — the same pending update the rail
   * surfaces (autoUpdate.ts), so the two control surfaces never disagree.
   */
  setUpdateReady(version: string): void {
    if (version === this.updateVersion) {
      return;
    }
    this.updateVersion = version;
    this.render();
  }

  // ---- tray ----------------------------------------------------------------------

  private createTray(): void {
    try {
      this.tray = new Tray(trayImage(this.state));
    } catch (err) {
      // A tray can fail to register (no shell, exotic session). Log it and carry on
      // windowed — `hasTray()` makes the caller restore quit-on-close.
      console.error(
        '[tray] could not create tray icon',
        (err as Error).message,
      );
      return;
    }
    // On Windows a left click does nothing by default, which reads as a dead icon.
    this.tray.on('click', () => this.showWindow());
    this.tray.on('double-click', () => this.showWindow());
    this.render();
  }

  /** Push the current state onto the icon, tooltip and menu. */
  private render(): void {
    if (!this.tray) {
      return;
    }
    this.tray.setImage(trayImage(this.state));
    const suffix = this.updateVersion ? ' · update ready' : '';
    this.tray.setToolTip(TOOLTIPS[this.state] + suffix);
    this.tray.setContextMenu(this.buildMenu());
  }

  /**
   * The menu is rebuilt on every state change: labels and enablement are derived, never
   * toggled in place. Items that can't act are disabled rather than hidden — a menu whose
   * rows move under the cursor is worse than one with a greyed row.
   */
  private buildMenu(): Menu {
    const stopped = this.state === 'stopped';
    const paused = this.state === 'paused';
    // In a dev build there is no bundled agent to supervise, so Stop/Start would silently
    // do nothing. Say so by greying them rather than lying.
    const supervising = this.agent.isSupervising();

    const items: Electron.MenuItemConstructorOptions[] = [
      { label: MENU_HEADERS[this.state], enabled: false },
      { type: 'separator' },
      { label: 'Open Lore', click: () => this.showWindow() },
    ];

    // A downloaded update, offered here as a peer to Open — restarting is the user's call,
    // and this is the quickest way to say yes without opening the window (D: no silent install).
    if (this.updateVersion) {
      items.push(
        { type: 'separator' },
        {
          label: `Restart to update — ${this.updateVersion}`,
          click: () => this.host.installUpdate(),
        },
      );
    }

    items.push(
      { type: 'separator' },
      {
        label: paused ? 'Resume capture' : 'Pause capture',
        enabled: !stopped,
        click: () => void this.setPaused(!paused),
      },
      {
        label: stopped ? 'Start Lore' : 'Stop Lore',
        enabled: supervising,
        click: () => (stopped ? this.startAgent() : this.stopAgent()),
      },
      { type: 'separator' },
      { label: 'Quit Lore', click: () => this.quit() },
    );

    return Menu.buildFromTemplate(items);
  }

  /**
   * Tell the user once — and only once, ever — that closing the window didn't stop Lore.
   * Without this, close-to-tray is indistinguishable from a crash; with it on every close,
   * it's nagging.
   */
  private announceFirstHide(): void {
    if (getFlag('hideBalloonShown') || !this.tray) {
      return;
    }
    if (process.platform !== 'win32') {
      return; // balloons are a Windows affordance; elsewhere the icon speaks for itself
    }
    this.tray.displayBalloon({
      title: 'Lore is still running',
      content:
        "It's in your system tray, still keeping your memory warm. Right-click the icon to pause, stop, or quit.",
      iconType: 'info',
    });
    setFlag('hideBalloonShown', true);
  }

  // ---- renderer ------------------------------------------------------------------

  /** Let an open window update its rail immediately on a tray-side change (R4 AC 2). */
  private broadcast(): void {
    for (const window of BrowserWindow.getAllWindows()) {
      if (!window.isDestroyed()) {
        window.webContents.send('lore:lifecycle-state', this.state);
      }
    }
  }

  /** A short burst of re-reads after an action, so the icon converges in ~a second. */
  private nudge(): void {
    for (const delay of NUDGE_MS) {
      const timer = setTimeout(() => {
        this.nudgeTimers.delete(timer);
        void this.refresh();
      }, delay);
      this.nudgeTimers.add(timer);
    }
  }
}
