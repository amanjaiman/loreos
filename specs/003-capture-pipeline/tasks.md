# 003 — Capture Pipeline · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — Window monitor + dwell/change detection  `[deps: spec 002]`

> **Status: complete.** `IForegroundWindowSource` (Win32 seam), `Win32ForegroundWindowSource`
> (one P/Invoke boundary; access-denied returns `WindowSnapshot.None`),
> `WindowSnapshot`, `WindowObservation`, and `WindowMonitor` (dwell timer + change
> detection, fully tested without a live desktop via `FakeWindowSource` /
> `FakeTimeProvider`) all landed here. `AllowUnsafeBlocks` enabled on the agent
> project for the `LibraryImport` source-generator stubs. 9 unit tests covering dwell,
> level-triggering, title-change reset, no-window idle, and constructor validation.

Port `WindowMonitor` (Win32 foreground polling, dwell timing, window/title/content
change detection) into the 002 host as the front of the loop.
**Done when:** the monitor reports the active window and fires only after the dwell
threshold; unit tests cover dwell + change transitions.

### T002 — Text extraction (UIA + OCR fallback)  `[deps: T001]`
Port `UiaExtractor` and `OcrExtractor` behind a common extraction interface; OCR is
the fallback when UIA exposes no text. (`UseWPF=true` from 001 enables UIA.)
**Done when:** extraction returns text for a UIA-friendly window and falls back to
OCR otherwise; the interface is the only Win32/UIA seam.

### T003 — Sensitivity filter chain + exhaustive case table  `[deps: T002]` ⚠ trust-critical
Implement `SensitivityFilter` as one ordered, fail-closed chain (blocklist → UIA
structural → regex SSN/card with Luhn). Each drop returns a typed reason. Build the
exhaustive case table from `specification.md` acceptance criterion 2.
**Done when:** the chain is ~100% line+branch covered; every case-table row asserts
the correct drop/allow + reason; no downstream path can see unfiltered text.

### T004 — Content classification + similarity math  `[deps: T002]`
Port `ContentType` and `TextSimilarity` (pure functions feeding the gates).
**Done when:** classification and similarity are unit-tested over representative
inputs.

### T005 — Smart gating  `[deps: T003, T004]`
Implement `SmartGate` + `RecentCaptureGate` using the v1 smart-capture thresholds
(dwell, diff high/low, per-type, history cap). Skips return typed reasons.
**Done when:** an unchanged window does not trigger analysis; thresholds and
per-`ContentType` behavior are unit-tested.

### T006 — Capture analysis (distillation)  `[deps: T003]`
Port the `analysis.txt` prompt (trimmed) and the JSON-tolerant parse helper (as a
pure function). Turn filtered title+text into a first-person observation + category
via `IInferenceBackend` (mocked). Malformed output → skip, never crash.
**Done when:** good input yields a clean distilled observation; malformed model
output is handled; parser is unit-tested.

### T007 — Local activity store  `[deps: spec 002]`
Implement `ActivityStore` (thin local SQLite: `activity_log`, `raw_captures`). This
is operational telemetry only and must never reach mem0 or a model.
**Done when:** entries are written locally; a test asserts no activity-store data
crosses the `IMemoryService`/`IInferenceBackend` seams.

### T008 — Assemble the capture loop + metrics  `[deps: T005, T006, T007]`
Wire `CaptureAgent` as a `BackgroundService` in the 002 host: monitor → gate →
extract → filter → analyze → `Remember()` → activity log. Add `CaptureMetrics`
(captured/skipped/filtered + reasons). Await the memoryd `Ready` gate; survive
memoryd restarts.
**Done when:** end-to-end capture produces a stored memory; metrics explain every
decision; killing memoryd mid-run doesn't crash the loop (acceptance criteria 1, 6).

---

## Definition of done for spec 003

All acceptance criteria pass; the filter chain is exhaustively covered and
fail-closed; smart gating demonstrably limits inference calls; distilled
observations flow to `IMemoryService`; activity telemetry stays local. Capture is
ready to be surfaced by the local API (005).
