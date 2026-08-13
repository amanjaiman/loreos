// autoUpdate.ts — in-app updates for packaged Windows builds (Squirrel + Hazel).
//
// Electron's built-in `autoUpdater` on Windows is Squirrel.Windows: it fetches a RELEASES
// manifest from a feed URL and applies the newest full/delta `.nupkg`. We point it at our
// Hazel deployment (https://lore-hazel.vercel.app), which proxies the *private* GitHub
// Releases of amanjaiman/loreos and serves the Squirrel feed at
// `/update/win32/<current-version>`. Hazel compares that version against the latest Release
// and hands back an update when one exists — so publishing a higher-versioned tag
// (release.yml) is all it takes for installed apps to pick the update up on their next poll.
//
// Consent model: Squirrel downloads the update in the background, but Lore never *installs*
// it on its own. Once the `.nupkg` is staged we record it as a pending update, surface it in
// the rail and the tray, and wait — the restart only happens when the user asks for it. This
// is deliberate transparency for an open-source app: an update is a thing the user is told
// about and chooses, not something that swaps itself in behind their back. (v0.1.x installed
// silently when the window was hidden; this replaces that.)
//
// Scope: Windows only. The macOS/Linux makers ship plain archives with no update feed, and
// Squirrel.Windows' autoUpdater throws if given a URL on other platforms — so this is a
// no-op off win32. Unpackaged dev builds never check.

import { app, autoUpdater, BrowserWindow, ipcMain } from 'electron';
import * as fs from 'fs';
import * as path from 'path';

import { setFlag } from './lifecycle/prefs';

/** The reused Hazel deployment (see specs / installer README). Repointed at loreos. */
const FEED_HOST = 'https://lore-hazel.vercel.app';
/** Re-check while the app is running; the launch check covers most cases. */
const CHECK_INTERVAL_MS = 10 * 60 * 1000;
/** Release metadata is optional; never hide a staged update behind a slow feed. */
const RELEASE_INFO_TIMEOUT_MS = 5_000;

/** A downloaded, ready-to-install update, as the rail and the changelog modal see it. */
export interface PendingUpdate {
  /** Release name straight off the feed, e.g. "v0.1.2". */
  version: string;
  /** The release body (markdown) — Lore's curated release-notes.md, published verbatim
   * by release.yml. Empty if the feed had none. */
  notes: string;
}

/** What the updater needs from its host — the app window, and the tray that mirrors it. */
export interface UpdateHost {
  getWindow(): BrowserWindow | null;
  /** An update finished downloading and is now waiting on the user's go-ahead. */
  onUpdateReady(update: PendingUpdate): void;
}

// The update Squirrel has already staged, held until the user chooses to install it. A
// window opened *after* the download completed reads this via `lore:get-update-state`, since
// it missed the `lore:update-ready` push.
let pending: PendingUpdate | null = null;

function logToFile(message: string): void {
  const line = `${new Date().toISOString()} [updater] ${message}\n`;
  try {
    fs.appendFileSync(path.join(app.getPath('userData'), 'updater.log'), line);
  } catch {
    // Logging is best-effort; it must never break the updater itself.
  }
}

/**
 * Ask Hazel what the update we just downloaded actually is. Squirrel's own `update-downloaded`
 * event carries no usable notes on Windows (it only ever saw the RELEASES manifest), so we
 * hit the JSON feed for the same version and read back `{ name, notes }`. `app.getVersion()`
 * is still the *old* version at this point, which is exactly what makes the feed report the
 * newer release. Any failure degrades to a version-less label with no notes rather than
 * claiming the installed version is the staged update — and never throws.
 */
