# 003 — Capture Pipeline · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 002 (memory
> seam). Parallelizable with 004 (built against a mocked `IInferenceBackend`).

## Overview

The capture pipeline is Lore's reason to exist and its hardest-won asset: the
ambient layer that watches the active window, decides what is worth remembering,
filters out anything sensitive, distills the rest into a clean first-person
observation, and hands it to the memory service. It is a careful port of v1's
proven C# capture code (`WindowMonitor`, `UiaExtractor`, `OcrExtractor`,
`Blocklist`, smart gating) — re-read and cleaned per the constitution, **not**
copy-pasted — with its storage tail re-pointed from v1's SQLite to
`IMemoryService.Remember()`.

The **sensitivity filter chain is the trust-critical path of the entire project**
and is held to the highest test bar (constitution §6).

## User stories

- **As a user**, Lore notices what I'm actually doing (researching a monitor,
  planning a trip, deep in a codebase) without me typing anything, and records a
  useful memory of it.
- **As a privacy-sensitive user**, anything sensitive — password managers, banking,
  SSNs, card numbers, anything on my blocklist — is filtered **before** it is ever
  analyzed, stored, or sent to a model, and I can verify that in the code.
- **As a user on a metered model**, Lore doesn't burn an inference call on every
  idle second — smart gating skips redundant, low-signal, and unchanged windows.
- **As the memory service (002)**, I receive clean, distilled, first-person
  observations — ideal input for mem0's extraction — not raw screen dumps.

## Scope

### In scope

- **Window monitoring:** poll the foreground window (Win32 `GetForegroundWindow`)
  with dwell timing; detect window/title/content changes.
- **Text extraction:** UI Automation (`UiaTextExtractor`) with OCR fallback
  (`OcrTextExtractor`) for windows that don't expose text, behind the `ITextExtractor`
  seam; `CompositeTextExtractor` implements the fallback policy.
- **Sensitivity filter chain (trust-critical):** blocklist (apps + keywords) →
  UIA structural check (password fields etc.) → regex (SSN, card numbers). Applied
  **before** analysis/storage/egress.
- **Smart gating:** dwell threshold, content-diff thresholds (skip near-identical
  captures), per-content-type heuristics (reading/shopping/messaging/coding),
  recent-capture gate, capture-history cap. Ported from v1's smart-capture work.
- **Capture analysis:** send title + filtered text to `IInferenceBackend` (004),
  parse the result into a distilled first-person observation + category metadata.
- **Storage tail:** write the observation via `IMemoryService.Remember()`.
- **Local operational store:** a thin local SQLite for the human-readable activity
  log and raw-capture telemetry the user can inspect (this is operational data, not
  memory — it never goes to mem0).
- **Capture metrics:** counters for decisions made (captured/skipped/filtered) for
  the user to see why Lore did or didn't record something.

### Out of scope

- The inference backends themselves (004) — capture calls `IInferenceBackend` and
  is developed against a mock until 004 lands.
- mem0 internals (002).
- Buckets and retention tiers (**deferred** — not in this rewrite).
- The REST API that surfaces capture data to the app (005) and the app UI (010).
- macOS/Linux capture (future modules; the extractor interfaces leave room).

## Acceptance criteria

1. The pipeline captures a foreground window after the dwell threshold, extracts
   its text, and produces a stored memory via `IMemoryService.Remember()`.
2. **Filter chain:** for an exhaustive case table — blocklisted apps, blocklisted
   keywords, password fields, SSN/card patterns, and safe controls — sensitive
   content is dropped before analysis, and the drop reason is recorded in metrics.
   Coverage of the filter chain is ~100% line + branch.
3. **Smart gating:** a window whose content hasn't meaningfully changed since the
   last capture does **not** trigger a new inference call; thresholds are unit-tested.
4. Capture analysis output is a clean first-person observation (not a raw text
   dump); malformed model output is handled without crashing the loop.
5. The activity log and raw-capture telemetry are written to the **local** store
   only and are never sent to mem0 or any model.
6. Killing/restarting memoryd (002) does not crash capture; observations queue or
   fail gracefully with a logged, recoverable error.

## Non-functional requirements

- **Filter-before-everything:** no code path analyzes, stores, or transmits text
  that hasn't passed the full filter chain. This is verified by tests, not trust.
- **Seam discipline:** Win32/UIA only through the extractor interfaces; models only
  through `IInferenceBackend`; memory only through `IMemoryService` (constitution §3.2).
- **Low idle cost:** smart gating keeps inference calls proportional to genuine
  activity, not wall-clock time.
- **Resilience:** a single bad window/extraction/model response never kills the
  capture loop.

## Risks

- *A filter regression silently leaks sensitive data.* Mitigation: highest test
  bar in the project; the case table is part of the spec; review treats filter
  changes as `ask-user`-level.
- *UIA/OCR edge cases vary wildly across apps.* Mitigation: port v1's hard-won
  handling deliberately; keep extraction behind an interface so fixes are localized.
