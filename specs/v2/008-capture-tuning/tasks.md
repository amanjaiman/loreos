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

- [x] **T011 — Recall calibration follow-ups (R5.4).** ✅ Done. Both decided consequences of
  T010 land as specified. `RecallOptions.MinConfidence` (0.5) is a second inclusion gate
  beside the semantic floor — `semantic >= Floor AND confidence >= MinConfidence`, ordered by
  the blend — and `ExperienceDecayFloor` drops 0.6 → 0.2, moving saturation from ~187 days to
  ~587. `Floor`, `ExperienceWeight`, `ExperienceDecayDays` and `Blend` untouched.

  **Golden corpus re-run pair by pair. Both acceptance criteria met.** The eight bookings
  return in **strict recency order** — `1mo, 3mo, 5mo, 8mo, 12mo, 16mo, 20mo, 24mo` at
  0.6788 / 0.5786 / 0.4858 / 0.3796 / 0.2705 / 0.2008 / 0.1485 / 0.1465 — inverting T010's
  noisy `…16mo, 20mo, 12mo, 8mo…`. `weak-state` (0.3 confidence) drops out of the takeout
  query while `tea` (0.8) stays, **despite `weak-state` scoring higher semantically** (0.55
  vs 0.48) — the clearest demonstration that the two gates measure different things. The
  wisdom-teeth case is unaffected. The kept set is exactly T010's minus `weak-state`: **no
  pair newly clears either gate**, and the carbonara true-negative is still empty.

  **Two things to carry forward, neither a blocker:**
  - **The ordering gap is narrowed, not closed.** Saturation moved to ~587 days, so the 20-
    and 24-month rows (600 and 730 days) are *still* both pinned at `ExperienceDecayFloor`
    and still separated by embedder noise (0.8250 vs 0.8140) rather than by age. They happen
    to land in recency order on this corpus; that is a coincidence of these similarities, not
    a property of the scoring. Everything out to 16 months is genuinely recency-ordered.
    Ordering a history longer than ~19 months needs a lower floor or a larger
    `ExperienceDecayDays` — a further calibration, not a defect. Asserted explicitly.
  - **Nothing outside the aged experiences reordered.** The decay-floor change is visible on
    exactly one non-flight row, `marathon-old` (seven years, past saturation under either
    value): its blend falls ~0.29 → ~0.10. It still returns — the semantic floor, not the
    decay floor, has guaranteed that since R5.3 — and still ranks below `france`. No
    experience changed position relative to any `state` or `identity` memory; `france` at 60
    days is inside the live-decay window under both floors, so the cross-kind ordering tests
    (`Old_experiences_decay_below_current_facts_at_equal_similarity`) are untouched.

  Tests updated: the wisdom-teeth test and its `LoreToolsTests` sibling (3 hits → 2),
  `Flight_booking_history_returns_every_booking_however_old` (T010's known-gap assertions
  replaced by strict recency plus the honest 20/24mo saturation note),
  `Experience_decay_is_gentle_and_floored` (0.6 → 0.2 plus a saturation-day assertion), and
  two new tests pinning the confidence gate's independence and its `>=` boundary. **No
  `GoldenCorpus` similarity value was changed.** 493 agent + 100 CLI tests green.

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

