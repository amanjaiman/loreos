# v2-008 — Capture Tuning, Retention & Recall Aggregation · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> **Audience:** an agent implementing the C# agent, the local API, and the Settings renderer.
> **Shape:** three user-facing presets, the plumbing to apply them live, bounded storage,
> one recall fix, and three defects that are **not** settings.

## Why this exists

Capture behaviour today is a single fixed point: poll every 2s, dwell 4s, re-read the same
window every 30s, close an episode at 40 observations, promote a fact at 0.85 confidence,
cap a statement at 200 characters. Those numbers are reasonable defaults and wrong for
somebody. A user on battery wants Lore to look less often. A user with a cloud provider is
paying per episode. A user who books a lot of travel wants seat and fare detail in the
record; a privacy-minded user wants the opposite.

Lore is open source and local-first, so the answer is not for us to pick better numbers —
it is to let the user pick, in language they can act on, without turning Settings into a
control panel of Jaccard thresholds.

**The dividing line this spec enforces:** a slider is for a genuine preference with an
honest tradeoff on both sides. Anything where one setting is simply *worse* is a defect and
gets fixed for everyone (R6). We do not ship settings that let users opt out of bugs.

---

## R1 — Three presets in Settings → Capture & Privacy

Three discrete 3-stop controls, not continuous sliders — a 0–100 value has no meaning to
the user here. Each control shows a **live plain-English consequence line** beneath it;
that sentence is what makes the control understandable, more than the label is.

### R1.1 — "How closely Lore watches" (`attentiveness`)

The tradeoff: **how much Lore notices vs. battery, CPU, and OCR work.**

**Correction (T006): this control is not an AI-spend dial.** Earlier drafts said it was.
Because the cap scaling below deliberately holds every stop at a ~20-minute episode, and one
closed episode is exactly one distill call, **AI call volume is roughly the same at all three
stops** — about 24 in an 8-hour day. What actually moves is the *reading* rate (poll 5s → 2s,
re-read 40s → 15s), which is CPU, OCR and battery. The flat call count is a feature — it is
precisely what the cap scaling was designed to deliver — but it must not be sold as a cost
slider, and a consequence line claiming otherwise would be false.

| | `light` | `balanced` (default) | `close` |
|---|---|---|---|
| `PollInterval` | 5s | 2s | 2s |
| `DwellThreshold` | 10s | 4s | 3s |
| `RecaptureInterval` | 40s | 25s | 15s |
| `TitleRecaptureInterval` | 15s | 10s | 6s |
| `Episodes.MaxObservations` | 30 | 48 | 80 |
| `Episodes.MaxSamples` | 6 | 8 | 12 |

`PollInterval` is deliberately 2s at both `balanced` and `close` — the `close` stop buys
its extra attention through dwell, re-read, and the episode bounds, not by spinning the
foreground poll faster. It reads like a typo and is not one.

`TitleRecaptureInterval` (added by T003, sized here) holds retitling windows at a constant
**~2.5× the read rate of a window sitting still** at every stop. Pinning it at one value
across all three would make a `light` user read retitling windows 4× more often than
everything else — the cost blow-out the knob exists to prevent. Implementations should
assert the *ratio*, not the three literals, so retuning `RecaptureInterval` cannot silently
break the pairing.

`MaxObservations` **must** scale with `RecaptureInterval`. It — not the clock — is what
ends most episodes today, so changing the interval alone changes episode *length* instead
of capture density: at 15s re-reads with a 40-observation cap, the day fragments into
~10-minute episodes, each giving the distiller less context and roughly doubling AI spend.
The pairings above hold every preset at a **~20-minute episode**, so the control changes
evidence granularity, not episode shape, and AI call volume stays roughly flat across all
three stops.

`ContinuityGap` (3 min), `IdleTimeout` (10 min) and `MaxAge` (45 min) are unchanged by this
control.

**Note for the release notes:** `balanced` is not byte-identical to today — re-read moves
30s → 25s and the cap 40 → 48. Episode length and AI call volume are unchanged; readings
rise ~20%, so existing users see slightly more CPU/OCR after upgrade. This is deliberate
(it keeps the three stops in a usable band) and must be stated, not slipped in.

