# v2-003 — App Redesign · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> **Depends on:** v2-001 (memory model — the app renders its objects), v2-002
> (the old renderer is retired). Source of visual truth: the **Lore Design System**
> (claude.ai/design project `d94c444d-9feb-49b9-9d0a-5ae15c18d4a6`), synced into the
> repo per "Design-system intake" below.

## Overview

The v1 app is a sidebar-navigated dashboard — functional, generic, and at odds with
what Lore is. v2 replaces the renderer wholesale with the Lore Design System's
language: **calm + precise, single-focus, no sidebar.** The app is not a dashboard
the user works *in*; it is a quiet place they *visit* — to check what Lore has
learned, correct it, and leave. Electron + React 18 + TypeScript stand
(constitution §2); every screen is a client of the local API only (§3.1).

## Design-system intake

The design system is the visual source of truth and is **vendored into the repo**
(tokens, component sources, fonts, icon subset) rather than referenced at runtime:

- **Tokens & components:** `styles.css`, `tokens/*` and the React components
  (forms / feedback / surfaces / theming) are synced into the app's source tree and
  adapted to the app's build (TSX conversion permitted; visual output must match
  the system's showcase).
- **No runtime CDN — constitutional.** The design system as authored loads Lucide
  from unpkg and fonts from Google. The app **must** bundle fonts (Space Grotesk,
  Manrope, JetBrains Mono) and a vendored subset of Lucide icons locally. The
  packaged app makes **zero** outbound requests (constitution §4.3); this is
  release-gated (AC 8).
- **Divergence rule:** where app needs exceed the system (new composite
  components), new pieces are built *from the system's tokens and primitives* and
  contributed back into the synced library — never as one-off styled elements.

## Visual & voice contract

Binding rules from the design system, restated here because review enforces them:

- **Two themes, both shipped:** Coastal (minimal · light · default) and Nocturne
  (techy · dark) via the system's `data-palette`/`data-style`/`data-mode`
  attributes and `ThemeSelector`. First run follows the OS light/dark preference
  (light → Coastal, dark → Nocturne); the user's explicit choice persists after
  that. No custom third theme.
- **Layout:** centered single column (~1140 px max), sticky translucent blurred
  header, generous vertical rhythm. **No sidebar, no dense multi-pane dashboards.**
- **Type & motifs:** sentence case everywhere; mono uppercase eyebrows (sparingly,
  the `//` motif only on labels); numbers/data in mono. No emoji anywhere in the UI.
