# 010 — Desktop App · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 005 (the only
> API the app talks to), 004 (provider settings), 002 (memory engine setting), 006
> (Connect screen surfaces MCP setup), 007 (Connect surfaces CLI/skill installs),
> 009 (import UI, if shipped).

## Overview

The desktop app is Lore's human face — the one surface a user actually looks at. It
is an Electron + React 18 shell that is a **pure client of the local API (005)** and
is built entirely on the **Lore Design System** (the `claude.ai/design` handoff
bundle: brand voice, two themes, and ~20 React components). Its job is to make
ambient capture feel calm and trustworthy: explain plainly what Lore watches and
filters, let the user bring their own model, browse and correct what Lore remembers,
and connect their AI tools — with no account, no telemetry, and nothing leaving the
machine except the model calls the user configured.

This spec replaces the earlier placeholder now that the design direction is settled.

## Design source of truth

The **Lore Design System** is non-negotiable as the visual and voice authority. It
is delivered as a handoff bundle from `claude.ai/design`:

- **Bundle download:** <https://api.anthropic.com/v1/design/h/kbIrOzMa_vuwiPxrWpf8Wg>
  (a gzip tarball — extract it; root is `lore-design-system/`, the project lives
  under `lore-design-system/project/`). Read `project/readme.md` and `project/SKILL.md`
  first.
- **Namespace:** `LoreDesignSystem_d94c44`. The bundle must be **vendored into the
  repo** during T001 so the build is self-contained and does not depend on the link
  at build time.

Its authority:

- **Two themes, locked:** **Coastal** (default — Minimal, Light, Cerulean `#0081AF`
  / Coral `#F76C5E`, Manrope, soft pill corners) and **Nocturne** (Techy, Dark, Yale
  Blue `#344966` / Bronze `#C9A74D`, Space Grotesk, crisp corners). Switched via
  `<ThemeSelector>` / `data-*` on `<html>`; persisted.
- **Components:** compose the system's primitives (`Button`, `IconButton`, `Input`,
  `Select`, `Checkbox`, `Switch`, `Card`, `Tabs`, `Dialog`, `Badge`, `Tag`,
  `Toast`, `Tooltip`, `ProgressBar`, `Spinner`, `Avatar`, `Divider`). **Do not
  re-implement** these — vendor and use them.
- **Voice:** calm + precise. Speak to the user as "you", the product as "Lore" in
  third person. Sentence case everywhere; the one uppercase moment is mono `//`
  eyebrows. **No emoji.** Ambient verbs (listen, surface, thread, recall, keep
  warm). Numbers in mono with units/arrows (`latency → 42ms`).
- **Iconography:** Lucide via the system's convention, always `currentColor`.
- **Motion:** smooth & subtle; respect `prefers-reduced-motion`.

## User stories

- **As a first-time user**, the app explains in plain language what Lore captures
  and filters before it asks for anything, so I can decide to trust it.
- **As any user**, I set up my own model (a key or a local URL), confirm it works,
  and never see an account or login.
- **As a curious user**, I can see exactly what Lore has captured and why it skipped
  or filtered things — and delete anything — so the system is legible, not a black box.
- **As a user**, I can search and edit my memory, manually add a fact, and import a
  document to seed it.
- **As a user**, I can connect Claude Desktop / Claude Code / Cursor in a couple of
  clicks via the Connect screen.
- **As a returning user**, the app sits quietly and tells me at a glance that Lore is
  listening and what it has kept warm lately.

## The page set

| Page | Purpose | Key components | API (005) |
|---|---|---|---|
| **Onboarding** (first-run, multi-step) | Welcome → how Lore works & privacy → connect your model (+ test) → tune capture (blocklist) → done | `Card`, `Button`, `Input`, `Select`, `Switch`, `ProgressBar`, `ThemeSelector` | `GET/PATCH /config`, `POST /providers/test` |
| **Home** | Ambient status: is Lore listening, recent highlights, terse stats (memories, latency) | `Card`, `Badge`, `Tag`, `Divider` | `GET /system/status`, `GET /recent`, `GET /memories` |
| **Library** | Search-first browse of all memories; view/edit/delete | `Input` (search), `Card`, `Tag`, `Dialog`, `IconButton` | `GET /memories`, `POST /memories/search`, `PATCH/DELETE /memories/{id}` |
| **Activity** | Transparency timeline: what Lore saw and decided (captured / skipped / filtered + reason) | `Tabs`, `Card`, `Badge`, `Tooltip` | `GET /activity`, `GET /recent` |
| **Connect** | Wire up AI tools: MCP (Claude Desktop/Code/Cursor), CLI, skill | `Tabs`, `Card`, `Button`, `Tooltip` (copy) | `GET /system/status`; install help text (006/007) |
| **Add** | Manually add a memory; import a document | `Textarea`, `Select`, `Button`, `ProgressBar` (import) | `POST /memories`, `POST /import` (009) |
| **Settings** | Model & Memory · Capture & Privacy (blocklist) · Appearance (theme) · Data (export/reset) · About | `Tabs`, `Input`, `Select`, `Switch`, `Checkbox`, `ThemeSelector`, `Dialog` | `GET/PATCH /config`, `POST /providers/test`, `GET /export/*`, `DELETE /system/data` |

