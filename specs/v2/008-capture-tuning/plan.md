# v2-008 — Capture Tuning, Retention & Recall Aggregation · Plan

## Decisions

- **Presets, not raw values, in the UI.** Three 3-stop segmented controls
  (`attentiveness` · `certainty` · `detail`). Config stores the preset *name*; the agent
  resolves it to numbers at read time. Any raw value explicitly present in `config.json`
  wins over the preset for that field alone — two controls for everyone, every knob for
  anyone who opens the file.
- **This supersedes a v2-007 decision.** v2-007 held that "timing and lifecycle options
  remain process-lifetime configuration" and kept only enable/blocklist in the live
  snapshot. That is reversed here: the snapshot carries every preset-derived value, and all
  of them apply without a restart. A preferences control that needs a restart — and drops
  the open episode doing it — is not acceptable.
- **`MaxObservations` scales with `RecaptureInterval`.** It, not the clock, ends most
  episodes today. Changing the interval alone would change episode *length* rather than
  capture density and roughly double AI spend at the `close` stop. The pairings hold every
  preset at a ~20-minute episode.
- **`balanced` is not byte-identical to today.** Re-read moves 30s → 25s and the cap 40 →
  48, per the agreed 40/25/15 range. Episode length and AI call volume hold; readings rise
  ~20%. This ships as a stated release-notes item, not a silent change.
- **Similarity thresholds stay out of the UI.** `SameFactThreshold` and
  `SameTopicThreshold` govern whether two statements are the same fact — a correctness
  property of dedup and arbitration, not taste. `certainty` moves only
  `HighSignalConfidence` and `StagedTtlDays`.
- **Detail changes depth, never fact count.** A higher setting must not split one event
  into several facts, and must never mint a `preference` from a single observed choice.
  Lore records what happened; the consuming agent infers what it means. This is why R5
  exists — the inference needs the whole set at recall time.
- **Defects are fixed for everyone.** Discarded `ContentType`, dwell reset by title churn,
  and time-blind sample selection are not settings. Nobody would choose the worse side. We
  do not ship controls that let users opt out of bugs.
- **`raw_captures` stays off by default.** Unbounded it is 2–5 GB/year for a debugging
  table. It becomes an opt-in diagnostic hard-bounded to 24h / 500 rows. Most tuning
  questions are already answered by `episodes` + `decisions`.
- **Retention defaults to 90 days and never touches memories.** Pruning evidence must not
  delete what the evidence supported; the evidence endpoints tolerate the resulting gaps.

## Sequencing

**T001 gates T005.** The recall-floor check can invalidate the detail control's entire
premise — if the eighth-most-recent booking does not clear the floor, a higher `k` returns
nothing extra and R1.3 does not deliver its use case. It is cheap and goes first.

**T003 gates everything user-facing.** The live snapshot is the plumbing all three controls
ride on. Landing it early, resolving to today's defaults, means it can be verified in
isolation before any preset exists.

**T002 is independent.** The defect fixes touch only `agent/Capture` and can land in
parallel with the plumbing. They are grouped into this spec for framing, not coupling. Land
T002 before T005 if both are in flight — both touch `DistillPrompt`, and T002's change is
smaller.

## Verification

- Agent unit tests cover preset resolution (including raw-override precedence and garbage
  preset names), the live snapshot swap, retention pruning, and the three defect fixes.
- The golden corpus is re-run for any preset whose prompt text differs; the E2E acceptance
  harness pins `balanced`. Temperature 0 is unchanged.
- The recall-floor check (T001) is a seeded-store test, not a manual inspection — eight
  flight bookings across two years, all returned at `k = 30`.
- App lint, typecheck, and package build cover the renderer.
- **Live app check is the acceptance test, not the suite.** Move `attentiveness` to `close`
  with a browser focused, watch the observation rate in `GET /episodes`, and confirm AI
  calls per hour have *not* doubled — that is the failure mode the cap scaling exists to
  prevent, and no unit test will catch it.
- Consequence lines are verified against a hand-edited `config.json` override, not just
  against preset names — a control that lies under an override is the likeliest UI defect.
