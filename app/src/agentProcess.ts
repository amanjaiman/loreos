// agentProcess.ts — the app's lifecycle bridge to the Lore agent (spec 011 T002).
//
// In a packaged install the native payload (the self-contained agent, the CLI, the
// skill, and the frozen memoryd) ships under resources/native. The app launches
// LoreAgent.exe on startup; the agent in turn supervises memoryd (spec 002), so one
// app launch brings up the whole loopback stack. In dev the agent is run separately
// (`dotnet run` / `LoreAgent.exe`), so there is no bundled exe and we do nothing —
// the renderer just talks to whatever is already on :7842, degrading to the "Lore
// isn't running" state if nothing is.
//
// Background capture while the window is closed (tray persistence) is product
// behavior left to a later spec; here the agent lives for the app's lifetime.

import { spawn, ChildProcess } from 'child_process';
import { app } from 'electron';
import * as fs from 'fs';
import * as path from 'path';

/** Where the installer (T002) lays the native payload: resources/native. */
export function nativeDir(): string {
  return path.join(process.resourcesPath, 'native');
}

/** The bundled agent exe, or null when running unpackaged (dev). */
export function resolveAgentExe(): string | null {
  if (!app.isPackaged) return null;
  const exe = path.join(nativeDir(), 'LoreAgent.exe');
  return fs.existsSync(exe) ? exe : null;
}

const RESTART_DELAY_MS = 2000;

/** Spawns and keeps the agent alive for the app's lifetime. */
export class AgentProcess {
  private child: ChildProcess | null = null;
  private stopping = false;
  private restartTimer: NodeJS.Timeout | null = null;

  /** Launches the agent if one is bundled; a no-op in dev. */
  start(): void {
    const exe = resolveAgentExe();
    if (!exe) {
      console.log(
        '[agent] dev build — not spawning an agent; run one yourself ' +
          '(`dotnet run --project agent`) and the app will find it on 127.0.0.1:7842',
      );
      return;
    }
    this.spawn(exe);
  }

  private spawn(exe: string): void {
    if (this.stopping) return;
    console.log(`[agent] starting ${exe}`);
    // windowsHide keeps the agent's console from flashing; cwd is its own dir so
    // relative resolution (memoryd/, skills/) matches the supervisor's expectations.
    const child = spawn(exe, [], {
      cwd: path.dirname(exe),
      windowsHide: true,
      stdio: 'ignore',
    });
    this.child = child;

    child.on('exit', (code) => {
      this.child = null;
      if (this.stopping) return;
      console.warn(
        `[agent] exited (code ${code}); restarting in ${RESTART_DELAY_MS}ms`,
      );
      this.restartTimer = setTimeout(() => this.spawn(exe), RESTART_DELAY_MS);
    });
    child.on('error', (err) => {
      console.error('[agent] failed to launch', err);
    });
  }

  /** Stops the agent and prevents any pending restart. Called on app quit. */
  stop(): void {
    this.stopping = true;
    if (this.restartTimer) {
      clearTimeout(this.restartTimer);
      this.restartTimer = null;
    }
    if (this.child && !this.child.killed) {
      console.log('[agent] stopping');
      // The agent shuts memoryd down cleanly on its own SIGTERM/exit (spec 002 §3.3).
      this.child.kill();
      this.child = null;
    }
  }
}