### R1.2 — "How sure Lore has to be" (`certainty`)

The tradeoff: **fewer, surer memories vs. broader coverage with more noise.** Reversible in
both directions — nothing promoted is deleted, and nothing withheld is lost; it waits in
staging where the user can see it.

| | `strict` | `balanced` (default) | `eager` |
|---|---|---|---|
| `HighSignalConfidence` | 0.90 | 0.85 | 0.70 |
| `StagedTtlDays` | 7 | 14 | 30 |

`SameFactThreshold` (0.90) and `SameTopicThreshold` (0.75) are **not** touched by this
control and must not be exposed. They govern whether two statements are *the same fact* —
a correctness property of dedup and arbitration, not a matter of taste. Moving them to make
Lore "more eager" corrupts the store rather than filling it.

### R1.3 — "How much detail" (`detail`)

The tradeoff: **richer records vs. less specific data on disk.** The low stop is a privacy
choice, not just a cheaper one.

| | `minimal` | `balanced` (default) | `rich` |
|---|---|---|---|
| `capture.statementMaxChars` | 120 | 200 | 500 |
| `Episodes.SampleMaxChars` | 400 | 600 | 900 |
| Prompt detail directive | terse | today's wording | specifics-first |

The statement cap is a top-level `capture` key, beside the nested `episodes.sampleMaxChars`
(T004). `DistillPrompt` currently hardcodes "under 200 characters" in its system prompt —
that literal is what T005 templates from this value.

Both caps move together: the model cannot write "seat 14C" if the sample it read was
truncated before that text. Raising the output cap without the input cap produces longer
statements with no more information in them.

Illustrative output for one flight booking:

| Stop | Statement |
|---|---|
| `minimal` | I booked a flight to San Diego. |
| `balanced` | I booked a United flight to San Diego for Sep 9–13. |
| `rich` | I booked United UA 2411 to San Diego Sep 9–13, seat 14C aisle, one checked bag, $312. |

**Binding rule — detail changes depth, never fact count.** A higher detail setting must not
cause the distiller to split one event into several facts, and in particular must never
mint a `preference` from a single observed choice. One aisle seat is not a preference for
aisle seats; storing it as one is the over-generalization the distill prompt already
forbids ("never infer identity traits from a single page view"), and it actively destroys
data: two such memories fall in the same-topic band, go to arbitration, and one supersedes
the other — so eight bookings collapse into a single flip-flopping preference instead of
eight records.

**Lore records what happened; the consuming agent infers what it means.** An agent booking
a flight should see eight `experience` rows and conclude "mostly window seats, though the
last was an aisle." That inference belongs at recall time with the whole set in view, not
at capture time with one episode in view. R5 exists to make that possible.

Detail also *helps* dedup rather than threatening it: "I booked a flight" against "I booked
a flight" scores near-identical and risks being merged as one repeatedly-reinforced blur,
while "UA 2411 to San Diego Sep 9–13" against "DL 88 to Boston Mar 2" stays distinctly two
records. This is a positive reason for a frequent traveller to turn the control up.

### R1.4 — UI placement and shape

- All three live in `app/src/renderer/views/settings/CapturePrivacy.tsx`, in a new card
  below the existing capture toggle and above the blocklist.
- The design system has no 3-stop control today (`Card`, `Switch`, `ChipListEditor` only);
  add a `SegmentedControl` to the vendored DS rather than improvising in the view.
- **The controls carry no explanatory text.** Label plus three stops, nothing beneath.

  This reverses the original requirement, on the evidence of the built pane. R1.4 first
  called a derived "consequence line" per control *the highest-leverage part of this task* —
  *"Lore reads your screen about every 25 seconds, once a window has held your attention for
  4 seconds — roughly 24 AI calls in an 8-hour day."* Built and looked at, three of those
  stacked read as a spec sheet: precise, honest, and not what someone opening Settings wants.
  The labels and stop names were written to carry their own meaning, and they do.

  `docs/privacy.md` explains what the three controls change, for anyone who wants it. That is
  the right home for the detail — reference material you seek out, not a caption you must
  read past every time.

