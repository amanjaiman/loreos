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
  by kind — the last from v2-005 R1's counts endpoint rather than a 500-row fetch.
  `--text-sm` demoted out of content. AC 6.

- [x] **T005 — Motion + glass.** Route cascade with travel direction (`--dir` from the
  previous route), sliding nav indicator (fixed-height items, pure-CSS offset — no
  layout measurement to go stale), condensing header on scroll with the page's own
  context fading in beside the crumb, card collapse on keep/dismiss so the queue heals
  itself, meter draw-in, theme cross-fade (ground split into `background-color` +
  `background-image`, since gradient stops can't transition), and the memory-landing
  animation. Glass restricted to floating surfaces. The two places that *wait* on an
  animation check `prefers-reduced-motion` in JS, not just CSS. AC 8.

- [x] **T006 — Retire the techy axis.** Decided: stripped. `[data-style="techy"]`
  removed from `typography.css` / `effects.css` / `spacing.css`, the Nocturne palette
  block removed from `colors.css` (its dark mapping merged into the single
  `[data-mode="dark"]` rule), Space Grotesk dropped from `ds-globals.ts` and from
  `package.json`, and the design-system readme/SKILL/styles docs updated. The system is
  now honestly one palette + two modes. Recoverable from git if a third theme is wanted.

- [x] **T008 — Surface what Lore is watching.** The live element reads
  `status.capture.window_title` from [v2-005](../005-app-read-api/specification.md) R2.
  An earlier attempt derived it from `GET /activity`, filtering to `Captured` rows —
  that was **dead on arrival**: `CaptureAgent` only ever logs `Filtered`, so the field
  was always null. The agent's tracker is what makes the feature real, and it also puts
  the redaction rule next to the filter chain instead of in the client.

- [x] **T007 — Container query for the context column.** `.app-card__scroll` declares
  `container-name: pane`; Home's context column now collapses at
  `@container pane (max-width: 820px)` instead of a viewport breakpoint that fired at
  the wrong moment because the rail consumes 236 px first.

- [x] **T009 — "Show evidence" on a queue card.** Expands a staged memory to the
  episodes behind it (app, window title, when), fetched on demand from v2-005 R3's
  `GET /memories/{id}/evidence`. Only rendered when the memory actually has episodes.

## Not in this spec

Memory / Timeline / Settings keep the reading column (`.page--reading`) and are
rebuilt separately — Memory in particular wants a two-pane list+detail treatment that
also retires its record-detail `Dialog`. Mica and the Ctrl+K palette are likewise
their own work.
