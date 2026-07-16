# 010 — Desktop App · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Keep v1's Electron + React + TypeScript shell where it still serves, **vendor the
Lore Design System** as the visual/voice layer, and rebuild the views as thin
compositions over the local API (005). Delete every account/sync/cloud surface. The
renderer is a presentation layer: all data and actions go through one `api.ts`
client; nothing in `renderer/` makes a network call elsewhere or holds business logic.

## Design-system integration

The design system is a handoff bundle from `claude.ai/design` (the **Lore Design
System**: namespace `LoreDesignSystem_d94c44`, two themes Coastal/Nocturne, ~20
React components that depend only on React + CSS custom properties).

- **Obtain it:** download the bundle from
  <https://api.anthropic.com/v1/design/h/kbIrOzMa_vuwiPxrWpf8Wg> (gzip tarball;
  extract → `lore-design-system/project/`). Read `project/readme.md` (brand voice,
  themes, visual foundations) and `project/SKILL.md` before building. If the link is
  unavailable, the same bundle can be re-exported from the Lore project at
  `claude.ai/design`.

- **Vendor it** into the app at `app/src/renderer/design-system/` (tokens,
  `styles.css`, `assets/`, `components/`). Consume components by their named exports;
  their sibling `.d.ts` files provide types. **Components are used unmodified** —
  customization happens through tokens, never by forking a component.
- **Link `styles.css` once** at the renderer entry; a bare app renders **Coastal**.
- **Theme** via `<ThemeSelector>` (in Settings → Appearance); it sets `data-*` on
  `<html>` and persists. Coastal→Light and Nocturne→Dark are locked pairings.
- **Voice** is enforced in copy review (a checklist task): sentence case, mono `//`
  eyebrows, no emoji, ambient verbs, mono numerics with units.
- **Icons** via the system's Lucide convention, `currentColor`.

## App structure

```
app/src/
├── main/                       # Electron main (reused, trimmed)
│   ├── main.ts                 # spawn/supervise agent, tray, window, IPC (open-path, etc.)
│   └── ...                     # NO auth/sync IPC
└── renderer/
    ├── index.tsx · App.tsx     # entry; mounts router + theme; links design-system styles.css
    ├── api.ts                  # the ONLY client of the local API (005)
    ├── design-system/          # vendored Lore Design System (unmodified components + tokens)
    ├── chrome/                 # Sidebar, Header (ambient status), AppShell
    ├── views/
    │   ├── onboarding/         # Welcome, Privacy, ConnectModel, TuneCapture, Done (steps)
    │   ├── Home.tsx
    │   ├── Library.tsx
    │   ├── Activity.tsx
    │   ├── Connect.tsx
    │   ├── Add.tsx
    │   └── settings/           # ModelMemory, CapturePrivacy, Appearance, Data, About
    └── lib/                    # formatting, hooks (useStatus, useMemories) — view-only helpers
```

Deleted from v1: `Auth.tsx`, `Review.tsx` (cloud), `Compact.tsx`, and all
auth/sync code in `api.ts` and `main.ts`.

## `api.ts` — the single seam to 005

A typed client mirroring the 005 OpenAPI contract: memories (list/search/get/add/
update/delete), recent/activity, config (get/patch — secrets submitted, never
returned), providers/test, system status/log/reset, export, import (009). Every view
calls this; no view constructs a URL or fetch itself. A connection-refused result
surfaces the calm "Lore isn't running" state.

## Per-page plan

- **Onboarding** (multi-step `Card` flow, `ProgressBar` for steps):
  1. *Welcome* — brand statement ("Memory, kept warm."), what Lore is, calmly.
  2. *How Lore works & privacy* — plain-language: what's captured (active window
     text), the three filter layers, that it's all local, the single egress (your
     model). **Shown before any input.**
  3. *Connect your model* — provider `Select` (Anthropic / OpenAI / Gemini /
     OpenAI-compatible) + key `Input` or base-URL `Input`; **Test** button →
     `POST /providers/test`, showing model + `latency → Nms` or an actionable error.
  4. *Tune capture* — seed the blocklist (apps/keywords) with sensible defaults; a
     `Switch` to start paused or listening.
  5. *Done* — confirm; offer a jump to Connect. Sets `onboarding.completed`.
- **Home** — ambient status `Card` (listening/paused `Badge`), recent highlights from
  `GET /recent`, terse stats (memory count, last capture, latency) in mono. Calm,
  single-focus, no dense dashboard.
- **Library** — search `Input` first; results as `Card`s with category `Tag`s; click
  → `Dialog` to view/edit/delete (`PATCH`/`DELETE`). Flat + search (buckets deferred);
  optional category facets.
- **Activity** — `Tabs` (All / Captured / Skipped / Filtered); each row shows the
  window, decision `Badge`, and reason `Tooltip`. This is the legibility surface —
  filtered items show *that* they were filtered and why, never the sensitive content.
- **Connect** — `Tabs` (Claude Desktop / Claude Code / Cursor / CLI): copy-paste
  config blocks (consistent with 006), a one-click "install" affordance where the CLI
  supports it (007's `lore mcp install` / `lore skills install`), and a live
  "connected?" hint from status.
- **Add** — `Textarea` + category `Select` → `POST /memories`; a document picker →
  `POST /import` with a `ProgressBar` (009; hidden/disabled if 009 not shipped).
- **Settings** — `Tabs`:
  - *Model & Memory* — provider config (+ Test), memory engine (embedded / remote URL).
  - *Capture & Privacy* — blocklist editor (apps + keywords), capture toggles, dwell.
  - *Appearance* — `<ThemeSelector>` (Coastal / Nocturne).
  - *Data* — export JSON/Markdown, reset all data (`Dialog` confirm).
  - *About* — version, links to privacy/docs.

## State & data flow

View-local state only (search text, dialog open, form drafts). Server state via small
read hooks over `api.ts` (`useStatus`, `useMemories`, `useActivity`) with simple
polling for status. No global store of business data; the API is the source of truth.

## Decisions

- **Vendor the design system, consume components unmodified** — restyle via tokens;
  forking a component is a design-system change, not an app change.
- **Coastal default, Nocturne via selector** — matches the locked two-theme model
  from the design handoff.
- **Renderer is presentation-only; `api.ts` is the one seam** — upholds the
  one-behavior-layer invariant on the client side too.
- **Live capture + transparent Activity for v1** — a local review-queue mode is an
  open question (spec §"Open questions"), not built unless chosen.
- **Reuse, don't rebuild, the Electron main** — keep agent spawn/tray/window IPC;
  strip account/sync.

## Dependencies & order

Upstream: **005** (the API; everything reads it), **004** (provider settings/test),
**002** (memory engine), **006/007** (Connect surfaces their setup), **009** (import
UI, optional). Internal order: vendor DS + shell + theming → `api.ts` → onboarding →
Home → Library → Activity → Connect → Add/import → Settings → accessibility + voice
polish. See [`tasks.md`](tasks.md).