- **The selected stop still comes from `resolved`**, so a misspelled preset name in
  `config.json` shows the stop actually in force rather than nothing. Dropping the captions
  removes the requirement that they stay truthful under a raw override; it does not remove
  the need for the control itself to reflect reality.
- The diagnostics switch and the retention note keep their one-line descriptions. They are
  not tuning stops: one turns on recording of what Lore reads, the other says how long
  evidence is kept. Both are statements about data, and a switch with privacy consequences
  should say what it does.
- Saves immediately on change, through the existing `api.patchConfig` queue in that file.

**Acceptance:** changing any control writes config, takes effect without an agent restart
(R2), and survives an app reload; the consequence line reflects a raw override typed into
`config.json`, not the preset name.

---

## R2 — Presets apply live, without a restart

Today `CaptureOptions` is bound once at startup and injected as a singleton, and
`LiveCaptureSettings` deliberately carries only `enabled` + blocklist — its own summary
says timing and lifecycle thresholds are "fixed for a process lifetime". A settings control
that requires a restart is not acceptable UX for a toggle in a preferences pane, and a
restart drops the open episode.

**Wanted:** `LiveCaptureSettings` carries a full resolved snapshot, replaced atomically on
`PATCH /config` exactly as the blocklist is today.

Sites that must read live instead of from the startup singleton:

| Site | Today | Change |
|---|---|---|
| `CaptureAgent` poll delay + `ShouldProcess` | reads `_options` per tick | point at the live snapshot |
| `WindowMonitor` | dwell threshold is a constructor field | read per `Poll()` |
| `EpisodeBuilder` | `EpisodeOptions` singleton | read per `Add` / `CloseIfIdle` |
| `LifecycleEngine` | reads `_options.X` per call | point at the live snapshot |
| `DistillPrompt.Build` | static, takes `Episode` only | takes the detail level too |

Most of these already re-read their options per use, so this is largely a pointer swap;
`WindowMonitor` and `EpisodeBuilder` are the two that need real changes.

A threshold change mid-episode is harmless and must not be special-cased: the new value
simply applies from the next observation. An in-flight episode is never discarded or
force-closed by a settings change.

**One documented exception (found by T005): `SampleMaxChars` applies retroactively.**
`EpisodeBuilder` holds full sample text in memory and truncates only at episode close, so
moving the `detail` control mid-episode governs samples already collected rather than just
future ones. Harmless in both directions — raising it yields more text, lowering it less, and
the sensitivity filter has already run on all of it — but it is a genuine deviation from the
rule above and is recorded rather than quietly tolerated.

**Acceptance:** with the agent running, moving `attentiveness` to `light` measurably slows
the observation rate within one poll cycle, with no restart and no lost episode; a
`PATCH /config` that touches only `provider` leaves capture timing untouched.

---

## R3 — Config schema: presets stored, raw overrides win

Store the **preset name**, not the resolved numbers. The agent resolves preset → values at
read time.

```json
"capture": {
  "enabled": true,
  "attentiveness": "balanced",
  "certainty": "balanced",
  "detail": "balanced",
  "blocklistApps": [],
  "blocklistKeywords": [],
  "episodes": { "maxObservations": 64 }
}
```

**Binding rule:** any raw value explicitly present in `config.json` **wins over the preset**
for that field only. Two controls for everyone; every knob for anyone who opens the file.
That split is the point — it is what makes "full control" true without a wall of options.

`GET /config` additionally returns a **read-only** `capture.resolved` block containing the
effective values, so the UI can render honest consequence lines and a power user can see
what a preset actually means.

**`resolved` must echo the writable keys exactly — same names, same value formats.**

```json
"capture": { ..., "resolved": { "pollInterval": "00:00:02", "recaptureInterval": "00:00:25",
  "episodes": { "maxObservations": 48 }, "highSignalConfidence": 0.85 } }
```