Persistent chrome: a left **sidebar** nav + a **sticky header** carrying an ambient
"Lore is listening / paused" status indicator (the design system's blurred sticky
ground).

## Scope

### In scope

- The seven pages above + app chrome, built on the design system, talking only to 005.
- First-run onboarding with a trust-first privacy explainer and BYO-model setup.
- Theme support (Coastal default, Nocturne via selector, persisted); reduced-motion.
- Reuse of the v1 Electron shell (main process, tray, agent spawn) where it still
  fits; deletion of all account/sync/cloud-review UI.
- A thin renderer API client (`api.ts`) as the single path to 005.

### Out of scope

- Any behavior not exposed by 005 — the renderer holds **no** business logic.
- Account/auth/login, cloud sync, hosted inference, analytics, any direct mem0/model
  access (constitution §1–§3). v1's `Auth.tsx`, `Review.tsx` (cloud), `Compact.tsx`
  are deleted, not ported.
- Buckets/retention UI (deferred features).
- Authoring the design system (it is consumed, not built) and the MCP/CLI/skill
  internals (006/007 — Connect only surfaces them).

## Acceptance criteria

1. Every screen is rendered with design-system components and tokens; a bare app
   renders **Coastal**, and the `ThemeSelector` switches to **Nocturne** and persists.
2. The renderer talks **only** to `127.0.0.1:7842` via `api.ts`; a check confirms no
   direct mem0/model/network calls from the renderer.
3. First-run onboarding shows the privacy explainer **before** requesting any input,
   completes a successful `POST /providers/test`, seeds a blocklist, and marks
   `onboarding.completed`.
4. No account/auth/login surface exists anywhere in the app.
5. Library searches, views, edits, and deletes memories through 005; Activity shows
   captured/skipped/filtered decisions with reasons.
6. Connect produces working setup for Claude Desktop (and at least Claude Code +
   Cursor), consistent with 006/007.
7. Settings changes provider/model (with test), capture/blocklist, theme, and
   exports/resets data — all via 005, never echoing secrets.
8. All copy follows the design-system voice (sentence case, mono `//` eyebrows, no
   emoji); motion respects `prefers-reduced-motion`.

## Non-functional requirements

- **Thin client:** zero business logic in the renderer (constitution §3.1).
- **Trust-first:** onboarding earns consent before capture; the Activity + Library
  surfaces make the system legible and correctable.
- **Accessible:** keyboard-navigable, focus rings (the system's `--ring`), adequate
  contrast in both themes, reduced-motion honored.
- **Resilient:** when the agent/API is down, the app shows a calm "Lore isn't
  running" state, not errors.
- **No secrets in renderer state or logs:** keys are submitted to 005 and stored in
  the keystore (004); never held in renderer state.

## Open questions (to iterate before/while building)

These are intentionally unresolved and will be decided with the owner:

- **Local "review before remembering" mode** — should the app offer an optional
  local queue where captures await approval before storage (reviving v1's Review UI
  as a *local*, no-cloud gate)? Default for v1 is **live capture + a transparent
  Activity view** (see and delete anything); a local review queue is a candidate
  enhancement, not assumed.
- **Home depth** — minimal ambient status vs. a richer "what Lore learned this week"
  digest.
- **What's New** — a small in-app release-notes surface (modal) vs. deferring to docs.

## Risks

- *Renderer accretes logic over time.* Mitigation: `api.ts` is the only outbound
  path; a test asserts no other network/IPC business calls (acceptance criterion 2).
- *Design-system drift if components are copied and edited.* Mitigation: vendor the
  bundle and consume components unmodified; restyle via tokens, not forks.
- *Onboarding feels like a permission wall.* Mitigation: lead with plain-language
  value + privacy, in the brand's calm voice, before any field.
