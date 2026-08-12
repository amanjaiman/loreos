# v2-006 — Background Running & System Tray · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> **Audience:** an agent implementing the Electron main process (`app/src/`), plus two
> small renderer surfaces (onboarding + settings toggle).
> **Depends on:** v2-004 (frameless shell), v2-005 (`GET /system/status` carries
> `capture.enabled`). Both merged.

## Why this exists

Lore is an **ambient** capture agent, but today it is not ambient at all. The window is
the process: `app.on('window-all-closed')` calls `app.quit()`, `will-quit` kills
`LoreAgent.exe`, and with it memoryd. Closing the window therefore stops capture
entirely, and nothing restarts it after a reboot. The product promise — "Lore keeps your
memory warm in the background" (the literal copy on the last onboarding step) — is false
for any user who closes the window.

There is also no way to tell whether Lore is running without opening the app, and no way
to pause or stop it without opening the app.

This spec makes Lore a **background app with a tray presence**:

1. It keeps running when the window is closed, and starts with the user's session.
2. A tray icon shows, at a glance, which of three states it is in.
3. That icon's menu is a complete lifecycle control: open, pause, resume, stop, start, quit.

**Out of scope** (do not pull in): notifications/toasts beyond the single first-hide
balloon; a tray-hosted mini-view of recent memories; macOS/Linux tray parity beyond what
Electron gives for free; any change to capture, distillation, lifecycle, or storage; any
new agent API endpoint (this spec is a *client* of the existing `/system/status` and
`PATCH /config`).

---

## The state model

Exactly three user-visible states. They are **derived**, never independently stored:

| State | Meaning | Derivation |
|---|---|---|
| **Running** | The agent is up and capturing. | agent process alive **and** `capture.enabled` is true |
| **Paused** | The agent is up; capture is off. Recall, the API, MCP and the CLI all still work. | agent process alive **and** `capture.enabled` is false |
| **Stopped** | The agent process is not running. Nothing is listening on `:7842`; recall is unavailable to every client. | agent process not alive / API unreachable |

This is deliberately the same three-way split the rail already renders
(`listening` / `paused` / `offline` in `LiveElement.tsx`) — the tray must never disagree
with the rail, because both are visible at once.

**Pause is the agent's existing `config.capture.enabled`** (v2-005 R2 already reports it
and the rail already writes it via `PATCH /config`). The tray is a second control on the
same switch, not a second switch.

**Stop is process-level**: it terminates `LoreAgent.exe`, which shuts memoryd down with
it (spec 002 §3.3). Stop is not "pause harder" — it is for the user who wants Lore
genuinely gone from the machine for a while without uninstalling it. Start brings it back.

**Quit** exits the app: tray icon gone, agent stopped, nothing left running.

---

## R1 — Lore keeps running when the window is closed

**Today:** close → `window-all-closed` → `app.quit()` → `will-quit` → `agent.stop()`.
Capture ends.

**Wanted:**

- Closing the window **hides** it. The process, the tray icon and the agent all survive.
- The window is hidden, not destroyed, so reopening is instant and preserves the current
  route and scroll position.
- `window-all-closed` must **not** quit while the tray is alive.
- The app quits only via an explicit quit: the tray's **Quit Lore** item, or
  `autoUpdater.quitAndInstall()`. On any quit the agent is stopped first, so no orphaned
  `LoreAgent.exe` / memoryd survives the app.
- **The first time** the window is hidden this way, and only the first time, the user is
  told — a tray balloon: *"Lore is still running. It's in your system tray."* The fact
  that this has been shown persists across restarts.
- Only one Lore may run at a time. A second launch (double-clicking the shortcut while
  Lore sits in the tray) must **not** start a second agent; it must reveal and focus the
  window already running.

**Acceptance:**

1. With the window closed, `LoreAgent.exe` is still running and `GET /system/status`
   still answers; new memories are still captured.
2. Launching Lore a second time from the Start menu shows the existing window; exactly
   one `Lore.exe` main process and one `LoreAgent.exe` exist.
3. Quit from the tray leaves no `Lore.exe`, no `LoreAgent.exe` and no memoryd process.
4. The "still running" balloon appears on the first hide and never again, including
   after an app restart.

---

## R2 — Lore starts with the user's session

**Today:** nothing. After a reboot Lore is not running until launched by hand, and the
user has no reason to suspect otherwise.

**Wanted:**

- Lore registers a login item so it starts with the user's Windows session.
- A session start is a **hidden** start: tray icon, agent running, **no window**. Login
  must not throw a window in the user's face.
- **Default on**, but the user is asked, not told: the onboarding flow carries the
  toggle (on by default) so the choice is made during setup, and Settings carries the
  same toggle so it stays reversible forever.
- The toggle's truth is the OS login item itself, so it cannot drift from reality — if a
  user removes the entry via Task Manager's Startup tab, the app reports it as off.
- The login item must **survive updates**. Squirrel installs each version into its own
  `app-<version>` directory, so a login item pointing at today's `Lore.exe` is broken by
  the next update. It must point at the stable stub instead.