The original draft of this spec had `resolved` report `pollIntervalSeconds: 2` as a plain
number while the writable key is a TimeSpan string (`"pollInterval": "00:00:02"`). That is a
trap: the obvious workflow for a tinkerer is to read `resolved`, copy a line into `capture`
to pin it, and the binder then reads `2` as **two days**. Echoing the writable shape makes
copy-a-line-to-pin-it correct by construction, which is the whole point of the
preset-plus-override design. (T003 added a one-minute ceiling on `PollInterval` that catches
this particular mistake, but the shape should not invite it.)

`PATCH /config` must ignore or reject `capture.resolved` rather than persisting it.

**Absent is not the same as default.** For "delete the raw key and the preset's value
returns" to work, resolution has to distinguish a key that is *explicitly present* from one
that is *absent* — which is the same distinction raw-override-wins needs. T003's live
snapshot deliberately kept an absent key's current value in force; T004 replaced that
fallback base with the resolved preset.

Three things T004 established that this section originally left open:

- **Presence must be read where it is still visible.** Once options are bound, a value equal
  to the built-in default is indistinguishable from an absent key, so resolution cannot live
  inside `LiveCaptureSettings`'s constructor or `CaptureSnapshot.From`. It happens at the two
  points that can still see presence: the startup binder (which writes only what the config
  actually has) and the merged `capture` JSON object on the live path.
- **`resolved` excludes the blocklists and reports repaired preset names.** A UI rendering
  honest consequence lines needs the full effective set, but duplicating potentially long
  user-data arrays into every `GET` buys nothing. A misspelled `"attentivenes"` shows as
  `"attentiveness": "balanced"` under `resolved`, so T006's control lands on the stop
  actually in force rather than on nothing.
- **The blocklist is the one field an absent key does not revert.** No preset touches it, so
  there is nothing to revert *to*, and the only question is which way to fail: keeping what
  is in force means Lore goes on filtering, while clearing it means capturing the thing the
  user most wanted left alone. Deliberate clearing still works, because the editor sends an
  empty array — which is present, not absent.

**Accepted cost:** a timing set through a configuration source other than `config.json` (an
environment variable, say) now survives only until the first capture `PATCH`. That is the
price of a delete that actually reverts.

Unknown or misspelled preset names fall back to `balanced` and log a warning — never crash
the agent on a hand-edited config.

**Acceptance:** a config with `"attentiveness": "light"` plus an explicit
`episodes.maxObservations` uses light's timings and the explicit cap; deleting the explicit
key restores light's cap without an app restart; a garbage preset name yields balanced.

---

## R4 — Bounded storage: retention and opt-in raw captures

`ActivityStore` has **no pruning of any kind** — no `DELETE`, no retention window, no
vacuum. `episodes`, `decisions`, and `activity_log` grow for the life of the install. For a
local-first app whose promise is that your data stays on your machine, "forever" is the
wrong default: the user cannot see the growth and never agreed to it.

**R4.1 — Retention.** A `capture.retentionDays` setting, default **90**, pruning
`episodes`, `decisions`, and `activity_log` older than the window. Pruned on agent start and
once daily thereafter. `0` means keep forever, for users who want it.

At balanced settings an episode costs roughly 6 KB (8 samples × 600 chars plus titles), so
a working day is a few hundred KB and an unbounded year is ~100 MB. Not alarming, but not
the user's choice today.

Memories themselves are **not** touched by retention. Pruning evidence must not delete what
the evidence supported.

**R4.2 — `raw_captures` stays off.** The table, its schema, and `GET /recent` exist, but
nothing in the v2 loop ever calls `LogRawCaptureAsync`. Do not simply switch it on: at
balanced settings that is ~1,000–2,000 readings a day at a few KB each — **5–15 MB/day,
2–5 GB/year** — for a debugging table.

