# v2-008 — Capture Tuning, Retention & Recall Aggregation · Tasks

Each task is PR-sized and lands through the `no-mistakes` gate.

- [x] **T001 — Recall floor check (gates T005).** ✅ Ran. **Result: FAILED — 3 of 8
  returned.** Cause is `Floor` being compared against the blended score while
  `TemporalFactor` saturates at 0.6 past ~187 days; below ~0.87 confidence an aged
  `experience` cannot clear the floor at any similarity. Latent v2-001 defect, not caused by
  this spec. `NormalizeLimit` caps at 1000, so `k` was never the constraint. Tests landed as
  a regression guard pinning today's (broken) behaviour — **T010 must invert them.**

- [ ] **T010 — Recall floor on semantic, blend for rank (R5.3, gates T005).** Compare
  `Floor` against the raw semantic score; keep the blend for ordering only. Then invert
  T001's two tests in `agent.tests/Recall/RecallServiceTests.cs` so they assert all eight
  bookings return, recent-first. **Re-run and re-calibrate the golden corpus** — this changes
  what passes the floor for every kind. A newly-passing unrelated pair is a calibration
  failure to investigate, not a number to update. Do not touch `Floor`, `ExperienceWeight`,
  `ExperienceDecayDays`, or `ExperienceDecayFloor`.

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

- [ ] **T003 — Live capture snapshot (R2).** Extend `LiveCaptureSettings` to carry a full
  resolved snapshot, replaced atomically on `PATCH /config`. Repoint `CaptureAgent`,
  `WindowMonitor` (dwell is a ctor field today), `EpisodeBuilder`, and `LifecycleEngine`.
  Resolves to today's defaults — no user-visible change yet. A mid-episode threshold change
  applies from the next observation and never force-closes an episode.
  - **From T007:** also fold `capture.diagnostics` and `capture.retentionDays` into the
    snapshot. T007 had to leave them startup-bound (it was scoped out of `LiveCaptureSettings`),
    so toggling diagnostics currently needs an agent restart. Once in the snapshot, the
    `_options` reads in `CaptureAgent` and `RecentEndpoints` become snapshot reads.

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