- [x] **T004 — Preset resolution and config schema (R3).** ✅ Done. `CapturePresets` holds the
  three tables (`AttentivenessPreset` / `CertaintyPreset` / `DetailPreset` enums, config stores
  the name) and is the one place a name becomes numbers. Both paths resolve the preset into a
  **base** `CaptureOptions` and then lay the explicitly-present raw keys over it:
  `AddCapturePipeline` binds the `capture` section *onto* the resolved preset (the binder only
  writes keys configuration actually has), and `LiveCaptureSettings.Update` uses presence in the
  handed-over JSON object. `CaptureSnapshot.From` still owns all validation, and now resolves the
  presets a second time so **repairs fall back to the preset's value, not the built-in default** —
  a `light` user who mistypes a cap gets light's 30 back, not balanced's 48.

  - **T003's fallback base is gone.** An absent key now takes the resolved preset, never the value
    currently in force; that is what makes "delete the raw key, the preset's value returns" work.
    The cost T003 named is real and accepted: a timing set through a configuration source other
    than config.json (an environment variable) now survives only until the first capture PATCH.
    **The blocklist is the one field deliberately kept on T003's rule**: no preset touches it, so
    there is nothing to revert to, and the wrong way to fail on a section that arrives without it
    is to stop filtering. Clearing it deliberately still works — the editor sends an empty array,
    which is present, not absent.
  - **The shipped defaults ARE the balanced column**, pinned by a test both ways.
    `CaptureOptions.RecaptureInterval` 30s → **25s** and `EpisodeOptions.MaxObservations` 40 →
    **48** as R1.1 requires. Nothing else moved. **T009 must carry this in the release notes.**
  - **`TitleRecaptureInterval` scales 15 / 10 / 6 seconds** across light/balanced/close. It is not
    in R1.1's table (T003 added the knob afterwards). The values hold it at a constant ~2.5× the
    read rate of a window sitting still — the ratio T003 sized 10s to get — against each stop's
    40/25/15 re-read. Pinned at 10s a `light` user would see retitling windows read 4× as often as
    everything else, which is the cost blow-out the knob exists to prevent. Asserted as a ratio,
    not as three literals, so a retune of `RecaptureInterval` cannot silently break the pairing.
  - **`capture.statementMaxChars`** (120 / 200 / 500) is a new writable key carried in the
    snapshot alongside the resolved `Detail` stop, for T005 to consume. It moves with
    `episodes.sampleMaxChars` (400 / 600 / 900) and is individually overridable like everything
    else.
  - **`capture.resolved`** is added to `GET /config` (and to the `PATCH` response, so a caller
    sees what its own patch resolved to) whenever the capture pipeline is registered. It echoes
    every writable key except the blocklists — TimeSpans as `"hh:mm:ss"` strings, `episodes` and
    `lifecycle` nested exactly as they are written — so a line copied out of it and into `capture`
    pins that value; a test round-trips the whole block back through `Update` and asserts an
    identical snapshot. It is stripped both at the endpoint and in `LoreConfig.PatchAsync`, so no
    read-modify-write can persist it and a file that already holds one is cleaned on the next
    write.
  - **Preset names are `string?` on `CaptureOptions`, not enums.** The configuration binder
    *throws* on an enum value it cannot parse, which would take the agent down at startup over a
    typo in a file we invite the user to edit. Parsing is `CapturePresets`' job: unknown → warn +
    balanced, absent/empty → balanced silently (that is every pre-v2-008 config, not a mistake).
    A bare number is rejected too — `Enum.TryParse` would have read `"1"` as an ordinal.
  - **Four T003 tests were rewritten**, all for the fallback-base change: two seeded a value via
    the constructor and then PATCHed a partial object, which now resets that value to the preset.
    They pass the whole capture section instead, which is what `ConfigEndpoints` actually hands
    over. 546 agent + 100 CLI tests green.