Instead: a `capture.diagnostics` toggle, **off by default**, exposed in Settings as a small
"Record what Lore reads (for troubleshooting)" switch, and **hard-bounded** to the last 24
hours or 500 rows, whichever is smaller, pruned on the same schedule. Tens of MB, not
gigabytes. Its purpose is answering "why isn't Lore seeing this app" — an occasional
question, not an always-on need.

Most tuning questions are already answered without it: `episodes` stores exactly what the
distiller saw and `decisions` records why each episode did or did not produce a fact.

**R4.3 — Dangling evidence.** Pruning episodes leaves ids in `metadata.episodes` pointing
at rows that no longer exist. `GET /memories/{id}/evidence` and the queue card's evidence
line must tolerate missing episodes and render what survives, never error.

**Acceptance:** with `retentionDays: 1`, episodes older than a day are gone after a restart
and the memories they supported are intact and still recallable; `GET /recent` returns
empty with diagnostics off; with diagnostics on, row count never exceeds the bound.

---

## R5 — Recall must be able to return a set, not just the top match

R1.3's design — Lore records, the agent infers — only works if recall hands the agent
enough records to see a pattern. Today it does not:

- `RecallOptions.DefaultK` is **5**, and the MCP `recall` tool defaults `k = 5`.
- The tool description frames recall as memories "relevant to their CURRENT message …
  strongest first". Nothing tells a calling agent it may raise `k` to look across history.

So an agent asked to book a flight calls `recall("book a flight to Denver")`, gets five
items — perhaps two past flights and three unrelated facts — and never sees the pattern.
The eight bookings are correctly stored and effectively invisible.

**R5.1 — Invite aggregation in the tool description.** Add to `LoreTools.Recall`'s
`[Description]`: when the user's request resembles something they have done before, raise
`k` (20–30) and consider filtering to `experience` to see the pattern rather than the
single closest match. This string is model-facing and drives tool selection — change it
deliberately, and keep the existing every-message framing intact.

Confirm `NormalizeLimit` permits `k` up to at least 30.

**R5.2 — Verify the floor before shipping the detail control.** `Floor` is 0.47 and an
`experience` is weighted 0.9 with 365-day decay (floored at 0.6), so an older booking is
weighted down twice. Check against the golden corpus that the eighth-most-recent
flight-shaped memory still clears the floor on a flight-shaped query. If it does not, a
higher `k` returns nothing extra and R1.3 does not deliver its use case.

**This verification gates R1.3** — do it first; it is the item that decides whether the
whole pattern works.

**Acceptance:** a store seeded with eight flight bookings across two years returns all
eight for a flight-shaped query at `k = 30`, in recency-weighted order, none dropped by the
floor.

### R5.3 — The floor applies to semantic relevance, not to the blend

**T001 ran this check and it failed: only 3 of 8 bookings returned.** The cause is not
specific to flights, and not caused by anything else in this spec.

`RecallScorer.Blend` is `semantic × kind × temporal × confidence`, and `RecallService`
compares that **blended** value against `Floor`. For an `experience`, `TemporalFactor`
saturates at `ExperienceDecayFloor` (0.6) once the memory is ~187 days old. So past six
months the best any experience can score is:

```
semantic × 0.9 (ExperienceWeight) × 0.6 (decay floor) × confidence
```

against a floor of 0.47. That needs `semantic ≥ 0.870` at confidence 1.0, `≥ 0.967` at the
0.9 a real booking earns — and at confidence ≤ 0.87 it is **arithmetically impossible**,
since `0.54 × 0.87 = 0.4698` is already under the floor. Measured similarity between a
flight query and a stored booking is ~0.82 with `nomic-embed-text`, because the embedder
separates topics, not instances within a topic.

Two consequences worth stating plainly:

- **This is a latent v2-001 defect, not a v2-008 one.** `RecallScorer.cs` documents the
  intended invariant — "experiences fade gently with age but never vanish — 'visited
  France' still matters on a travel query years later (spec AC 2)." The decay floor exists
  to guarantee exactly that, and the recall floor sits above where the guarantee lands. The
  invariant is not delivered today. Every experience older than roughly six months is
  unrecallable; below ~0.87 confidence, unrecallable at any similarity.
