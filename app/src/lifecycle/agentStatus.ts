// agentStatus.ts — the main process's loopback client for the local API (spec v2-006 D1/D2).
//
// The tray needs two things the renderer already reads: whether the agent answers, and
// whether capture is enabled. Rather than invent a channel, the main process calls the
// same two endpoints the rail calls — GET /system/status (v2-005 R2 carries capture.enabled)
// and PATCH /config to write the pause bit.
//
// This is loopback only: 127.0.0.1:7842, the same origin the renderer uses, no auth, no
// remote surface, no egress (docs/privacy.md). Unreachable is a normal, expected answer —
// it is precisely how "Stopped" is detected — so failures are values here, never throws.

const API_BASE = 'http://127.0.0.1:7842';

/**
 * Short by design. A status call that hasn't answered in 1.5 s is not going to inform a
 * tray icon usefully, and the poll comes round again in 5 s. Keeping it well under the
 * poll interval is what stops slow requests from stacking up.
 */
const REQUEST_TIMEOUT_MS = 1500;

/** What the tray needs to know about the agent right now. */
export interface AgentSnapshot {
  /** The local API answered. False means nothing is listening — the agent is down. */
  reachable: boolean;
  /**
   * config.capture.enabled as the agent reports it. Defaults to true when the agent is
   * up but omits the (optional) capture block, so a status payload that predates v2-005
   * reads as "running" rather than as a phantom pause.
   */
  captureEnabled: boolean;
}

/** `AbortSignal.timeout` isn't in the DOM lib this app's TS target pulls in — do it by hand. */
async function request(
  path: string,
  init?: { method: string; body: string },
): Promise<Response | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    return await fetch(`${API_BASE}${path}`, {
      ...(init
        ? {
            method: init.method,
            body: init.body,
            headers: { 'Content-Type': 'application/json' },
          }
        : {}),
      signal: controller.signal,
    });
  } catch {
    // Connection refused, DNS-less loopback failure, or our own abort: all mean the same
    // thing to the caller — the agent isn't answering.
    return null;
  } finally {
    clearTimeout(timer);
  }
}

/** Poll the agent. Never throws; an unreachable agent is the `reachable: false` snapshot. */
export async function readStatus(): Promise<AgentSnapshot> {
  const response = await request('/system/status');
  if (!response || !response.ok) {
    return { reachable: false, captureEnabled: false };
  }
  try {
    const body = (await response.json()) as {
      capture?: { enabled?: boolean };
    };
    return { reachable: true, captureEnabled: body.capture?.enabled !== false };
  } catch {
    // It answered but the body was not what we expect. It is up; assume capture is on
    // rather than reporting a pause we cannot substantiate.
    return { reachable: true, captureEnabled: true };
  }
}

/**
 * Write the pause bit through the same path the rail uses — `PATCH /config`, identical
 * payload (D2). Returns whether the agent accepted it, so the caller can leave the tray
 * alone instead of showing a state the agent never entered.
 */
export async function setCaptureEnabled(enabled: boolean): Promise<boolean> {
  const response = await request('/config', {
    method: 'PATCH',
    body: JSON.stringify({ capture: { enabled } }),
  });
  return response !== null && response.ok;
}
