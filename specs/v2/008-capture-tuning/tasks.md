# v2-008 — Capture Tuning, Retention & Recall Aggregation · Tasks

Each task is PR-sized and lands through the `no-mistakes` gate.

- [x] **T001 — Recall floor check (gates T005).** ✅ Ran. **Result: FAILED — 3 of 8
  returned.** Cause is `Floor` being compared against the blended score while
  `TemporalFactor` saturates at 0.6 past ~187 days; below ~0.87 confidence an aged
  `experience` cannot clear the floor at any similarity. Latent v2-001 defect, not caused by
  this spec. `NormalizeLimit` caps at 1000, so `k` was never the constraint. Tests landed as
  a regression guard pinning today's (broken) behaviour — **T010 must invert them.**

- [x] **T010 — Recall floor on semantic, blend for rank (R5.3, gates T005).** ✅ Done.
  `RecallService` now filters on the raw semantic score and orders by the blend; `Floor`,
  `ExperienceWeight`, `ExperienceDecayDays`, `ExperienceDecayFloor` and `RecallScorer.Blend`
  are untouched. All eight bookings return at `k = 30`. Golden corpus re-run pair by pair.

  **Calibration result: no unrelated pair newly passes.** Four non-flight pairs cross the
  floor that did not before, all of them on-topic: `marathon-old` on the travel query (0.60
  semantic — the aged-experience fix working on a non-flight memory), and `tea` (0.48) and
  `weak-state` (0.55) on the takeout query. The true-negative case is unchanged — the
  carbonara query still returns empty, and `boston` (0.42) is still excluded on travel. The
  separation holds but is tighter: on the semantic axis the corpus splits 0.42 (out) from
  0.48 (in) around the 0.47 floor, versus the 0.44/0.50 blended bands the `Floor` doc
  recorded. Only `tea` sits in that narrowed gap.

  **Two consequences worth carrying forward, neither a blocker:**
  - **Confidence no longer gates inclusion for any kind** — only rank. `weak-state` is a
    0.3-confidence hunch that now returns with a 0.19 score where it used to be omitted.
    Intended (a score is legible to a caller, an omission is not), but broader than the
    aged-experience case R5.3 was written for. The wisdom-teeth relevance case therefore
    **did change**, against R5.3's "existing relevance cases are unchanged" acceptance.
  - **"Ordered recent-first" is NOT delivered, and cannot be under T010's constraints.**
    Past ~187 days the temporal factor is pinned at `ExperienceDecayFloor` for every row, so
    the blend degenerates to `semantic × 0.9 × confidence` and age stops contributing.
    Among the five saturated bookings the order is set by embedder noise (0.8140–0.8309):
    `flight-16mo` outranks `flight-8mo`, and 8mo/24mo tie exactly. Returning all eight is
    delivered; ordering them by age needs `ExperienceDecayFloor`/`ExperienceDecayDays`,
    which T010 was scoped out of. Asserted explicitly in `RecallServiceTests` as a known
    gap. **Worth a follow-up task if R1.3's consumer depends on recency order.**

- [x] **T002 — Capture defects (R6).** Independent of the preset work; land early.
  - Carry the episode's content-type mix on `Episode` and into `DistillPrompt`.
  - Reset dwell on window change only; a title change within the same window continues dwell.
  - Weight `EpisodeBuilder.SelectSamples()` by time-spent alongside diversity.
  - **Two follow-ups this task deliberately did not take.** (1) `Episode.ContentTypeMix` is a
    live distiller input only — the `episodes` table has no column for it and the repo has no
    schema-migration path, so adding one would break existing installs' inserts; episodes read
    back from storage carry an empty mix. (2) R6.2 removes the only thing that was throttling
    title churn: `ShouldProcess` keys on handle + title, so a window that retitles every poll
    is now extracted every poll, ignoring `RecaptureInterval` (~1,800 readings/hour at 2s
    polling against ~144 at 25s). Wanted, per R6.2's acceptance, but it deserves a floor —
    likely a minimum gap for a title-only re-read — sized alongside T003's live snapshot.

- [ ] **T011 — Recall calibration follow-ups (R5.4).** Two decided consequences of T010.
  Add `RecallOptions.MinConfidence` (default 0.5) as a second inclusion gate beside the
  semantic floor — `semantic >= Floor AND confidence >= MinConfidence`, ordered by the blend.
  Lower `ExperienceDecayFloor` 0.6 → 0.2, now safe because it can no longer cause exclusion.
  Re-run the golden corpus; `weak-state` should drop out again and the eight bookings should
  come back in strict recency order.

