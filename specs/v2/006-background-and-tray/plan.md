# v2-006 — Background Running & System Tray · Plan

> SDD artifact: **how.** Reads on top of [`specification.md`](specification.md).
> Everything here lives in `app/src/` (Electron main) except two small renderer edits.

## Shape

One new module owns the whole thing, and `index.ts` becomes its caller:

```
app/src/
├─ index.ts              # wires: single-instance → ready → lifecycle.start()
├─ lifecycle/
│  ├─ LoreLifecycle.ts   # the state machine + tray + menu (R1, R3, R4)
│  ├─ agentStatus.ts     # loopback client: poll /system/status, PATCH /config
│  ├─ autostart.ts       # login item, Squirrel-stable path (R2)
│  ├─ prefs.ts           # tiny JSON store in userData: one-shot flags
│  └─ trayIcons.ts       # the three generated icons, as embedded data
├─ agentProcess.ts       # (edit) stop() must be reversible; expose isSupervising()
└─ autoUpdate.ts         # (edit) remember "was hidden" across a silent update
```

`LoreLifecycle` is the only thing that decides state, and the only thing that talks to the
tray. `index.ts` keeps its window/IPC responsibilities and gains no branching.

## Decisions

### D1 — State is derived, and polled from the same place the renderer reads

The main process gets a loopback client (`agentStatus.ts`) that calls
`GET http://127.0.0.1:7842/system/status` every **5 s** — the same interval and the same
endpoint the rail already polls (`useSystemStatus(5000)`), so the two surfaces can't
disagree by more than one tick. `capture.enabled` off that payload is the pause bit; a
failed/timed-out request (1.5 s `AbortSignal.timeout`) is Stopped.

Deriving Stopped from *reachability* rather than from "did we spawn a child" is what makes
R3 acceptance 2 work — an agent killed in Task Manager, or one that never started because
this is a dev build, is Stopped by the same rule.

Polling alone would leave the tray up to 5 s stale after a click, so every state-changing
path (tray pause, rail pause, stop, start) also pokes the machine to re-read immediately.

**Why not have the agent push?** It would need a websocket/SSE endpoint the agent doesn't
have, and this spec is explicitly not allowed to add agent API. A 5 s poll of a
non-blocking status endpoint over loopback is cheap.

### D2 — Pause writes through `PATCH /config`, exactly like the rail

The tray does not get its own notion of pause. `Pause capture` issues
`PATCH /config {"capture":{"enabled":false}}` — byte-identical to what `AppShell.toggleCapture`
sends. One write path; the agent stays the source of truth.

### D3 — Autostart truth is the OS, and the path must be Squirrel-stable

`app.getLoginItemSettings()` / `setLoginItemSettings()` read and write the real
`Run` registry entry, so the toggle can't drift from what Windows will actually do (R2
acceptance 3). Nothing about "is autostart on" is cached in a file.

The registered command must not be `.../app-0.1.2/Lore.exe`, which the next update
orphans. Squirrel keeps a stable stub one level up, so we register:

```
%LocalAppData%\Lore\Update.exe --processStart Lore.exe --process-start-args "--hidden"
```

which always launches the current version. This is the documented Squirrel.Windows
pattern and is what `electron-squirrel-startup`'s shortcuts use too.

In an unpackaged dev build there is no `Update.exe`; autostart is inert there and the
Settings toggle says so rather than writing a login item that points at `electron.exe`.

### D4 — One-shot flags need a file; only one-shot flags go in it

`prefs.ts` is a small JSON store in `app.getPath('userData')` holding exactly three
booleans: `hideBalloonShown` (R1 acceptance 4), `relaunchHidden` (D5), and
`autostartDefaultApplied` (D3 — the guard that makes "default on" happen once rather than
re-enabling autostart behind the back of a user who turned it off). It is not a general
settings store — user settings belong in the agent's config, reached through the API, per
the constitution. If a fourth key ever wants to live here, that is a signal to check
whether it belongs in the agent instead.

### D5 — A silent update must not resurrect the window

`autoUpdate.ts` already installs silently when the window isn't visible. With R1 that
becomes the *common* case, and Squirrel relaunches the app afterwards with no arguments —
so a user who had Lore quietly in the tray would get a window in their face after an
update. Before that silent `quitAndInstall()` we set `prefs.relaunchHidden = true`; the
next start consumes-and-clears it and starts hidden. This is the minimum change to the
updater and leaves its visible-window path untouched.

### D6 — Icons are generated once, committed, and reproducible

Electron's `nativeImage` cannot rasterise SVG, so the tray needs real bitmaps.
`app/tools/build-tray-icons.mjs` renders the transparent mark's three rounded layers into
16/20/24/32 px PNGs using only Node's `zlib`. The same pass emits the
multi-resolution Windows `.ico` used by the packaged app and Squirrel installer. No new
dependency, no binary blob whose provenance is a mystery: re-running the script
reproduces the committed bytes.