- **It is not fixable by lowering `Floor`.** That value is calibrated to keep unrelated
  pairs out and lowering it degrades every other recall.

**Decision: separate the two jobs the floor is doing.** "Is this relevant?" is a semantic
question; "how should this rank?" is what the blend is for. Conflating them lets *age* make
a *relevant* memory invisible.

```
now:    if (blended  >= Floor) keep;  order by blended
wanted: if (semantic >= Floor) keep;  order by blended
```

An aged booking then returns on its 0.82 semantic score and ranks last on its 0.40 blend —
the intended behaviour. Unrelated pairs sit at ~0.44 semantic and are still excluded by the
same 0.47 threshold, so the floor keeps doing the job it was calibrated for.

**This changes what passes the floor for every kind, so the golden corpus must be re-run
and re-calibrated, not merely re-asserted.** Treat any newly-passing unrelated pair as a
calibration failure to investigate, not a number to update.

**Acceptance:** all eight bookings return at `k = 30`, ordered recent-first; the existing
golden-corpus relevance cases are unchanged; no unrelated pair newly clears the floor.

---

## R6 — Defects fixed for everyone (not settings)

Nobody would choose the worse side of these. They are not presets.

**R6.1 — `ContentType` is computed and discarded.** `ContentClassifier.Classify` runs on
every observation and is stored on `CapturedObservation`, but `Episode` does not carry it,
so the distiller never learns an episode was shopping rather than reading. Put the
episode's content-type mix on `Episode` and into the prompt. We already pay for this
signal.

**R6.2 — Title churn can block capture entirely.** Any title change resets the dwell timer
in `WindowMonitor.Poll()`. An app that rewrites its title faster than the dwell threshold —
media players, terminals with progress output, chat apps with unread counters — never
dwells and is never captured, with no log row saying so. Reset dwell on **window** change;
treat a title change within the same window as continuing dwell.

**R6.3 — Sample selection ignores time spent.** `EpisodeBuilder.SelectSamples()` greedily
picks the observation *least* similar to those already chosen. Near-duplicates are counted
and dropped, so fifteen minutes on one document contributes one snippet and a thirty-second
glance contributes another of equal weight. Dwell time never reaches the prompt at all.
Weight selection by time-spent alongside diversity, so the evidence reflects where the day
actually went.

**R6.2 has a consequence this spec originally missed.** Dwell was accidentally acting as the
rate limiter for retitling windows. Once a title change no longer resets it, `ShouldProcess`
keys on `handle + title`, so a window retitling every poll is extracted on *every poll* —
~1,800 readings/hour at 2s polling against ~144 at a 25s re-read, with OCR on the expensive
path, and nearly all of it absorbed downstream as near-duplicates. **A title-only re-read
needs its own minimum gap**, carried in the live snapshot (T003). Capturing these windows at
all is the fix; capturing them 12× more often than any other window is not.

**Acceptance:** a window whose title changes every 2s is captured, *and* is not re-extracted
more often than the title-only gap allows. For sample selection, the episode must present
**more candidate samples than `MaxSamples` slots** — with 8 slots and most episodes yielding
fewer than 8 distinct samples, selection never has to choose and the old algorithm passes
too. Pin a sample budget below the candidate count so the assertion discriminates.

---

## R5.4 — Two consequences of R5.3, decided

Moving the floor to the semantic axis had two effects beyond the age fix. Both were found by
T010 and decided deliberately.

**Confidence no longer gates inclusion.** It was a factor in the blended score, so a
low-confidence memory used to be excluded as a side effect. On the semantic axis it only
affects rank — a 0.3-confidence "I might be coming down with a cold" now returns. That
weakens the contract `RecallService` states for itself: *"a client must be able to trust
that whatever comes back is worth context space."*

**Decision: add `RecallOptions.MinConfidence`, default 0.5**, as a second inclusion gate
beside the semantic floor. Keep if `semantic >= Floor AND confidence >= MinConfidence`; order
by the blend. Real bookings sit at ~0.9 so the age fix is untouched; the wisdom-teeth case
(0.8 in the corpus) still surfaces; near-speculation does not.