async function fetchReleaseInfo(): Promise<PendingUpdate> {
  const fallback: PendingUpdate = {
    version: 'a new version',
    notes: '',
  };
  try {
    const res = await fetch(`${FEED_HOST}/update/win32/${app.getVersion()}`, {
      signal: AbortSignal.timeout(RELEASE_INFO_TIMEOUT_MS),
    });
    if (!res.ok) {
      logToFile(`notes fetch: HTTP ${res.status}`);
      return fallback;
    }
    const body = (await res.json()) as { name?: unknown; notes?: unknown };
    return {
      version:
        typeof body.name === 'string' && body.name.length > 0
          ? body.name
          : fallback.version,
      notes: typeof body.notes === 'string' ? body.notes : '',
    };
  } catch (err) {
    logToFile(`notes fetch failed: ${(err as Error).message}`);
    return fallback;
  }
}

/**
 * Apply the already-downloaded update and restart. Shared by the rail's "Restart to update"
 * button and the tray's menu item — the two user-facing ways to say yes.
 *
 * If the window is hidden when this fires, remember it: Squirrel relaunches the app with no
 * arguments after installing, and without the flag Lore would pop a window open on a user who
 * was working entirely from the tray. The next launch consumes the flag and comes up tray-only.
 */
export function installDownloadedUpdate(
  getWindow: () => BrowserWindow | null,
): void {
  const window = getWindow();
  const hidden = !window || window.isDestroyed() || !window.isVisible();
  if (hidden) {
    setFlag('relaunchHidden', true);
  }
  logToFile(`installing on user request (hidden=${hidden})`);
  autoUpdater.quitAndInstall();
}

/**
 * Wire up Squirrel auto-updates. Safe to call unconditionally: it no-ops in dev and off
 * Windows, registering only the install/query IPC handlers. The host lets the updater reach
 * the current window (to place the "relaunch hidden" flag correctly) and tell the tray when
 * an update is waiting.
 */
export function initAutoUpdates(host: UpdateHost): void {
  const getWindow = (): BrowserWindow | null => host.getWindow();

  // The rail button and tray item both route here once the user accepts.
  ipcMain.handle('lore:install-update', () =>
    installDownloadedUpdate(getWindow),
  );
  // A window that opened after the download completed asks for the pending update on mount.
  ipcMain.handle('lore:get-update-state', (): PendingUpdate | null => pending);

  if (!app.isPackaged || process.platform !== 'win32') {
    return;
  }

  logToFile(`started, version=${app.getVersion()}`);

  autoUpdater.on('error', (err) => logToFile(`error: ${err.message}`));
  autoUpdater.on('checking-for-update', () =>
    logToFile('checking for update…'),
  );
  autoUpdater.on('update-available', () =>
    logToFile('update available, downloading…'),
  );
  autoUpdater.on('update-not-available', () => logToFile('already up to date'));

  const feedUrl = `${FEED_HOST}/update/win32/${app.getVersion()}`;
  try {
    autoUpdater.setFeedURL({ url: feedUrl });
  } catch (err) {
    logToFile(`setFeedURL failed: ${(err as Error).message}`);
    return;
  }

  const check = (): void => {
    try {
      autoUpdater.checkForUpdates();
    } catch (err) {
      logToFile(`checkForUpdates failed: ${(err as Error).message}`);
    }
  };

  let checkTimer: NodeJS.Timeout | null = null;
  let updateDownloaded = false;

  autoUpdater.on('update-downloaded', () => {
    // Squirrel has staged the package. Further checks can only rediscover and re-stage the
    // same update while the user is deciding when to restart, so stop polling immediately.
    if (updateDownloaded) {
      return;
    }
    updateDownloaded = true;
    if (checkTimer) {
      clearInterval(checkTimer);
      checkTimer = null;
    }
    logToFile('update downloaded; awaiting the user to install');
    void (async () => {
      const info = await fetchReleaseInfo();
      pending = info;
      // Announce it, but install nothing. Push to any window that's already open, and let the
      // tray reflect it; a window opened later catches up through `lore:get-update-state`.
      for (const window of BrowserWindow.getAllWindows()) {
        if (!window.isDestroyed()) {
          window.webContents.send('lore:update-ready', info);
        }
      }
      host.onUpdateReady(info);
    })();
  });

  check();
  checkTimer = setInterval(check, CHECK_INTERVAL_MS);
}
