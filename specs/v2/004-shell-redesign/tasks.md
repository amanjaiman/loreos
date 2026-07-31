# v2-004 — Shell Redesign · Tasks

> Atomic, dependency-ordered, one PR each (~1–3 h), every task ends at a green
> `no-mistakes` gate (constitution §7). See [`plan.md`](plan.md) for designs.

- [ ] **T001 — Frameless window.** `titleBarStyle: 'hidden'` + `titleBarOverlay`
  (never `frame: false`, so Snap Layouts survive); application menu replaced with a
  template whose top-level items are `visible: false`, preserving the clipboard and
  undo accelerators on Windows; `lore:set-titlebar` IPC (main clamps to two hex
  strings) and the `setTitleBar` preload bridge. AC 1, 2, 3.

- [ ] **T002 — Shell: ground, strip, rail, card.** New `Rail.tsx` +
  `LiveElement.tsx`, `AppShell.tsx` rewritten to the four-area grid, `Header.tsx`
  deleted, `chrome.css` rewritten (per-corner radius vars, drag regions, the sticky
  card header carrying its own top radii). Staged count on the Memory nav item.
  AC 1, 6 (no duplicated numbers).

- [ ] **T003 — Coastal Dark + type scale + viz tokens.** `colors.css` gains the
  Coastal-dark warm ramp override and `--viz-1/2/rest` per mode; `typography.css`
  scale +1; `ThemeSelector` ships Coastal + Coastal Dark; `useTitleBarSync` derives
  overlay colours from computed custom properties and pushes them on theme change;
  Appearance settings copy updated. AC 4, 5, 7.

- [ ] **T004 — Home as a two-column pane.** `Today.tsx` + `today.css` rebuilt: queue
  first (statement, evidence line, Keep / Dismiss / Show evidence), then the kept
  record as a dense list; context column with today's counts, the selectivity ratio,
  the budget meter, and composition by kind. `--text-sm` demoted out of content across
  the touched views. AC 6.

- [ ] **T005 — Motion + glass.** Route cascade with travel direction, sliding nav
  indicator, condensing header, row collapse on keep/dismiss, meter draw-in, theme
  cross-fade, and the memory-landing animation. Glass restricted to floating surfaces.
  `prefers-reduced-motion` collapses everything. AC 8.

- [ ] **T006 — Retire the techy axis (decision required).** Nocturne leaves the theme
  list in T003, but `[data-style="techy"]` remains defined across `typography.css`,
  `effects.css` and `spacing.css`, and Space Grotesk stays in the bundle via
  `ds-globals.ts`. Either strip the axis (smaller bundle, honest two-axis system) or
  keep it documented as reserved. **Human call — do not decide unilaterally.**

- [ ] **T008 — Surface what Lore is watching.** The rail's live element currently
  reports capture *state* ("Watching the active window") because the local API exposes
  no active-window field — `GET /system/status` carries component health only. Showing
  the real target is the difference between an indicator and presence. Needs an agent +
  API change (a redacted, blocklist-respecting current-window title), so it is its own
  spec-level decision rather than a renderer task.

- [ ] **T007 — Container query for the context column.** Home's context column
  currently collapses on a viewport media query; it should key off the card's width,
  since the rail consumes 236 px first. Swap to a container query once the shell is
  settled.

## Not in this spec

Memory / Timeline / Settings keep the reading column (`.page--reading`) and are
rebuilt separately — Memory in particular wants a two-pane list+detail treatment that
also retires its record-detail `Dialog`. Mica and the Ctrl+K palette are likewise
their own work.
