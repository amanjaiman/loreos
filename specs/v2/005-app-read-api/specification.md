# v2-005 — App Read API · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> **Audience:** an agent implementing the C# agent + local API side.
> **Consumer:** the v2-004 shell, already merged. Nothing here blocks it — the renderer
> works today by deriving these values client-side. This spec removes the derivations.

## Why this exists

v2-004 rebuilt the desktop shell. Three of its surfaces need data the API does not
expose directly, so the renderer currently derives them by over-fetching. Each item
below states **what the renderer does today**, so you can see exactly what you are
replacing and verify the change end-to-end.

This is a **read-only, additive** spec. No existing endpoint changes shape. No capture,
distillation, lifecycle, or storage behaviour changes.

---

## R1 — Memory counts (`GET /memories/stats`)

**Today:** `Today.tsx` calls `GET /memories?status=active&limit=500` and counts kinds in
the browser, purely to draw a five-row composition bar. It also calls
`GET /memories?status=staged&limit=50` and reads `items.length` for the rail's badge —
which silently under-reports past 50 and pulls full memory rows to render one integer.

**Wanted:**

```
GET /memories/stats
→ 200
{
  "active":  { "identity": 6, "preference": 18, "state": 3, "experience": 9, "project": 11 },
  "staged":  2,
  "archived": 41,
  "total_active": 47
}
```

- `active` is keyed by the five v2-001 kinds. Kinds with a zero count **must** still be
  present, so the client never has to distinguish "zero" from "field missing".
- Counts are for the default user unless `?user_id=` is supplied, matching `/memories`.
- Cheap: this is polled on the Home screen. It should be a count query, not a
  materialise-then-count.

**Acceptance:** stats agree exactly with what `GET /memories` returns for the same
filters; a store with zero memories returns all-zero rather than `404` or an empty object.

---

## R2 — Current capture target on `GET /system/status`

**Today:** the rail's live element calls `GET /activity?limit=5` and takes the newest
row's `window_title`, **but only when that row's `decision` is `Captured`**. Rows marked
`Filtered` or `Skipped` are discarded without being displayed, because echoing a
blocklisted window's title into the always-visible rail would leak exactly what the
blocklist exists to protect.

This works, but it is a client-side privacy rule enforced on data the API already
handed over. The rule belongs in the agent.

**Wanted:** an optional block on the existing status payload.

```
GET /system/status
→ 200
{
  "version": "...", "api_version": "...",
  "components": { ... },              // unchanged
  "capture": {
    "enabled": true,                  // mirrors config.capture.enabled
    "window_title": "Figma — Lore rebrand",   // or null
    "observed_at": "2026-07-31T14:12:04Z"     // or null
  }
}
```

**Binding rules for `window_title`:**

1. It is `null` whenever the current window is excluded by the blocklist, by a keyword
   rule, or by any privacy filter — the agent must never emit a title it would not have
   captured. A caller must not be able to infer blocked activity from this field.
2. It is `null` when capture is disabled.
3. It is the **redacted** title, i.e. whatever survives the existing filter chain — not
   the raw Win32 title.
4. Absence of the whole `capture` block must be tolerated by clients (the renderer keeps
   its `/activity` fallback until this ships), so this is additive, not breaking.

**Acceptance:** with a blocklisted app focused, the field is `null` and no part of that
app's title appears anywhere in the response; with capture paused, the field is `null`;
`observed_at` never runs ahead of the newest `/activity` row.

---

## R3 — Evidence for a staged memory (nice-to-have)

**Today:** the queue card's evidence line is built from `metadata.episodes.length` and
`metadata.established_at` — "Seen in 3 episodes · first noticed 28 July". The planned
**Show evidence** action can already be built client-side by fanning out over
`GET /episodes/{id}` for each id in `metadata.episodes`.

**Wanted (only if cheap):** a single `GET /memories/{id}/evidence` returning the episodes
that supported a memory, so the client makes one call instead of N.

```
GET /memories/{id}/evidence
→ 200
{ "episodes": [ { "id": "...", "started_at": "...", "ended_at": "...",
                  "executables": ["Figma.exe"], "titles": ["..."],
                  "observation_count": 12 } ] }
```

Same redaction rules as R2 apply to `titles`. **This is genuinely optional** — the client
path works without it. Skip it if it complicates the storage layer.

---

## Constraints (non-negotiable)

- **Local only.** Loopback `127.0.0.1:7842`, no auth surface, no new outbound calls.
  Nothing here may introduce network egress (constitution §1, §4.3).
- **Additive.** No existing response shape changes. The v2-004 renderer must keep working
  against an agent that has not yet shipped this spec.
- **Redaction is the agent's job.** Where a field could carry window text, it passes
  through the same filter chain as capture — that chain is trust-critical code
  (constitution §6) and must not be bypassed for a display-only field.

## Out of scope

- Any write endpoint. Pause/resume already works through `PATCH /config`.
- Push/streaming. The renderer polls; a websocket is a separate decision.
- Recall, distillation, MCP, or CLI surfaces.

## Handoff notes

- Ordering: **R1 then R2**. R1 is pure win and touches only the memory store; R2 needs
  care in the capture path and is the one with a privacy trap in it.
- The renderer changes needed to consume these are small and can follow separately:
  R1 replaces two calls in `app/src/renderer/views/Today.tsx` and one in
  `app/src/renderer/lib/hooks.ts`; R2 replaces `watchedTitle()` in the same hooks file.
- Verify against the real app, not just tests: `useRailSummary` polls every 8s, so a
  blocklist regression in R2 is visible within seconds of focusing a blocked window.
