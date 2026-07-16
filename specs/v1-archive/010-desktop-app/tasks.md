# 010 — Desktop App · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites. UI tasks include a copy pass against
> the design-system voice (sentence case, mono `//` eyebrows, no emoji).

---

### T001 — Vendor the design system + theming  `[deps: spec 005]`
Download the Lore Design System bundle from
<https://api.anthropic.com/v1/design/h/kbIrOzMa_vuwiPxrWpf8Wg> (gzip tarball; extract
→ `lore-design-system/project/`) and vendor it into `app/src/renderer/design-system/`
(tokens, `styles.css`, `assets/`, components — **unmodified**). Link `styles.css` at
the renderer entry; mount `<ThemeSelector>`. Confirm a bare app renders **Coastal**
and switching to **Nocturne** persists.
**Done when:** components render with system tokens; Coastal default + Nocturne
switch work and persist; reduced-motion is honored (acceptance criterion 1).

### T002 — App shell, sidebar, ambient header  `[deps: T001]`
Build `AppShell` with the sidebar nav and the sticky header (blurred ground) carrying
a "listening / paused" status `Badge`. Trim v1 Electron main to agent spawn + tray +
window IPC; delete account/sync IPC.
**Done when:** the shell navigates between (placeholder) pages; the header reflects
status; no account/sync IPC remains.

### T003 — `api.ts` client of 005  `[deps: spec 005]`
Implement the typed `api.ts` mirroring the 005 OpenAPI contract (memories, recent/
activity, config, providers/test, system, export, import). Add the connection-refused
→ "Lore isn't running" handling. Strip all auth/sync calls.
**Done when:** every endpoint is reachable through `api.ts`; a test/lint check
asserts the renderer makes **no** network/business call outside `api.ts` (acceptance
criterion 2).

### T004 — Onboarding flow  `[deps: T002, T003, spec 004]`
Build the multi-step onboarding: Welcome → How Lore works & privacy → Connect your
model (provider `Select` + key/URL + **Test** via `POST /providers/test`) → Tune
capture (blocklist seed + listen/paused) → Done. Privacy explainer renders **before**
any input. Sets `onboarding.completed`.
**Done when:** first run shows privacy before input, completes a successful provider
test, seeds a blocklist, and marks onboarding complete (acceptance criterion 3); no
account surface appears (acceptance criterion 4).

### T005 — Home  `[deps: T002, T003]`
Build Home: ambient status, recent highlights (`GET /recent`), terse mono stats
(memory count, last capture, latency). Calm, single-focus.
**Done when:** Home reflects live status and recent memories; copy follows the voice.

### T006 — Library  `[deps: T003]`
Build Library: search-first (`POST /memories/search`), results as `Card`s with
category `Tag`s, view/edit/delete via `Dialog` (`PATCH`/`DELETE`).
**Done when:** search/view/edit/delete all round-trip through 005 (acceptance
criterion 5, library half).

### T007 — Activity (transparency)  `[deps: T003]`
Build Activity: `Tabs` (All/Captured/Skipped/Filtered) over `GET /activity`; each row
shows window + decision `Badge` + reason `Tooltip`. Filtered rows show *that*/*why*,
never the sensitive content.
**Done when:** decisions and reasons display correctly; filtered content is never
revealed (acceptance criterion 5, activity half).

### T008 — Connect  `[deps: T003, spec 006, spec 007]`
Build Connect: `Tabs` for Claude Desktop / Claude Code / Cursor / CLI with copy-paste
config (consistent with 006) and one-click installs where 007 supports them; a live
"connected?" hint from status.
**Done when:** Claude Desktop + at least Claude Code and Cursor have working setup
paths (acceptance criterion 6).

### T009 — Add + document import  `[deps: T003, spec 009]`
Build Add: manual memory (`Textarea` + category → `POST /memories`) and document
import (`POST /import` + `ProgressBar`). Import UI is hidden/disabled if 009 isn't
shipped.
**Done when:** manual add works; import (if 009 present) runs with progress.

### T010 — Settings  `[deps: T003, spec 004, spec 002]`
Build Settings `Tabs`: Model & Memory (provider + Test, memory engine), Capture &
Privacy (blocklist, toggles), Appearance (`ThemeSelector`), Data (export/reset via
`Dialog`), About.
**Done when:** each section reads/writes via 005, never echoes secrets, and the
theme/export/reset work (acceptance criterion 7).

### T011 — Accessibility, voice & resilience polish  `[deps: T004–T010]`
Keyboard nav + focus rings in both themes, contrast check, reduced-motion sweep, the
"Lore isn't running" state across views, and a final copy pass against the voice
guide.
**Done when:** the app is keyboard-navigable and readable in both themes, degrades
calmly when the agent is down, and all copy follows the design-system voice
(acceptance criterion 8).

---

## Definition of done for spec 010

Every page is built on the Lore Design System, talks only to the local API, and
upholds the guardrails (no account, no telemetry, trust-first onboarding, BYO model);
both themes work; the app is accessible and degrades gracefully. Lore has a human
face ready for packaging (011).
