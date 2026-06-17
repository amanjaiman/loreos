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
├── ITextExtractor.cs        # text extraction seam (the only UIA/Win32 boundary for text)
├── ExtractedText.cs         # tagged result record + ExtractionSource enum
├── CompositeTextExtractor.cs # fallback policy: tries extractors in order, first non-empty wins
├── UiaTextExtractor.cs      # UI Automation primary path (ContentViewWalker BFS, bounded)
├── OcrTextExtractor.cs      # GDI PrintWindow capture + Windows.Media.Ocr (fallback)
├── ContentType.cs          # the ContentType enum (reading/shopping/messaging/coding/unknown)
├── ContentClassifier.cs    # pure classifier: executable + keyword cues → ContentType
├── Blocklist.cs            # apps + keywords (user-configurable)
├── FilterResult.cs         # FilterDecision / FilterReason enums + FilterResult sealed class
├── IWindowSecurityProbe.cs # UIA structural seam: is this window protected? (fail-closed)
├── SensitivePatterns.cs    # regex layer: SSN (separator-anchored) + Luhn-confirmed cards
├── SensitivityFilter.cs    # the trust-critical chain (blocklist → UIA → regex)
├── UiaWindowSecurityProbe.cs  # IWindowSecurityProbe: focused-element IsPassword check
├── SmartGateOptions.cs     # the v1 smart-capture thresholds (diff high/low, per-type, ...)
├── GateDecision.cs         # GateDecision + SkipReason (typed skip reasons)
├── CaptureHistoryEntry.cs  # one remembered capture (window + text + type + time)
├── SmartGate.cs            # diff thresholds + per-type heuristics + coding heartbeat
├── RecentCaptureGate.cs    # bounded newest-first history; recent-duplicate suppression
├── TextSimilarity.cs       # diff/similarity math used by the gates
├── CaptureAnalysis.cs      # distilled result: first-person observation + category
├── AnalysisPrompt.cs       # trimmed analysis.txt prompt builder (title+text → request)
├── ObservationParser.cs    # JSON-tolerant parse of the model reply (pure, unit-tested)
├── CaptureAnalyzer.cs      # orchestrates prompt → IInferenceBackend → parse
├── CaptureMetrics.cs       # captured/skipped/filtered counters + reasons
└── CaptureAgent.cs         # the loop: orchestrates the above as a BackgroundService
agent/Inference/
└── IInferenceBackend.cs    # minimal model seam + InferenceRequest (004 implements; capture mocks)
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
  text = await textExtractor.ExtractAsync(obs.Window)  # ITextExtractor (CompositeTextExtractor: UIA → OCR)
  filtered = SensitivityFilter.Apply(obs.Window, text)  # ← before anything else
  if filtered.Blocked: metrics.filtered(reason); continue
  type = ContentClassifier.Classify(obs.Window, filtered.Text)
  gate = SmartGate.Evaluate(obs.Window, type, filtered.Text)  # content-diff needs the text
  if not gate.ShouldCapture: metrics.skip(gate.Reason); continue
  result = await analysis.Analyze(obs.Window.Title, filtered.Text)  # IInferenceBackend
  if result is null: metrics.skip("analysis_empty"); continue
  await memory.Remember(result.Text, result.Category)    # IMemoryService
  SmartGate.Record(obs.Window, type, filtered.Text)      # remember it for the next diff
  activityStore.Append(obs.Window, result)               # local telemetry only
  metrics.captured()
