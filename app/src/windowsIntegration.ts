// windowsIntegration.ts — put the `lore` CLI on the user's PATH (spec 011 T002).
//
// Squirrel runs the app with a lifecycle flag on install/update/uninstall. We hook
// those to keep the bundled CLI directory (resources/native, where lore.exe lives
// next to LoreAgent.exe) on the *user* PATH, so `lore ...` works from any shell.
// Shortcut creation + the install/uninstall quit are handled by electron-squirrel-
// startup; this only manages PATH, which that package doesn't touch.
//
// Squirrel installs each version under %LocalAppData%\Lore\app-<version>\, so the
// native dir changes on every update. We therefore strip any stale Lore native entry
// and add the current one on install/update (keeping PATH pointed at the live
// version), and remove them all on uninstall. PATH failures are logged, never fatal.

import { spawnSync } from 'child_process';
import * as path from 'path';

type SquirrelEvent =
  | '--squirrel-install'
  | '--squirrel-updated'
  | '--squirrel-uninstall'
  | '--squirrel-obsolete'
  | '--squirrel-firstrun';

function nativeDir(): string {
  return path.join(process.resourcesPath, 'native');
}

// PowerShell that rewrites the *user* PATH: drop any prior Lore native entry plus a
// passed-in one, then optionally re-add it. Reading/writing the User scope (not the
// merged process PATH) is the only correct way to persist this, and SetEnvironmentVariable
// broadcasts WM_SETTINGCHANGE so new shells pick it up.
const PATH_SCRIPT = `
$ErrorActionPreference = 'Stop'
$dir = $env:LORE_PATHTOOL_DIR
$add = $env:LORE_PATHTOOL_ADD -eq '1'
$cur = [Environment]::GetEnvironmentVariable('Path','User')
$parts = @()
if ($cur) { $parts = $cur.Split(';') | Where-Object { $_ -ne '' } }
# Drop the target dir and any stale Lore versioned native dir.
$kept = $parts | Where-Object {
  ($_.TrimEnd('\\') -ne $dir.TrimEnd('\\')) -and
  ($_ -notmatch '\\\\Lore\\\\app-[^\\\\]+\\\\resources\\\\native$')
}
if ($add) { $kept = @($dir) + $kept }
$new = ($kept -join ';')
[Environment]::SetEnvironmentVariable('Path', $new, 'User')
`;

function updateUserPath(dir: string, add: boolean): void {
  const result = spawnSync(
    'powershell.exe',
    [
      '-NoProfile',
      '-NonInteractive',
      '-ExecutionPolicy',
      'Bypass',
      '-Command',
      PATH_SCRIPT,
    ],
    {
      env: {
        ...process.env,
        LORE_PATHTOOL_DIR: dir,
        LORE_PATHTOOL_ADD: add ? '1' : '0',
      },
      windowsHide: true,
      encoding: 'utf8',
    },
  );
  if (result.status !== 0) {
    console.error(
      '[path] failed to update user PATH',
      result.stderr || result.error,
    );
  }
}

/**
 * If launched for a Squirrel lifecycle event, update the user PATH accordingly and
 * return true (the caller should let electron-squirrel-startup quit the process).
 * A no-op (returns false) on a normal launch or non-Windows.
 */
export function applySquirrelPathHook(): boolean {
  if (process.platform !== 'win32') return false;
  const event = process.argv[1] as SquirrelEvent | undefined;
  try {
    switch (event) {
      case '--squirrel-install':
      case '--squirrel-updated':
        updateUserPath(nativeDir(), true);
        return true;
      case '--squirrel-uninstall':
        updateUserPath(nativeDir(), false);
        return true;
      case '--squirrel-obsolete':
        return true;
      default:
        return false;
    }
  } catch (err) {
    console.error('[path] squirrel PATH hook failed', err);
    return false;
  }
}