- **Voice:** Lore speaks in third person, calm and declarative ("Lore remembered
  2 things today"), never hype. Empty states are confident, not apologetic.
- **Motion:** system easings/durations only; no bounces, no infinite animation
  except the spinner; `prefers-reduced-motion` collapses durations.

## The screens

Navigation is a small set of header tabs (system `Tabs`), single view at a time.

1. **Today (home).** The answer to "what has Lore been doing?" in one calm column:
   agent/memoryd/capture status as a quiet strip (not a dashboard), the day's
   promoted memories, staged candidates awaiting evidence, and the day's capture
   economy in one mono line (episodes seen → distilled → staged → promoted, budget
   used). Primary action: review staged candidates.
2. **Memory (library).** The user's profile as Lore knows it: memories grouped by
   kind (`identity` / `preference` / `state` / `experience` / `project`), each a
   card with statement, kind tag, when-established, confidence, and provenance
   ("from 3 episodes — view"). Actions: edit, delete, pin (user authority per
   v2-001), confirm-or-expire prompts for `state` memories past horizon. Search
   and kind filters. A distinct **staging** section makes near-misses recoverable
   with one tap (v2-001 risk mitigation).
3. **Activity (decision trail).** The explainability surface: a chronological feed
   of episodes and decisions — captured / skipped (why) / filtered (which stage) /
   staged / promoted / budget-dropped — rendering v2-001's decision trail. This
   answers "why does/doesn't Lore know X?" and is where trust is won.
4. **Settings.** Provider & embedder (existing 004 flows re-skinned, including
   connection test), capture controls (blocklist editor, dwell/episode knobs,
   daily budget, pause capture), connections (one portable agent setup action with
   advanced MCP/REST pointers), and data (open data dir, export, delete-all with
   confirmation). Theme control lives in the rail.
5. **Onboarding (first run).** The v1 flow's contract survives with the new skin
   and one addition: privacy explainer → connect a model (key or URL, with test) →
   **what Lore remembers** (the v2 memory model in three sentences: facts not
   activity; skeptical by default; everything visible and editable in Memory) →
   connect a client (MCP install) → done. Skippable except the provider step.

## Scope

### In scope

- Design-system intake (vendored tokens/components/fonts/icons) and the app shell
  (header, tabs, theming, tray/window behavior transcribed from v1 main process).
- The five screens above against the v2 local API (v2-001 resources: memories,
  staging, episodes/decisions, metrics; surviving 004/005 resources: provider
  config, status, MCP install).
- Empty/loading/error states for every screen in the system voice — including the
  degraded state when the agent or memoryd is unreachable (app stays up, says so
  calmly, retries).
- Accessibility: keyboard navigable, visible focus rings (system `--ring`),
  `prefers-reduced-motion` honored, AA contrast in both themes.

### Out of scope

- New behavior: the app adds zero business logic (constitution §3.1) — every
  action is an existing local-API call defined by v2-001/004/005.
- The REST API's shape itself (v2-001 / surviving 005).
- The marketing site (`lore/web` is a separate property, not this repo).
- Mobile/companion apps; macOS/Linux chrome polish (Windows-first, not designed out).

## Acceptance criteria

1. **No sidebar exists.** Navigation is header tabs; every screen is a single
   centered column ≤ ~1140 px; the header is sticky with the system's blur.
2. Both themes render every screen correctly; first run follows OS mode; an
   explicit ThemeSelector choice persists across restarts; switching cross-fades
   per the system's motion rules.
3. All rendered color/type/spacing/radius/shadow values trace to system tokens —
   a stylelint/eslint gate rejects hard-coded values in renderer styles.
4. The Memory screen supports edit / delete / pin / confirm-expire round-trips to
   the local API; a pinned or edited memory visibly reflects its user-authority
   state; staging promote/dismiss is one tap.
5. The Activity feed renders every v2-001 decision type with its reason, and a
   memory's provenance link lands on its supporting episodes.
6. Onboarding completes end-to-end (privacy → provider test → memory explainer →
   MCP install) on a clean machine; every step but provider connect is skippable;
   re-running is available from Settings.
7. UI copy audit passes: sentence case, no emoji, no first-person Lore, eyebrows
   only as specified — enforced by a checklist in review, spot-checked per PR.
8. **Zero outbound requests:** with capture running and every screen visited, the
   packaged app's process makes no network connections except `127.0.0.1` (and the
   user's configured model endpoints from the agent process). Verified with a
   network trace as a release gate; fonts and icons load from the bundle offline.
9. With agent/memoryd stopped, every screen shows its calm degraded state and
   recovers without restart when the process returns.

## Non-functional requirements

- **Perceived calm is a feature:** initial window ≤ 2 s to interactive on the
  reference machine; no layout shift after first paint; skeletons over spinners
  for list loads.
- **Renderer discipline:** all data via the typed local-API client; no Node
  integration in the renderer beyond the existing preload IPC surface (open-path,
  login-item, window controls).
- **Bundle hygiene:** vendored icon subset only (no full icon pack), fonts
  subsetted to used weights; app package size budget set in `plan.md`.

## Risks

- *Design-system drift:* the claude.ai project evolves and the vendored copy
  rots. Mitigation: intake is a documented `/design-sync` procedure with a synced
  manifest; re-sync is a routine PR, and AC 3's token gate catches local forks.
- *"No dashboard" fights power users* who want dense views. Mitigation: the
  Activity feed carries the density; the design bet is deliberate and revisitable
  after launch feedback, not hedged now.
- *A calm UI hides real failures.* Mitigation: degraded states are explicit (AC 9)
  and status is always one glance away on Today; calm ≠ silent.
