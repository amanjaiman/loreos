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

> **Status: complete.** `ITextExtractor` (async seam), `ExtractedText` / `ExtractionSource`
> (tagged result), `CompositeTextExtractor` (fallback policy: UIA → OCR, first non-empty
> wins; fully unit-tested, 8 tests), `UiaTextExtractor` (ContentViewWalker BFS, 1 500-node /
> 20 000-char caps), and `OcrTextExtractor` (GDI `PrintWindow` capture +
> `Windows.Media.Ocr`) all landed here. Both platform extractors are total — any failure
> yields `ExtractedText.Empty`. TFM bumped to `net8.0-windows10.0.19041.0` on both
> `LoreAgent` and `LoreAgent.Tests` for the WinRT projections.

Port `UiaTextExtractor` and `OcrTextExtractor` behind a common `ITextExtractor` seam;
OCR is the fallback when UIA exposes no text. (`UseWPF=true` from 001 enables UIA.)
**Done when:** extraction returns text for a UIA-friendly window and falls back to
OCR otherwise; the interface is the only Win32/UIA seam.

### T003 — Sensitivity filter chain + exhaustive case table  `[deps: T002]` ⚠ trust-critical

> **Status: complete.** `FilterDecision` / `FilterReason` / `FilterResult` (sealed class, not
> record — trust-critical coverage measures real branches only), `Blocklist` (app + keyword
> match; case-insensitive, `.exe`-suffix-normalised), `IWindowSecurityProbe` (UIA seam;
> fail-closed), `UiaWindowSecurityProbe` (focused-element `IsPassword` check; any UIA error
> → "protected"), `SensitivePatterns` (SSN separator-anchored regex + Luhn-confirmed card
> regex; pure/static), and `SensitivityFilter` (one ordered chain: blocklist → UIA structural
> → regex; both title and text screened; block carries no text) all landed here. Both `.exe`
> and case variants, edge cases, and each chain layer are covered by the exhaustive case
> table in `SensitivityFilterTests`. CA1861 relaxed in `.editorconfig` for `*.tests`
> (inline `new[]/InlineData` are idiomatic test inputs). 93 tests passing; trust-critical
> coverage verified 100% line + 100% branch across `SensitivityFilter`, `Blocklist`,
> `SensitivePatterns`, and `FilterResult`.

Implement `SensitivityFilter` as one ordered, fail-closed chain (blocklist → UIA
structural → regex SSN/card with Luhn). Each drop returns a typed reason. Build the
exhaustive case table from `specification.md` acceptance criterion 2.
**Done when:** the chain is ~100% line+branch covered; every case-table row asserts
the correct drop/allow + reason; no downstream path can see unfiltered text.

### T004 — Content classification + similarity math  `[deps: T002]`

> **Status: complete.** `ContentType` (enum: Unknown/Reading/Shopping/Messaging/Coding),
> `ContentClassifier` (static; executable-name sets win first — `CodingApps` /
> `MessagingApps` hash sets — then small keyword cue lists searched over title + text in
> order: shopping → messaging → coding → reading; falls through to `Unknown`), and
> `TextSimilarity` (`Similarity` — token-set Jaccard in [0, 1]; `Difference` — its
> complement; tokens normalised with `ToUpperInvariant` to satisfy CA1308; two empty
> texts = 1.0, empty vs. non-empty = 0.0) all landed here. Both are pure static classes
> with no Win32/UIA/platform seam. 21 new unit tests covering classifier exe-wins,
> keyword priority, case-insensitivity, and all similarity boundary cases; 120 tests
> passing total.

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