```

Registered into the 002 host; awaits the memoryd `Ready` gate before storing.

## Sensitivity filter chain (trust-critical)

Ordered, fail-closed, each layer independently tested:

1. **Blocklist** — app executable match + keyword match (title/text). User-editable.
2. **UIA structural** — drop when focused control is a password field or the window
   exposes protected/secure content.
3. **Regex** — SSN and payment-card patterns (Luhn-checked) dropped (whole capture).

Every drop returns a typed reason recorded by `CaptureMetrics`. Nothing downstream
ever sees unfiltered text.

## Smart gating

Ported v1 smart-capture parameters (config keys already exist in the v1 registry):
`dwell_seconds`, `diff_threshold_high/low`, `reading_threshold`,
`shopping_threshold`, `messaging_tail_chars`, `coding_heartbeat_skip`,
`max_capture_history`. `TextSimilarity` provides the diff; `SmartGate` +
`RecentCaptureGate` apply the thresholds per `ContentType`.

## Capture analysis

`AnalysisPrompt` (ported, trimmed `analysis.txt`) turns filtered title+text into a JSON
observation: a first-person sentence + a category, with an explicit "nothing worth
remembering" escape (an empty observation). `ObservationParser` is the JSON-tolerant
parse as a small pure helper — it unwraps markdown fences/prose, tolerates trailing
commas, and returns `null` for any unusable reply. `CaptureAnalyzer` orchestrates
prompt → `IInferenceBackend` → parse: malformed output → `null` (skip, never crash);
backend/transport failures propagate to the loop, which owns resilience (T008).
**Input to mem0 is this distilled observation** — the quality lever called out in 002.

## Decisions

- **The monitor tracks window + title transitions; content-diff is a gate concern.**
  `WindowMonitor` keys the dwell timer off the foreground window's *identity* (handle +
  title), read through one Win32 seam (`IForegroundWindowSource`). Text content is only
  available after extraction (T002), so "content change detection" lives in
  `TextSimilarity`/`SmartGate` (T004/T005), not the monitor. Dwell is level-triggered
  (`HasDwelled` stays true while focus holds) so a window the gate later skips is still
  re-offered, rather than firing a single edge that can be lost.
- **Extraction is one async seam (`ITextExtractor`) with a tested fallback composite.**
  `CompositeTextExtractor` tries extractors in order and takes the first non-empty result
  (UIA → OCR), tagging the source; this orchestration is pure and fully unit-tested. The
  platform extractors (`UiaTextExtractor`, `OcrTextExtractor`) are total — any failure
  yields `ExtractedText.Empty` so a bad read falls through instead of throwing.
- **OCR uses the OS-native `Windows.Media.Ocr` engine, not a third-party library.** It
  ships with Windows (no extra dependency, no key, nothing leaves the machine —
  constitution §1/§5). This requires a Windows SDK-versioned TFM
  (`net8.0-windows10.0.19041.0`, present on every CI windows runner) so the WinRT
  projections are available; the agent and test projects were bumped accordingly. The
  window is captured with GDI `PrintWindow` and recognized off that bitmap.
- **Consolidate filtering into one ordered, fail-closed `SensitivityFilter`** for
  testability and auditability. Each layer is independently covered ~100% line+branch.
  Implementation choices that make the trust-critical guarantee provable: a block carries
  **no text** (`FilterResult.Text` is empty on every block); the structural UIA layer
  **fails closed** (any probe error answers "protected"); both the window title and the
  extracted text are screened at the keyword and regex layers; and the regex layer
  **drops** (does not redact) a capture containing an SSN or a Luhn-valid card number —
  dropping the whole capture is simpler to prove correct than partial scrubbing.
- **The smart gate runs after extraction+filter, immediately before analysis.** The
  content-diff that decides "unchanged → skip" needs the extracted text, which doesn't
  exist until after extraction — so the single gate decision sits right before the
  expensive inference call (the thing worth saving), not before extraction (cheap, local).
  `SmartGate.Evaluate` returns a typed `GateDecision`; `Record` is called only after a
  successful capture so the next candidate diffs against it. The high/low diff thresholds
  frame a band where the per-`ContentType` bar decides (reading/shopping tolerate more
  similarity); messaging diffs only the trailing tail (new messages append); coding has a
  heartbeat that suppresses constant editor churn. `RecentCaptureGate` is the bounded,
  newest-first history (cap = `MaxCaptureHistory`) the gate consults.
- **Similarity is token-set Jaccard; classification is executable-then-keyword.**
  `TextSimilarity.Similarity` is a set-based Jaccard ratio so scrolling/reflow (same
  vocabulary, shuffled positions) reads as "unchanged" and the gate skips a redundant
  inference call. `ContentClassifier` trusts the executable name first (an IDE is an IDE),
  then small keyword cue lists (shopping → messaging → coding → reading); both are pure and
  unit-tested. The cue lists are heuristics meant to be tuned, not a taxonomy.
- **Activity log / raw captures stay local** in `ActivityStore` and never reach
  mem0 — they are operational telemetry the user can inspect, satisfying "the code
  is the audit trail."
- **Develop against a mock `IInferenceBackend`** so this spec proceeds in parallel
  with 004; integrate for real once 004 merges. 003 defines the minimal seam it needs
  (`CompleteAsync(InferenceRequest) → string?`, a system+user prompt with temperature) in
  `agent/Inference/`; 004 owns the real backends and may extend the contract. Splitting
  analyzer responsibilities so the loop survives a bad model: the **parser** turns
  malformed output into a skip (`null`); the **analyzer** lets backend/transport exceptions
  propagate to the loop's per-tick try/catch (T008) rather than swallowing them, honoring
  §5 "no silently swallowed exceptions on the provider seam."
- **Buckets/retention deferred** — `Remember()` carries category metadata only;
  grouping/expiry can return later as metadata + a scheduled job without touching
  capture.

## Dependencies & order

Upstream: **002** (memory seam + host). Parallel: **004** (mocked until merged).
Internal order: extractors → filter chain (+ its test table) → gating → analysis →
loop + storage tail → metrics + activity store. See [`tasks.md`](tasks.md).
