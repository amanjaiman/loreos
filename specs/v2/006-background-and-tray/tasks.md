# v2-006 — Background Running & System Tray · Tasks

> SDD artifact: **PR-sized steps.** Each task is one branch, one gate run
> (`no-mistakes`), one PR. Work them in order — T002 depends on T001's lifecycle module
> existing, T003 on T002's tray.
>
> Local gate before every commit (`AGENTS.md`):
> `npm --prefix app run lint && npx --prefix app tsc --noEmit && npm --prefix app run build`

---

## T001 — Background lifecycle: the app survives its window

**Delivers:** R1.

- `app/src/lifecycle/prefs.ts` — one-shot flag store in `userData` (D4).
- `app/src/lifecycle/agentStatus.ts` — loopback client: `GET /system/status` with a
  1.5 s timeout, `PATCH /config` for the pause bit (D1, D2).
- `app/src/lifecycle/LoreLifecycle.ts` — owns `isQuitting`, the derived state, the
  5 s poll, and the show/hide/quit verbs. No tray yet.
- `app/src/agentProcess.ts` — `stop()` becomes reversible; add `isSupervising()` (D7).
- `app/src/index.ts` — single-instance lock + `second-instance` handler; `close` hides;
  `window-all-closed` no longer quits; `will-quit` still stops the agent.
- `deriveState()` is a pure exported function (plan → Testing).

**Verify:** spec R1 acceptance 1–3. (4 needs the tray balloon; it lands in T002.)

---

## T002 — Tray icon reflecting Running / Paused / Stopped

**Delivers:** R3, and R1 acceptance 4.

- `app/tools/build-tray-icons.mjs` — the tray and Windows app-icon generator (D6).
  Committed, and pointed at from `AGENTS.md` so nobody hand-edits its output.
- `app/src/lifecycle/trayIcons.ts` — generated output, base64, four sizes per state.
- `app/assets/lore.ico` / `lore.png` — generated app and installer icons.
- `LoreLifecycle` creates the `Tray`, swaps image + tooltip on every state transition,
  and fires the one-time "still running" balloon on first hide.

**Verify:** spec R3 acceptance 1–3; R1 acceptance 4. Check both taskbar themes
(Settings → Personalisation → Colors → light/dark).

---

## T003 — The tray menu: open, pause, resume, stop, start, quit

**Delivers:** R4.

- The context menu, rebuilt on each state change (labels and enablement are state-derived,
  items are disabled rather than hidden).
- Left-click and double-click both open the window; right-click opens the menu.
- Stop kills the agent and stays resident; Start respawns and waits for reachability.
- `lore:capture-state` pushed to the renderer on any tray-side change, so the rail
  updates without waiting out its poll; `preload.ts` exposes the subscription and
  `AppShell` consumes it.

**Verify:** spec R4 acceptance 1–4.

---

## T004 — Start with Windows, asked for in onboarding

**Delivers:** R2.

- `app/src/lifecycle/autostart.ts` — read/write the login item through
  `app.getLoginItemSettings()`, registering the Squirrel-stable
  `Update.exe --processStart` command (D3); inert and reported unsupported when
  unpackaged.
- First run enables it by default; `--hidden` (and a consumed `prefs.relaunchHidden`)
  starts with no window (D5).
- `autoUpdate.ts` sets `relaunchHidden` before a silent `quitAndInstall()`.
- `preload.ts` bridge + the toggle on onboarding's **Done** step and in
  Settings → Capture & privacy.

**Verify:** spec R2 acceptance 1–4.

---

## T005 — The app's own Start control

**Delivers:** R5.

- `LoreLifecycle.startAgent()` / `canControlAgent()` go public; `lore:start-agent` and
  `lore:can-start` IPC; `preload.ts` exposes `startLore` / `canStartLore`.
- `useAgentControl(offline)` — one hook holding `canStart` / `starting` / `start`, so the
  rail and Home share the pending state and the give-up timeout instead of each inventing
  one.
- `LiveElement` swaps its pause control for Start when offline; Home's resting panel gains
  a Start button.

**Verify:** spec R5 acceptance 1–4.

---

## T006 — Docs

- `docs/privacy.md` — the main process now makes loopback calls to `127.0.0.1:7842`;
  still zero egress.
- `docs/architecture.md` — the app is a background app with a tray; the agent's lifetime
  is no longer the window's lifetime.
- `specs/v2/README.md` — add the v2-006 row.
- `AGENTS.md` — the tray icons are generated; regenerate, don't hand-edit.

**Verify:** docs match shipped behaviour; no stale "the agent lives for the app's
lifetime" claims remain (there is one in `agentProcess.ts`'s header comment — fix it in
T001 where the behaviour changes).