Running uses the unmodified mark. Paused and stopped add amber pause and grey hollow
badges, respectively, so state is carried by **shape as well as colour** and the brand
mark remains consistent across every surface.

The generated PNGs are embedded as base64 in `trayIcons.ts` rather than shipped as
files, because a tray icon read from `resources/` at runtime is one more path that can be
wrong in a packaged build for no benefit — they total under 4 KB.

The generated multi-resolution `lore.ico` is also copied into the packaged resources and
assigned directly to every `BrowserWindow`. The main process sets
`com.squirrel.Lore.Lore`, matching the AppUserModelID on Squirrel's installed shortcut;
together these prevent Windows from grouping the window under Electron's default taskbar icon.

The pause badge uses `#D9973A`, a deepened amber that remains visible against a light
taskbar. The stopped badge uses `--n-5` `#7D7565` and is hollow. The transparent mark
uses the same artwork on both light and dark taskbars.

### D7 — Stop must be reversible, which `AgentProcess` currently isn't

`stop()` sets `stopping = true` permanently, so a later `start()` would spawn and then be
ignored by the exit handler. `start()` now clears the flag, and `isSupervising()` reports
whether there is a bundled exe to supervise at all — which is what greys out Stop/Start in
a dev build (R4).

### D8 — The window is hidden, never closed, unless we are really quitting

A single `isQuitting` flag in `LoreLifecycle`, set by Quit and by
`autoUpdater.quitAndInstall`'s path, decides whether `win.on('close')` prevents default
and hides. `window-all-closed` stops calling `app.quit()` on Windows/Linux. Since the
window is hidden rather than destroyed, `mainWindow` stays valid, which the updater's
`getWindow()` already tolerates (it checks `isVisible()`, not existence).

`app.requestSingleInstanceLock()` guards R1 acceptance 2; the `second-instance` handler
shows and focuses the existing window.

### D9 — A Squirrel lifecycle launch must not boot the app

`electron-squirrel-startup` returns true on `--squirrel-install` / `--squirrel-updated` /
`--squirrel-uninstall` and quits — but *asynchronously*, waiting for the shortcut-writing
`Update.exe` to close first. Module execution continues meanwhile and `ready` still fires.
Before v2-006 that was mostly harmless (a window might flash during an install). With a
tray and a supervised agent it is not: an install would spawn `LoreAgent.exe` and plant a
tray icon, then quit out from under both.

So the whole wiring — `ready`, the tray, the agent, every `app.on` — now lives behind a
guard and is skipped entirely on those launches. Note `--squirrel-firstrun` is *not* one of
them: that is the user's genuine first launch after installing, and it boots normally, with
a window.

## Renderer surface (kept deliberately small)

- `preload.ts` gains `getAutostart()` / `setAutostart(on)` / `isAutostartSupported()` and
  an `onCaptureState` subscription so the rail can react to a tray-side pause without
  waiting for its poll (R4 acceptance 2).
- Onboarding: the autostart toggle joins the **Done** step — the step whose copy already
  promises background capture — defaulted on.
- Settings → Capture & privacy: the same toggle, so it is reversible forever.

Both call the same preload bridge; neither holds state beyond what it reads back.

## Testing

The app has no test harness (`AGENTS.md`: "no tests yet"), and standing one up for
Electron main is out of proportion to this change. The state derivation is therefore
factored into a **pure function** — `deriveState({reachable, captureEnabled})` — so if/when
a harness lands, the part with actual branching is testable without Electron. (Whether the
app supervises an agent at all is a *menu-enablement* question, not a state question, so it
stays out of this signature.)
Everything else in this spec is verified by the manual acceptance script in
[`tasks.md`](tasks.md), which each task carries.

The local gate before committing stays: `npm --prefix app run lint`,
`npx --prefix app tsc --noEmit`, `npm --prefix app run build`.

## Risks

| Risk | Mitigation |
|---|---|
| A user quits via Task Manager, orphaning `LoreAgent.exe`. | Pre-existing (the agent already outlives a killed app); `will-quit` covers every graceful path. Not widened by this spec. |
| Tray icon unreadable on an unusual taskbar theme/scale. | Four sizes shipped; mid-tone colours chosen against both themes; tooltip always carries the state in words. |
| Login item written for a per-user install that is later machine-wide. | Squirrel's installer is per-user by design (`%LocalAppData%`), which is the only layout we ship. |
| Close-to-tray surprises a user who wanted quit. | The one-time balloon (R1) plus an always-available Quit in the menu. If it still generates complaints, the escape hatch is a preference — deliberately not built yet. |