- After a silent auto-update installs while Lore was hidden in the tray, Lore must come
  back **hidden**, not with a window the user never asked for.

**Acceptance:**

1. With the toggle on, Lore appears in Task Manager → Startup and starts on next login
   with a tray icon and no window.
2. With the toggle off, it does not appear there and does not start.
3. Turning it off in Settings and reopening Settings shows it off (state read from the OS,
   not from a cached copy).
4. After an update is applied, the login item still launches the app (i.e. it does not
   reference the previous version's directory).

---

## R3 — The tray icon shows the state

**Wanted:**

- A tray icon exists whenever Lore is running, window or no window.
- Its **artwork** differs per state — not only its tooltip — because the tooltip requires
  a hover and the point is at-a-glance legibility:
  - **Running** — the Lore mark, Cerulean.
  - **Paused** — the Lore mark, amber, with the arcs dropped (the mark stops "speaking").
  - **Stopped** — the Lore mark, grey, hollow.
- The icon must stay legible on both a light and a dark Windows taskbar.
- Its **tooltip** names the state in words: `Lore — capturing`, `Lore — paused`,
  `Lore — stopped`.
- The icon follows state changes **from any source** within ~5s, and immediately when the
  change was made through the app or the tray itself. Sources include: the rail's pause
  button, `PATCH /config` from the CLI or any MCP client, the agent crashing, and the
  agent's supervisor restarting it.

**Acceptance:**

1. Pausing from the rail changes the tray icon and tooltip without touching the tray.
2. Killing `LoreAgent.exe` from Task Manager moves the tray to Stopped within ~5s;
   letting it restart moves it back to Running.
3. The three icons are distinguishable at 16×16 on both taskbar themes.

---

## R4 — The tray menu is a complete lifecycle control

**Wanted:** right-click (and left-click, which on Windows should behave the same rather
than doing nothing) opens a menu:

```
Lore — capturing          ← disabled; the state, in words
─────────────────
Open Lore
─────────────────
Pause capture             ← "Resume capture" when paused; disabled when stopped
Stop Lore                 ← "Start Lore" when stopped
─────────────────
Quit Lore
```

- **Open Lore** shows and focuses the window, creating it if it was destroyed. It is also
  what a double-click on the icon does.
- **Pause / Resume** writes `capture.enabled` through the same `PATCH /config` path the
  rail uses — one write path, no second source of truth.
- **Stop** terminates the agent (and thus memoryd) and stays in the tray, so the user can
  start it again. **Start** brings it back and waits for it to become reachable.
- **Quit Lore** stops the agent and exits.
- Items that cannot work in the current state are **disabled, not hidden** — a menu whose
  items move around under the cursor is worse than one with a greyed row.
- If the window is open when the state changes from the tray, the app's rail must reflect
  it promptly rather than waiting out its poll interval.

**Acceptance:**

1. Each item performs its action with the window closed, and with the window open.
2. Pause from the tray is visible in the rail; pause from the rail is visible in the tray.
3. Stop, then Start, returns to a working agent — `GET /system/status` answers again and
   capture resumes.
4. While stopped, Pause/Resume is greyed out; while running, Start is not offered.

---

---

## R5 — The app can start a stopped Lore, not just the tray

Stop is reachable from the tray. If Start were *only* reachable from the tray, Stopped
would be a state the user can enter from one surface and leave from another — and the app
window, which stays perfectly usable while stopped, would show "Lore isn't running" with no
way to act on it. That is a trap, and it is the same argument that put pause in the rail
rather than in Settings.

**Wanted:**

- The rail's live element offers **Start** when the state is Stopped, in the same slot and
  with the same weight as the pause/resume control it replaces there.
- Home's "Lore is resting" panel offers it too — that is where a user actually lands.
- While starting, both say so (`Starting…`) and refuse a second click, then return to the
  normal running view on their own once the agent answers. If it never answers, they stop
  claiming progress rather than spinning forever.
- The control is **absent, not broken**, in a build that has no agent to supervise (dev).

**Note on the seam:** every other control in the app writes through `api.ts` (spec 005 is
the renderer's only network seam). This one cannot, and the reason is the point of the
feature: Stopped means nothing is listening on `:7842`. Start therefore goes through the
preload bridge to the main process, which holds the agent's process handle — the same verb
the tray's Start item calls. It is not a second network path; it is not a network path.

**Acceptance:**

1. Stop from the tray, then start again from the rail — the agent comes back and the rail
   returns to Running without touching the tray.
2. The same from Home's resting panel.
3. During a start, the control reads `Starting…` and is not clickable; it resolves by
   itself when the agent answers.
4. In a dev build (no bundled agent) no Start control appears in either place.

---

## Non-goals worth stating explicitly

- **No new outbound network calls.** The main process gains a *loopback* client
  (`127.0.0.1:7842`) only. `docs/privacy.md` gains a line saying so.
- **No business logic in the main process.** It reads `/system/status` and writes
  `PATCH /config` — the same two calls the renderer already makes, for the same reasons.
- **No new agent-side API.** If this spec appears to need one, it has been misread.
