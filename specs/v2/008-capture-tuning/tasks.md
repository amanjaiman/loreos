# v2-008 — Capture Tuning, Retention & Recall Aggregation · Tasks

Each task is PR-sized and lands through the `no-mistakes` gate.

- [ ] **T001 — Recall floor check (gates T005).** Seed a store with eight flight-shaped
  `experience` memories spanning two years; assert all eight clear `RecallOptions.Floor` and
  return at `k = 30` in recency-weighted order. If they do not, report before building the
  detail control — R1.3's premise depends on this. Confirm `NormalizeLimit` permits `k ≥ 30`.

- [ ] **T002 — Capture defects (R6).** Independent of the preset work; land early.
  - Carry the episode's content-type mix on `Episode` and into `DistillPrompt`.
  - Reset dwell on window change only; a title change within the same window continues dwell.
  - Weight `EpisodeBuilder.SelectSamples()` by time-spent alongside diversity.

- [ ] **T003 — Live capture snapshot (R2).** Extend `LiveCaptureSettings` to carry a full
  resolved snapshot, replaced atomically on `PATCH /config`. Repoint `CaptureAgent`,
  `WindowMonitor` (dwell is a ctor field today), `EpisodeBuilder`, and `LifecycleEngine`.
  Resolves to today's defaults — no user-visible change yet. A mid-episode threshold change
  applies from the next observation and never force-closes an episode.

- [ ] **T004 — Preset resolution and config schema (R3).** Resolve
  `attentiveness` / `certainty` / `detail` → values; explicit raw keys win per-field;
  unknown names fall back to `balanced` with a warning. Add the read-only `capture.resolved`
  block to `GET /config` and make `PATCH` ignore it.

- [ ] **T005 — Detail level in the distill prompt (R1.3).** Swap the detail directive and
  statement cap (120 / 200 / 500) with `SampleMaxChars` (400 / 600 / 900). Encode the
  binding rule: depth changes, fact count does not, and a single observed choice never
  becomes a `preference`. Re-run the golden corpus per preset.

- [ ] **T006 — Settings controls (R1.4).** Add `SegmentedControl` to the vendored design
  system, then three controls in `CapturePrivacy.tsx` below the capture toggle and above the
  blocklist. Consequence lines derive from `capture.resolved`, never from preset names.
  Saves through the existing `api.patchConfig` queue.

- [ ] **T007 — Retention and bounded diagnostics (R4).** `capture.retentionDays` (default
  90, `0` = forever) pruning `episodes` / `decisions` / `activity_log` on start and daily.
  Memories untouched. `capture.diagnostics` off by default, bounded to 24h / 500 rows, with
  its Settings switch. Evidence endpoints tolerate pruned episodes.

- [ ] **T008 — Recall aggregation (R5.1).** Extend `LoreTools.Recall`'s `[Description]` to
  invite a higher `k` and an `experience` filter when the request resembles something the
  user has done before. Model-facing string — change deliberately, keep the every-message
  framing.

- [ ] **T009 — Delivery.** Docs (`docs/privacy.md`, `docs/architecture.md`), release notes
  covering the `balanced` re-read change (30s → 25s, ~20% more readings), local checks,
  and the live app check from the plan.