- [x] **T005 — Detail level in the distill prompt (R1.3).** ✅ Done. `DistillPrompt.Build` takes
  `(Episode, DetailPreset, int statementMaxChars)`; `Distiller` reads both from
  `LiveCaptureSettings.Current` **once per episode** and holds the pair for that call, so a PATCH
  landing mid-distill can never pair `rich`'s directive with `minimal`'s cap. The templated cap
  and one directive sentence are the *only* things that move with the stop — the skepticism, the
  kinds, the confidence rule and the worked examples are byte-identical at all three, because
  those decide what Lore remembers and this control decides only how fully it is written down.
  Temperature stays 0.

  - **`balanced` is proven byte-identical to the pre-T005 prompt.** The old
    `DistillPrompt.System` literal is frozen verbatim in `DistillPromptTests` as
    `LegacySystemPrompt` and asserted equal, ordinally, to `Build(episode, Balanced, 200)`. That
    is the assertion that says the refactor did not silently change what Lore remembers for
    every existing user, and it is why the E2E harness — which pins `balanced` — needed no
    recalibration. `MemoryLayerE2ETests` now shares one `LiveCaptureSettings` between the
    distiller and the engine, seeded with defaults, i.e. explicitly the middle stop.
  - **The three directives.** `minimal` says what to leave out ("Keep the statement GENERAL —
    the core fact only, without the particulars…"), because a 120-char cap already enforces
    brevity and on its own would just truncate the same specifics. `balanced` is today's
    sentence, frozen. `rich` leads with the specifics and then carries the binding rule in the
    terms detail invites: "Detail means a LONGER statement, never MORE facts: one event stays
    one fact, and a single observed choice is never a preference — seat 14C is a detail of this
    booking, not a preference for aisle seats." The header's "never infer identity traits from a
    single page view" is unchanged and asserted present at all three stops, so `rich` restates
    that guard rather than replacing it. Directive lengths are 201 / 243 / 524 characters, so
    `rich` costs ~280 characters more than `balanced` per episode and `minimal` costs ~40 less.
    A test pins that ordering and a 400-character ceiling on `rich`'s excess, so prompt weight
    cannot creep.
  - **The cap is a separate argument from the stop, deliberately.** A raw
    `capture.statementMaxChars` wins over the preset for that field alone (R3), so the two can
    legitimately disagree; deriving the number from the enum would have quietly discarded the
    override. Pinned by a test that renders `rich` at 300.
  - **`SampleMaxChars` verified to reach truncation, not just the snapshot.** A test drives the
    real `EpisodeBuilder` off each resolved stop with a specific planted at character 700 of the
    sample: it reaches the user message at `rich` (900) and is cut at `balanced` (600) and
    `minimal` (400). That is the "both caps move together" claim tested end to end rather than
    asserted.
  - **The golden corpus does not cover this task.** `GoldenCorpus` is a recall fixture — hand
    authored embedding similarities calibrating `RecallScorer`/`RecallService`. Nothing in it
    touches `DistillPrompt`, so "re-run the golden corpus per preset" has no work behind it. The
    only harness that pins prompt behaviour is the opt-in E2E one (`LORE_TEST_E2E=1`, needs
    Ollama), and it pins `balanced`, which the equivalence test proves unmoved. **A real
    per-preset behavioural check on `minimal` and `rich` still has to be run against a live
    model — it belongs with T009's live app check.**
  - 563 agent + 100 CLI tests green (546 + 17 new); `dotnet format --verify-no-changes` clean.

- [x] **T006 — Settings controls (R1.4).** ✅ Done. `SegmentedControl` joins the vendored design
  system (`components/forms/`: `.jsx` + `.d.ts` + `.prompt.md`, a `lore-segmented` block in
  `tokens/components.css`, exported from `index.ts`), and `CapturePrivacy.tsx` renders the three
  stops in a new **How Lore watches** card between the capture toggle and the blocklist, with
  T007's diagnostics switch in a **Diagnostics** card beside it. Every change writes immediately
  through the existing `api.patchConfig` queue; the patch is built from the writable keys only, so
  `capture.resolved` is never sent back.
  - **From T007:** the "Record what Lore reads (for troubleshooting)" switch for
    `capture.diagnostics` (R4.2) is in, bound to `resolved.diagnostics`, off by default.

  - **The consequence lines are a module, not strings in the view.** `renderer/lib/capture.ts`
    parses `capture.resolved` (TimeSpan strings included) and returns one sentence per control, so
    the derivation is testable without a DOM and the view holds no arithmetic. Each line names the
    fields it reads:
    - attentiveness ← `pollInterval`, `dwellThreshold`, `recaptureInterval`,
      `episodes.maxObservations`, `episodes.maxAge` — *"Lore reads your screen about every 25
      seconds, once a window has held your attention for 4 seconds — roughly 24 AI calls in an
      8-hour day."*
    - certainty ← `lifecycle.highSignalConfidence`, `lifecycle.stagedTtlDays` — *"Lore keeps a
      memory on its own when it's at least 85% sure — anything less waits in review for 14 days."*
    - detail ← `statementMaxChars`, `episodes.sampleMaxChars` — *"Each memory is written in up to
      200 characters, from samples of up to 600 characters of what was on screen."*
    - retention (under the diagnostics switch) ← `retentionDays`.
  - **The read cadence is `max(pollInterval, recaptureInterval)` and the episode length is capped
    by `episodes.maxAge`.** Quoting the re-read gap alone would overstate a config whose poll is
    slower than it, and `maxObservations × re-read` alone would promise 7-hour episodes to anyone
    who pinned a large cap. One closed episode is one distill call, so the call count falls out of
    whichever bound ends the episode first.
  - **R1.4's illustrative "roughly 40 AI calls a day" does not follow from R1.1's own table.**
    Every stop is deliberately held at a ~20-minute episode, so the honest number is the same at
    all three — 24 in an 8-hour day, 40 would need a ~12-minute episode or a 13-hour day. The
    control therefore shows an unchanged call count as attentiveness moves, which is exactly what
    R1.1's cap scaling was designed to deliver; the day length is stated in the sentence because it
    is the one assumption in it that is not read off `resolved`.
  - **A repaired preset name lands on the stop in force.** The control's selection is
    `resolved.attentiveness` (etc.) when the agent reports it, the raw config key when it does not,
    and `balanced` when neither is a known stop — so `"attentivenes": "close"` shows balanced
    selected rather than nothing.
  - **When `resolved` is absent the caption says so** rather than falling back to preset numbers:
    an agent that is down, or a host without the capture pipeline, gets *"Lore reports what these
    settings do while it's running."*
  - **The segmented controls are deliberately not disabled while a save is in flight**, unlike the
    two switches. They are a radio group, and disabling one mid-change drops keyboard focus out of
    the group; the save queue already serializes writes.
  - **No renderer test framework exists** (`app/package.json` has lint / typecheck / format only,
    and no jest/vitest), so none was invented. Verified instead by compiling `lib/capture.ts` and
    driving it with hand-built `resolved` blocks, and by rendering `SegmentedControl` through
    `react-dom/server`: the override case (preset `close`, `recaptureInterval` pinned to
    `"00:02:00"` in config.json) shows *"about every 2 minutes … roughly 11 AI calls"*, and a
    `sampleMaxChars` override shows the raw 1500 under `minimal`. `npm run lint`, `typecheck`,
    `format:check` and `package` all green. **Not verified:** the live app — the stops writing
    config, taking effect without a restart, and surviving a reload still need a running agent.

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
