# v2-004 — Shell Redesign · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> **Depends on:** v2-003 (the design system is vendored and the five screens exist).
> **Supersedes:** v2-003's "Layout" and "Two themes" clauses only. Everything else in
> v2-003 — voice, vendoring, zero egress, the screen inventory — stands unamended.

## Overview

v2-003 gave Lore a correct visual language and the wrong container for it. The app
ships the OS title bar, Electron's default File/Edit/View menu, a horizontal tab
header, and a ~760 px centred reading column. The tokens are right; the layout reads
as a document rendered inside a generic desktop frame.

v2-004 replaces the shell and the Home pane. The window becomes **frameless**, the
ground plane becomes the app, content floats on it in an inset card, navigation moves
to a **left rail** that also carries Lore's ambient presence, and Home becomes a
**two-column app pane** led by the work that needs the user.

This is a presentation-layer spec. No agent, memoryd, API, or capture behaviour
changes. The renderer stays a pure client of the local API (constitution §3.1).

## Why the current shell fails

Recorded because review enforces the fix, not the taste:

1. **The frame isn't ours.** An OS title bar plus a File/Edit/View menu bar is two
   rows of chrome that belong to no one and say nothing.
2. **Navigation has no home.** Centre-aligned header pills give the four destinations
   equal, transient weight and leave nowhere for persistent state to live.
3. **The content is a reading column in a wide pane.** Centred ~760 px with wide empty
   margins on either side, stacked as `heading → hairline rule → list`, repeated. That
   is the shape of rendered markdown; typography carries the screen and layout carries
   nothing.
4. **Priority is inverted.** Home leads with summary counts. The only thing on it that
   requires the user — staged memories awaiting judgment — is last.

## The shell contract

- **Frameless window.** No OS title bar, no application menu bar. A 44 px top strip of
  ground is the drag surface; the native window controls sit in it.
- **Ground and card.** The window background is one continuous ground plane. Content
  lives in a rounded card (`--radius-2xl`) inset from the right and bottom edges, below
  the top strip. The ground is visible on all four sides of the card.
- **Left rail (236 px), on the ground.** Wordmark, search entry (Ctrl+K), the four
  destinations with a staged count on Memory, and — pinned low — Lore's ambient
  presence: the live element (what is being watched right now, with a pause control)
  and the trace of the last three memories kept.
- **Rail is presence, pane is data.** The rail never reports numbers the content pane
  also reports. Capture state and what's being watched live in the rail; counts,
  budget and composition live in the pane.

## The Home contract

- **Full width, two columns:** `minmax(0, 1fr)` work column + 262 px context column.
  No centred reading column. Long-form routes may opt into a column explicitly.
- **The queue leads.** Staged memories awaiting judgment are first, as cards carrying
  the statement, an evidence line ("seen twice today in Figma and VS Code · first
  noticed 28 July"), and Keep / Dismiss / Show evidence.
- **Left is where you act; right is where you check.** Nothing in the context column
  is operable. It carries today's counts, the selectivity ratio, the memory budget,
  and the composition of what Lore knows by kind.
- **Two section-header treatments, not one repeated rule.** A display-weight title
  with a count chip for the queue; a quiet mono label with an underline for the record.

## Visual additions to the design system

- **Coastal Dark replaces Nocturne** as the shipped second theme. Only *mode* flips:
  palette stays Coastal, style stays Minimal, Manrope and the soft shadows are kept.
  The neutral ramp is Coastal's own warm family extended downward (hue held near 38°,
  ground `#17130e`) rather than Nocturne's cool Ink Black. Nocturne is removed from
  the theme list; its palette/style CSS stays in the vendored library pending a
  separate decision (T006).
- **Type scale +1 across the working range.** `--text-base` 15→16, `--text-sm` 13→14,
  `--text-2xs` 11→12, and the mid scale with them. Separately, `--text-sm` stops being
  used for content — it is reserved for controls.
- **Explicit `--viz-*` tokens.** Chart colours are not `--primary`. Coastal Dark's
  lifted primary sits outside the dark-mode OKLCH lightness band, so each mode carries
  its own validated pair: light `#0081af` / `#f76c5e`, dark `#2499c2` / `#df6250`, with
  a neutral `--viz-rest` for the de-emphasised remainder.

## Acceptance criteria

1. The packaged app shows no OS title bar and no application menu bar; the window can
   be dragged by the top strip and by the rail's wordmark row.
2. Windows **Snap Layouts** still work on hover over Maximize (i.e. the implementation
   uses `titleBarStyle: 'hidden'` + `titleBarOverlay`, never `frame: false`).
3. Cut / copy / paste / select-all / undo keyboard accelerators work in every text
   input on Windows despite no visible menu bar.
4. The window-control overlay recolours when the theme changes; controls remain legible
   in both themes.
5. Both themes ship and are switchable: Coastal (light) and Coastal Dark. Neither
   changes typeface or shadow character — only the ground.
6. Home renders as two columns at the default window size, leads with the staged queue,
   and duplicates no number between rail and pane.
7. Every chart/meter colour resolves from `--viz-*`; the categorical pair passes the
   colourblind-safety checks in **both** modes (recorded in `plan.md`).
8. `prefers-reduced-motion` collapses all added motion, including the route transition,
   the nav indicator, and the memory-landing animation.
9. Zero outbound requests from the renderer (v2-003 AC 8 re-asserted — the shell work
   must not introduce a CDN font, icon, or script).

## Out of scope

- Rebuilding **Memory, Timeline and Settings** panes to the two-column contract. They
  keep the reading column for now; Memory's two-pane treatment is its own spec.
- Windows 11 **Mica** (`backgroundMaterial`). Prototyped separately; it interacts with
  window transparency and is not required by this spec.
- Command palette behaviour behind Ctrl+K (the rail carries the entry point; the
  palette itself is a later task).
- Any change to capture, distillation, recall, storage, or the local API.
