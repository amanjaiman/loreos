// autostart.ts — start Lore with the user's Windows session (spec v2-006 R2/D3).
//
// The OS is the source of truth. `getLoginItemSettings` reads the real `Run` entry, so the
// toggle in Settings cannot drift from what Windows will actually do — if the user removes
// Lore from Task Manager's Startup tab, the app reports it as off, because it is off.
//
// The subtlety is *what* to register. Squirrel installs every version into its own
// `%LocalAppData%\Lore\app-<version>\`, so a login item naming today's Lore.exe is dead
// weight after the next update. Squirrel keeps a stable `Update.exe` one level up whose
// `--processStart` launches whichever version is current, so that is what we register —
// the same indirection Squirrel's own shortcuts use.

import { app } from 'electron';
import * as fs from 'fs';
import * as path from 'path';

import { getFlag, setFlag } from './prefs';

/** The Squirrel stub that always launches the current version. */
function updateExe(): string {
  return path.resolve(path.dirname(process.execPath), '..', 'Update.exe');
}

/**
 * The exact login item this app owns. Passing it to both getters and setters is what makes
 * "is it on" and "turn it on" agree on the same registry entry.
 *
 * `--hidden` is what makes a session start a *background* start: tray icon, agent running,
 * no window in the user's face at login (R2).
 */
function loginItem(): Electron.Settings {
  return {
    path: updateExe(),
    args: [
      '--processStart',
      path.basename(process.execPath),
      '--process-start-args',
      '"--hidden"',
    ],
  };
}

/**
 * Whether a login item can be registered at all. False in dev (no Squirrel stub — a login
 * item would point at electron.exe and launch something the user never installed) and off
 * Windows. The Settings toggle reads this and explains itself rather than pretending.
 */
export function isSupported(): boolean {
  if (process.platform !== 'win32' || !app.isPackaged) {
    return false;
  }
  try {
    return fs.existsSync(updateExe());
  } catch {
    return false;
  }
}

/** Whether Lore is registered to start with the session, according to Windows. */
export function isEnabled(): boolean {
  if (!isSupported()) {
    return false;
  }
  try {
    return app.getLoginItemSettings(loginItem()).openAtLogin;
  } catch (err) {
    console.warn(
      '[autostart] could not read login item',
      (err as Error).message,
    );
    return false;
  }
}

/** Register or remove the login item. Returns the state Windows reports afterwards. */
export function setEnabled(enabled: boolean): boolean {
  if (!isSupported()) {
    return false;
  }
  try {
    app.setLoginItemSettings({ ...loginItem(), openAtLogin: enabled });
  } catch (err) {
    console.warn(
      '[autostart] could not write login item',
      (err as Error).message,
    );
  }
  return isEnabled();
}

/**
 * Turn autostart on once, on first run (R2: default on).
 *
 * Guarded by a persisted flag rather than by "is it already on", because those differ in
 * the case that matters: a user who deliberately turned it off must not have it switched
 * back on at the next launch.
 */
export function applyDefaultOnce(): void {
  if (!isSupported() || getFlag('autostartDefaultApplied')) {
    return;
  }
  setEnabled(true);
  setFlag('autostartDefaultApplied', true);
}

/** Whether this launch should come up hidden: a session start, or a post-update relaunch. */
export function startsHidden(argv: readonly string[]): boolean {
  return argv.includes('--hidden');
}