- [x] **T003 — Live capture snapshot (R2).** ✅ Done. Extend `LiveCaptureSettings` to carry a full
  resolved snapshot, replaced atomically on `PATCH /config`. Repoint `CaptureAgent`,
  `WindowMonitor` (dwell is a ctor field today), `EpisodeBuilder`, and `LifecycleEngine`.
  Resolves to today's defaults — no user-visible change yet. A mid-episode threshold change
  applies from the next observation and never force-closes an episode.
  - **From T007:** also fold `capture.diagnostics` and `capture.retentionDays` into the
    snapshot. T007 had to leave them startup-bound (it was scoped out of `LiveCaptureSettings`),
    so toggling diagnostics currently needs an agent restart. Once in the snapshot, the
    `_options` reads in `CaptureAgent` and `RecentEndpoints` become snapshot reads.
  - **From T002 — fix the title-churn throttle (do not skip).** T002's R6.2 fix made
    retitling windows capturable and, as a side effect, uncapped: `ShouldProcess` keys on
    `handle + title`, so they now bypass `RecaptureInterval` entirely (~1,800 readings/hour
    at 2s polling vs ~144 at 25s, with OCR on the expensive path). Add a **title-only
    re-read gap** to the snapshot — a minimum interval before the same *window* is
    re-extracted after a title-only change, distinct from the full `RecaptureInterval` for
    unchanged windows. Scale it with `attentiveness` alongside the other timings. A retitling
    window must still be captured (R6.2's acceptance) — just not 12× more often than every
    other window.

  **What landed.** `CaptureSnapshot` (new record) carries pause, blocklist, `PollInterval`,
  `DwellThreshold`, `RecaptureInterval`, the new `TitleRecaptureInterval`, the whole
  `EpisodeOptions` and `LifecycleOptions`, `Diagnostics` and `RetentionDays`.
  `LiveCaptureSettings` swaps it wholesale under `Volatile.Read`/`Write`; `PATCH /config`
  still goes through the existing `Update(capture)` seam. `CaptureAgent`, `WindowMonitor`,
  `EpisodeBuilder`, `LifecycleEngine`, `RetentionService`, `RecentEndpoints`,
  `ActivityEndpoints` and `SystemEndpoints` all read it. **`CaptureOptions`, `EpisodeOptions`
  and `LifecycleOptions` are no longer registered in DI at all** — there is exactly one source
  of truth, guarded by a structural test. Resolves to today's defaults; no user-visible change.

  - **Validation moved to the snapshot boundary.** `EpisodeBuilder`'s constructor checks are now
    in `CaptureSnapshot.From`, which both the startup binding and every PATCH go through: a bad
    value is replaced with the default and logged, never thrown. Throwing took the agent down at
    DI resolution over a mistyped number, and with the values live it would be a background
    thread killing the capture loop mid-episode. Scope is exactly what can break the loop
    (`Task.Delay` on a non-positive interval, a negative substring index, a bound that closes
    every episode at its first observation) plus a 1-minute ceiling on `PollInterval`, which
    catches `"pollInterval": 2` — the binder reads a bare `2` as two **days**. Lifecycle
    thresholds are deliberately not range-checked: none has such a path.
  - **Title-only gap: 10s.** ~2.5× a static window's read rate instead of ~12×, cutting a
    retitling window from ~1,800 readings/hour to ~360 while keeping worst-case staleness for
    genuinely new content at 10 seconds. T004 scales it with `attentiveness`.
  - **T002's test changed.** `A_window_that_rewrites_its_title_every_poll_is_still_captured`
    asserted 5 observations within a 20-second script — an assertion about the *unbounded*
    behaviour T002 shipped, where every retitle was a fresh key. Its script now runs 45 seconds
    and it asserts exactly 5 observations (t = 4, 14, 24, 34, 44). Being captured is what R6.2
    asks for and is still asserted; being captured on every poll never was.
  - **Absent keys keep the value in force**, rather than reverting to the built-in default — the
    startup binding draws on configuration sources besides config.json, and a blocklist edit
    must not silently discard a timing set through one of them. T004 replaces that fallback base
    with the resolved preset, which is what makes R3's "delete the raw key, the preset's value
    returns" work.

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
  - **From T007:** also add the "Record what Lore reads (for troubleshooting)" switch for
    `capture.diagnostics` (R4.2). T007 built the backend but left the renderer alone to
    avoid colliding with this task's rewrite of `CapturePrivacy.tsx`.

- [x] **T007 — Retention and bounded diagnostics (R4).** `capture.retentionDays` (default
  90, `0` = forever) pruning `episodes` / `decisions` / `activity_log` on start and daily.
  Memories untouched. `capture.diagnostics` off by default, bounded to 24h / 500 rows, with
  its Settings switch. Evidence endpoints tolerate pruned episodes.

- [x] **T008 — Recall aggregation (R5.1).** Extend `LoreTools.Recall`'s `[Description]` to
  invite a higher `k` and an `experience` filter when the request resembles something the
  user has done before. Model-facing string — change deliberately, keep the every-message
  framing.

- [ ] **T009 — Delivery.** Docs (`docs/privacy.md`, `docs/architecture.md`), release notes
  covering the `balanced` re-read change (30s → 25s, ~20% more readings), local checks,
  and the live app check from the plan.
  - **Live MCP schema check (from T008).** T008 added `kinds` as an `IReadOnlyList<string>?`
    tool parameter — the first collection-typed *parameter* on any tool in `LoreTools.cs`
    (all other `IReadOnlyList` uses there are locals or return types). Unit tests invoke the
    C# method directly and never exercise the SDK's JSON-schema generation, so a green suite
    proves nothing here. Connect a real MCP client and confirm the `recall` tool advertises
    `kinds` as an array and that passing `["experience"]` filters. If the SDK chokes, switch
    the parameter to `string[]?`.
