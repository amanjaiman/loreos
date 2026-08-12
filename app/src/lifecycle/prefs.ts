// prefs.ts — a deliberately tiny store for the handful of flags the *main process* needs
// before, or independently of, the agent (spec v2-006 D4).
//
// This is NOT a settings store. User settings live in the agent's config and are reached
// through the local API, which is the single source of truth (constitution §3). What is
// here is the residue that genuinely cannot live there: one-shot facts about this
// installation that must be readable at startup, before anything is listening on :7842.
//
// If you find yourself adding a fourth key, check first whether it belongs in the agent.

import { app } from 'electron';
import * as fs from 'fs';
import * as path from 'path';

interface Prefs {
  /** The "Lore is still running" tray balloon has been shown once (R1 acceptance 4). */
  hideBalloonShown?: boolean;
  /** Set before a silent update install so the post-update launch stays hidden (D5). */
  relaunchHidden?: boolean;
  /** The autostart default (on) has been applied once, so we never re-enable it
   *  behind the back of a user who turned it off (R2). */
  autostartDefaultApplied?: boolean;
}

type PrefKey = keyof Prefs;

function prefsPath(): string {
  return path.join(app.getPath('userData'), 'app-prefs.json');
}

function read(): Prefs {
  try {
    const raw = fs.readFileSync(prefsPath(), 'utf8');
    const parsed: unknown = JSON.parse(raw);
    // Anything unparseable or non-object is treated as "no prefs yet" rather than an
    // error: every caller has a working default, and a corrupt file must never be fatal.
    return typeof parsed === 'object' && parsed !== null
      ? (parsed as Prefs)
      : {};
  } catch {
    return {};
  }
}

function write(prefs: Prefs): void {
  try {
    fs.mkdirSync(path.dirname(prefsPath()), { recursive: true });
    fs.writeFileSync(
      prefsPath(),
      `${JSON.stringify(prefs, null, 2)}\n`,
      'utf8',
    );
  } catch (err) {
    console.warn('[prefs] could not persist', (err as Error).message);
  }
}

/** Read one flag; false when unset or unreadable. */
export function getFlag(key: PrefKey): boolean {
  return read()[key] === true;
}

/** Write one flag, leaving the others intact. Best-effort — never throws. */
export function setFlag(key: PrefKey, value: boolean): void {
  write({ ...read(), [key]: value });
}

/**
 * Read a flag and clear it in the same breath. Used for `relaunchHidden`, where acting on
 * a stale flag would hide a window the user asked for — so consuming it must not depend on
 * a later write succeeding.
 */
export function consumeFlag(key: PrefKey): boolean {
  const prefs = read();
  const value = prefs[key] === true;
  if (value) {
    write({ ...prefs, [key]: false });
  }
  return value;
}
