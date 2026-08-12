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

function logToFile(message: string): void {
  const line = `${new Date().toISOString()} [updater] ${message}\n`;
  try {
    fs.appendFileSync(path.join(app.getPath('userData'), 'updater.log'), line);
  } catch {
    // Logging is best-effort; it must never break the updater itself.
  }
}

/**
 * Wire up Squirrel auto-updates. Safe to call unconditionally: it no-ops in dev and off
 * Windows, registering only the install-on-request IPC handler. `getWindow` lets the
 * updater tell whether the user is looking at the app (prompt to restart) or not (apply
 * the update silently so the next launch is already current).
 */
export function initAutoUpdates(getWindow: () => BrowserWindow | null): void {
  // The renderer's "Restart now" button routes here once the user accepts the prompt.
  ipcMain.handle('lore:install-update', () => {
    logToFile('user accepted restart; quitting to install');
    autoUpdater.quitAndInstall();
  });

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

  autoUpdater.on('update-downloaded', () => {
    logToFile('update downloaded; will install on restart');
    const window = getWindow();
    if (window && !window.isDestroyed() && window.isVisible()) {
      // The user is in the app — let them finish and restart on their own terms.
      window.webContents.send('lore:update-ready');
    } else {
      // Nobody's looking; apply it now so the next launch is already up to date.
      //
      // Since v2-006 this is the *common* case — Lore normally sits in the tray with its
      // window hidden — and Squirrel relaunches the app afterwards with no arguments. Left
      // alone, a quiet background update would therefore end with a window appearing on the
      // user's screen out of nowhere. Remember that we were hidden; the next start consumes
      // the flag and comes up as tray-only.
      setFlag('relaunchHidden', true);
      autoUpdater.quitAndInstall();
    }
  });

  check();
  setInterval(check, CHECK_INTERVAL_MS);
}
