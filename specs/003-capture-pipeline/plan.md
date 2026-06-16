# 003 — Capture Pipeline · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Port v1's capture stack into the host that 002 established, cleaning each file as it
moves (constitution §5: transcription with judgment, no dead code). Keep the proven
structure — monitor → extract → filter → gate → analyze → store — but swap the
storage tail from v1's `ContextRepository`/SQLite to `IMemoryService.Remember()`,
and route analysis through `IInferenceBackend` (mockable until 004).

## Structure

```
agent/Capture/
├── IForegroundWindowSource.cs   # Win32 seam: the one P/Invoke boundary for window identity
├── Win32ForegroundWindowSource.cs  # production implementation (GetForegroundWindow + title/pid)
├── WindowSnapshot.cs        # identity of one foreground window at one poll tick
├── WindowObservation.cs     # one Poll() result: window + change kind + dwell duration + HasDwelled
├── WindowMonitor.cs         # dwell timer + change detection (testable; no Win32 calls here)
├── UiaExtractor.cs         # UI Automation text extraction
├── OcrExtractor.cs         # OCR fallback for windows without exposed text
├── ContentType.cs          # classify reading/shopping/messaging/coding/...
├── Blocklist.cs            # apps + keywords (user-configurable)
├── SensitivityFilter.cs    # the trust-critical chain (blocklist → UIA → regex)
├── SmartGate.cs            # dwell + diff thresholds + per-type heuristics
├── RecentCaptureGate.cs    # suppress near-duplicate recent captures
├── TextSimilarity.cs       # diff/similarity math used by the gates
├── CaptureMetrics.cs       # captured/skipped/filtered counters + reasons
└── CaptureAgent.cs         # the loop: orchestrates the above as a BackgroundService
agent/Storage/
└── ActivityStore.cs        # thin local SQLite: activity_log + raw_captures (NOT mem0)
```

> **Note on the filter chain:** v1 spread sensitivity logic across `Blocklist` and
> inline checks. This port consolidates it into one `SensitivityFilter` with an
> explicit, ordered, individually testable chain — so acceptance criterion 2's
> exhaustive case table maps to one well-covered unit.

## The loop (`CaptureAgent`, a hosted `BackgroundService`)

```
every poll tick:
  obs = WindowMonitor.Poll()           # → WindowObservation (window, change, dwell, HasDwelled)
  if not obs.HasDwelled: continue
  if SmartGate.ShouldSkip(obs.Window, history): metrics.skip(reason); continue
  text = UiaExtractor.Extract(obs.Window) ?? OcrExtractor.Extract(obs.Window)
  filtered = SensitivityFilter.Apply(obs.Window, text)  # ← before anything else
  if filtered.Blocked: metrics.filtered(reason); continue
  result = await analysis.Analyze(obs.Window.Title, filtered.Text)  # IInferenceBackend
  if result is null: metrics.skip("analysis_empty"); continue
  await memory.Remember(result.Text, result.Category)    # IMemoryService
  activityStore.Append(obs.Window, result)               # local telemetry only
  metrics.captured()
```

Registered into the 002 host; awaits the memoryd `Ready` gate before storing.

## Sensitivity filter chain (trust-critical)

Ordered, fail-closed, each layer independently tested:

1. **Blocklist** — app executable match + keyword match (title/text). User-editable.
2. **UIA structural** — drop when focused control is a password field or the window
   exposes protected/secure content.
3. **Regex** — SSN and payment-card patterns (Luhn-checked) scrubbed/dropped.

Every drop returns a typed reason recorded by `CaptureMetrics`. Nothing downstream
ever sees unfiltered text.

## Smart gating

Ported v1 smart-capture parameters (config keys already exist in the v1 registry):
`dwell_seconds`, `diff_threshold_high/low`, `reading_threshold`,
`shopping_threshold`, `messaging_tail_chars`, `coding_heartbeat_skip`,
`max_capture_history`. `TextSimilarity` provides the diff; `SmartGate` +
`RecentCaptureGate` apply the thresholds per `ContentType`.

## Capture analysis

`analysis.txt` prompt (ported, trimmed) turns filtered title+text into a JSON
observation: a first-person sentence + a category. Uses `InferenceManager`'s
JSON-tolerant parse (ported as a small pure helper). Malformed output → skip, never
crash. **Input to mem0 is this distilled observation** — the quality lever called
out in 002.

## Decisions

- **The monitor tracks window + title transitions; content-diff is a gate concern.**
  `WindowMonitor` keys the dwell timer off the foreground window's *identity* (handle +
  title), read through one Win32 seam (`IForegroundWindowSource`). Text content is only
  available after extraction (T002), so "content change detection" lives in
  `TextSimilarity`/`SmartGate` (T004/T005), not the monitor. Dwell is level-triggered
  (`HasDwelled` stays true while focus holds) so a window the gate later skips is still
  re-offered, rather than firing a single edge that can be lost.
- **Consolidate filtering into one ordered, fail-closed `SensitivityFilter`** for
  testability and auditability.
- **Activity log / raw captures stay local** in `ActivityStore` and never reach
  mem0 — they are operational telemetry the user can inspect, satisfying "the code
  is the audit trail."
- **Develop against a mock `IInferenceBackend`** so this spec proceeds in parallel
  with 004; integrate for real once 004 merges.
- **Buckets/retention deferred** — `Remember()` carries category metadata only;
  grouping/expiry can return later as metadata + a scheduled job without touching
  capture.

## Dependencies & order

Upstream: **002** (memory seam + host). Parallel: **004** (mocked until merged).
Internal order: extractors → filter chain (+ its test table) → gating → analysis →
loop + storage tail → metrics + activity store. See [`tasks.md`](tasks.md).
