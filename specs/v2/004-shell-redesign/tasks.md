# v2-004 — Shell Redesign · Tasks

> Atomic, dependency-ordered, one PR each (~1–3 h), every task ends at a green
> `no-mistakes` gate (constitution §7). See [`plan.md`](plan.md) for designs.

- [x] **T001 — Frameless window.** `titleBarStyle: 'hidden'` + `titleBarOverlay`
  (never `frame: false`, so Snap Layouts survive); application menu replaced with a
  template whose top-level items are `visible: false`, preserving the clipboard and
  undo accelerators on Windows; `lore:set-titlebar` IPC (main clamps to two hex
  strings) and the `setTitleBar` preload bridge. AC 1, 2, 3.

- [x] **T002 — Shell: ground, strip, rail, card.** New `Rail.tsx` +
  `LiveElement.tsx`, `AppShell.tsx` rewritten to the four-area grid, `Header.tsx`
  deleted, `chrome.css` rewritten (per-corner radius vars, drag regions, the sticky
  card header carrying its own top radii). Staged count on the Memory nav item.
  AC 1, 6 (no duplicated numbers).

- [x] **T003 — Coastal Dark + type scale + viz tokens.** `colors.css` gains the
  Coastal-dark warm ramp override and `--viz-1/2/rest` per mode; `typography.css`
  scale +1; `ThemeSelector` ships Coastal + Coastal Dark; `useTitleBarSync` derives
  overlay colours from computed custom properties and pushes them on theme change;
  Appearance settings copy updated. AC 4, 5, 7.

- [x] **T004 — Home as a two-column pane.** `Today.tsx` + `today.css` rebuilt: queue
  first (statement, evidence line derived from `metadata.episodes` +
  `established_at`, Keep / Dismiss), then the kept record as a dense list; context
  column with today's counts, the selectivity ratio, the budget meter, and composition
  by kind. `--text-sm` demoted out of content. AC 6.
  **Not built:** the *Show evidence* action from the mockup — it needs a per-episode
  fan-out over `GET /episodes/{id}`, or [v2-005](../005-app-read-api/specification.md)
  R3. Tracked as T009.

- [x] **T005 — Motion + glass.** Route cascade with travel direction (`--dir` from the
  previous route), sliding nav indicator (fixed-height items, pure-CSS offset — no
  layout measurement to go stale), condensing header on scroll with the page's own
  context fading in beside the crumb, card collapse on keep/dismiss so the queue heals
  itself, meter draw-in, theme cross-fade (ground split into `background-color` +
  `background-image`, since gradient stops can't transition), and the memory-landing
  animation. Glass restricted to floating surfaces. The two places that *wait* on an
  animation check `prefers-reduced-motion` in JS, not just CSS. AC 8.

- [ ] **T006 — Retire the techy axis (decision required).** Nocturne leaves the theme
  list in T003, but `[data-style="techy"]` remains defined across `typography.css`,
  `effects.css` and `spacing.css`, and Space Grotesk stays in the bundle via
  `ds-globals.ts`. Either strip the axis (smaller bundle, honest two-axis system) or
  keep it documented as reserved. **Human call — do not decide unilaterally.**

- [x] **T008 — Surface what Lore is watching.** Done in the renderer: `GET /activity`
  already carries `window_title`, so the live element shows the newest **`Captured`**
  row's title. `Filtered` / `Skipped` rows are discarded without display — echoing a
  blocklisted window's title into the always-visible rail would leak precisely what the
  blocklist protects. Moving that rule into the agent is [v2-005](../005-app-read-api/specification.md) R2.

- [ ] **T007 — Container query for the context column.** Home's context column
  currently collapses on a viewport media query; it should key off the card's width,
  since the rail consumes 236 px first. Swap to a container query once the shell is
  settled.

- [ ] **T009 — "Show evidence" on a queue card.** Expand a staged memory to the episodes
  that support it. Buildable now by fanning out over `GET /episodes/{id}` for each id in
  `metadata.episodes`; cheaper once [v2-005](../005-app-read-api/specification.md) R3
  lands. The evidence *line* already ships; this is the expansion.

## Not in this spec

Memory / Timeline / Settings keep the reading column (`.page--reading`) and are
rebuilt separately — Memory in particular wants a two-pane list+detail treatment that
also retires its record-detail `Dialog`. Mica and the Ctrl+K palette are likewise
their own work.