**Recency ordering was not delivered.** All eight bookings return, but past ~187 days
`TemporalFactor` is pinned at `ExperienceDecayFloor` for every row, so age stops contributing
and the tail sorts by embedder noise (measured: `1mo, 3mo, 5mo, 16mo, 20mo, 12mo, 8mo, 24mo`).

**Decision: lower `ExperienceDecayFloor` from 0.6 to 0.2.** That floor existed to stop old
memories vanishing; R5.3 moved that guarantee to the semantic floor, so the decay floor is now
free to do its real job — ordering — for far longer. Saturation moves from ~187 days to ~587
(`365 × -ln(floor)`), and it can no longer cause exclusion.

**Acceptance:** the eight bookings return in strict recency order; a 0.3-confidence memory is
excluded while a 0.7-confidence one is kept; no unrelated pair clears both gates.

---

## Constraints (non-negotiable)

- **Local only.** No new egress. Presets, retention, and diagnostics are all local state
  (constitution §1, §4.3).
- **The filter chain is untouched.** Nothing here bypasses, reorders, or weakens
  `SensitivityFilter`. No preset may make Lore capture something the blocklist or the
  sensitive-pattern layer would have dropped — including the `rich` detail stop.
- **No restart for a settings change.** R2 is a hard requirement, not an optimisation.
- **Balanced is the upgrade path.** A config written before this spec, with no preset keys,
  must resolve to `balanced` on every control with no user action.
- **Temperature 0 stays.** The distill prompt varies by detail level but remains
  deterministic: the same episode at the same stop renders byte-identically.
- **`balanced` must render byte-for-byte identical to the pre-v2-008 prompt**, apart from the
  templated statement cap. That equivalence is the proof the refactor did not silently change
  what Lore remembers for every existing user, and it is worth more than any other assertion
  in R1.3.
- **Note: "re-run the golden corpus per preset" was wrong** and is corrected here.
  `agent.tests/Recall/GoldenCorpus.cs` is a *recall* fixture — hand-authored embedding
  similarities calibrating `RecallScorer`/`RecallService`. Nothing in it touches
  `DistillPrompt`. The only harness that pins prompt behaviour is the opt-in E2E test
  (`LORE_TEST_E2E=1`, needs a live model), and it pins `balanced`. **A behavioural check of
  `minimal` and `rich` against a live model is therefore still outstanding** and belongs with
  T009's live app check — unit tests can prove the prompt text changed, not that the model
  responds to it as intended.

## Out of scope

- Exposing similarity thresholds, `MatchNeighbors`, `DailyBudget`, `MaxAge`, or
  `ContinuityGap` in the UI. They stay in `config.json` for tinkerers.
- The episode-grain problem — same-app observations merging unrelated browser activity into
  one episode — is real and is **not** a slider. It needs a topic-shift split, which is its
  own spec.
- Multi-user config. Presets are per-install, like everything else.
- Any change to memory storage, the memoryd seam, or the MCP transport.

## Handoff notes

- **Order: R5.2 → R2 → R1 → R3 → R4 → R6.** R5.2 is a verification that can invalidate
  R1.3's design, so it goes first and is cheap. R2 is the plumbing everything user-facing
  depends on. R6 is independent of the rest and can ship in parallel or as its own PR — it
  is grouped here for the framing, not because it is coupled.
- R6.1 and R1.3 both touch `DistillPrompt`; sequence them to avoid a collision, or land
  R6.1 first since it is smaller.
- The consequence lines in R1.4 are the highest-leverage part of the UI and the easiest to
  get wrong. Write them against resolved values from the start; retrofitting them onto
  preset names will produce a control that lies to anyone with an override.
- Verify against the real app, not just tests: move `attentiveness` to `close` with a
  browser focused and watch the observation rate in `GET /episodes`, then confirm the AI
  call count per hour has not doubled — that is the failure mode R1.1's cap scaling exists
  to prevent.
