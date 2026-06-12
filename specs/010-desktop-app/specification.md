# 010 — Desktop App · Specification  🚧 PLACEHOLDER

> **This spec is intentionally a placeholder.** The desktop app's design is being
> thought through separately and will be filled in collaboratively before any of
> its tasks begin. Do **not** start implementation from this file. Bound by
> [`constitution.md`](../../constitution.md). **Depends on:** 005 (the only API the
> app talks to), 004 (provider settings), 009 (import UI, if shipped).

## Why this is a placeholder

The Electron app is the most design-sensitive surface — onboarding sets the trust
tone for software that watches your screen, and the library/settings define the
day-to-day experience. The project owner wants to iterate on this deliberately
rather than lock a spec now. The other specs (001–009, 011) are designed so the app
is a **pure client of the local API (005)** and can be specified later without
blocking them.

## Known guardrails (won't change)

These hold regardless of the final design and constrain it:

- **Thin client of 005.** The app talks **only** to the local API on
  `127.0.0.1:7842` — never to mem0, models, or any network service directly
  (constitution §3.1). No behavior lives in the renderer.
- **No account, ever.** There is no auth, signup, or login surface. v1's `Auth.tsx`
  and all account UI are deleted, not ported (constitution §1).
- **No telemetry.** The app sends no usage data anywhere (constitution §2).
- **Trust-first onboarding.** First-run must clearly explain what is captured, the
  filter layers, and that everything is local — before asking for anything.
- **BYO-model setup.** Onboarding/settings collect the provider config and call
  `POST /providers/test` (004); keys go to the OS keystore via the API, never into
  renderer state or logs.
- **Reuse the shell, not the flows.** v1's Electron shell and the Home/Library/
  Recent/Settings views are a starting point; the account/sync/review flows
  (`Auth.tsx`, `Review.tsx`, `Compact.tsx`) are removed.

## Open design questions (to resolve together)

- Onboarding shape and copy — how we earn trust in the first 60 seconds.
- Whether a local "review before remembering" mode returns (revives v1's Review UI
  as a *local* gate, no cloud) or memory is always live.
- Library/Recent presentation now that buckets are deferred — flat list, search-first,
  category facets?
- How much of Settings is provider/model config vs. capture tuning vs. blocklist
  management.
- Visual direction for README-worthy screenshots.

## Placeholder scope

- **In scope (eventually):** onboarding, library/search, recent/activity view,
  settings (provider, capture, blocklist, memory engine), import UI (if 009 ships),
  data export/reset — all over 005.
- **Out of scope (always):** auth/accounts, cloud sync, hosted inference,
  analytics, any direct mem0/model access.

## Acceptance criteria

_To be defined when this spec is filled in._ At minimum it will assert: app talks
only to 005, no account surface exists, onboarding completes a provider setup with a
successful `/providers/test`, and the library reads/searches/edits memory through the
API.

---

_Next step: a working session to turn this placeholder into a full
specification/plan/tasks triplet like the others._
