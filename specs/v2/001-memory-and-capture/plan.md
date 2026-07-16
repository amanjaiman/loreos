# v2-001 — Memory Model & Capture · Plan

> SDD artifact: **how.** See [`specification.md`](specification.md) (what/why) and
> [`tasks.md`](tasks.md) (steps). Grounded in
> [`spike-findings.md`](spike-findings.md): **mem0 runs in raw-store mode
> (`infer=False`); Lore owns extraction and lifecycle.**

## Architecture

```
                        agent (C#) — the behavior layer
┌─────────────────────────────────────────────────────────────────────┐
│  capture loop (survives)          NEW pipeline                      │
│  WindowMonitor → extractors →     EpisodeBuilder → Distiller →      │
│  SensitivityFilter (untouched) →  LifecycleEngine → IMemoryService  │
│                                        │      │                     │
│                                        │      └→ DecisionTrail      │
│  RecallService ←──────────────────────┘         (SQLite, local)    │
│   (scoring, floor)                                                  │
│  REST /recall /memories /staging /episodes … ── MCP · CLI · app     │
└───────────────────────────────┬─────────────────────────────────────┘
                                │ 127.0.0.1 (typed CRUD + filtered search)
                        memoryd (Python, thinner)
                        mem0 infer=False → Qdrant on-disk
```

- **Everything the user distrusts stays where it was**: filter chain, extractor
  seams, memoryd supervision, provider layer — untouched.
- **Everything that decides** (segment, distill, stage, promote, revise, score)
  is new C# behind the agent's existing seams, unit-testable without a model.
- `SmartGate`'s per-dwell LLM-call decision dies; a cheap `RecentCaptureGate`-style
  dedupe survives as the observation intake throttle (skip unchanged windows).

## Memory schema

One mem0 memory per fact. `memory` = the first-person statement. Metadata
(all keys non-null; spike rule 4):

| Key | Type | Notes |
|---|---|---|
| `kind` | `identity·preference·state·experience·project` | closed set (spec) |
| `status` | `staged · active · archived` | recall filters `active`; `archived` covers superseded + user-deleted-by-revision; staging lives in mem0 (spike consequence) |
| `confidence` | float 0–1 | promotion sets ≥0.6; reinforcement bumps by +0.1 capped 0.95; user edit/pin sets 1.0 |
| `expires_at` | int epoch s | `state`: now + horizon (distiller suggests days, clamped 7–180; default 45). All other kinds: sentinel `4102444800`. Recall filters `gt now` |
| `established_at` / `updated_reason` | int epoch s / string | provenance for UI; `updated_reason` ∈ `promoted·reinforced·revised·user_edit·confirmed·v1_archive` |
| `reinforced` | int | supporting-episode count beyond the first |
| `episodes` | string[] | supporting episode ids (decision-trail join) |
| `pinned` / `user_edited` | bool | user authority: when either is true the LifecycleEngine may only *reinforce*, never revise/archive; conflicts surface as a confirmation card instead |
| `supersedes` | string \| "" | id of the memory this one revised |
| `v` | int (1) | schema version for future migration |

**memoryd changes (thinning):** always `infer=False`; delete `reconcile.py` (+
its tests; 002's criterion-3 contract test is superseded); `PATCH` accepts
`text?` + `metadata?` and always writes fetch-merged full metadata (wipe
footgun, spike rule 2); search validates the supported operator set and requires
`user_id`; add returns the single stored item. Everything else stands.

## Episode segmentation (pure logic, no inference)

`EpisodeBuilder` consumes post-filter observations (`CapturedObservation`) from
the existing loop:

- **Continuity:** an observation joins the open episode when same process, OR
  title Jaccard ≥ 0.4, OR text `TextSimilarity` ≥ 0.5 (existing util), AND gap
  since last observation < 3 min.
- **Near-duplicates:** text Jaccard ≥ 0.9 vs the episode's latest observation is
  counted but not kept as another sample.
- **Close:** on 10 min inactivity, continuity break (new episode opens), 45 min
  max age, or 40-observation cap. Agent shutdown flushes open episodes.
- **Episode record** (`episodes` table in the local activity store): id,
  start/end, app set, title set, observation count, and up to 8 *representative*
  text samples (first/last + most mutually dissimilar, each ≤ 600 chars) —
  bounded distiller input, auditable in the app.
- All thresholds live in `EpisodeOptions`, bound from `capture.episodes`
  (config, not code) — spec risk item.
- Closed episodes hand off through `IEpisodeProcessor` (the distiller/lifecycle
  seam; null placeholder until T005/T006), and the loop writes a `closed`
  decision row per episode regardless of processor outcome.

## Skeptical distillation (one inference call per closed episode)

`DistillPrompt` (prompt in code like `AnalysisPrompt`, not a `prompts/*.txt`
file — T005 decision): given the episode record, return
`{"facts": [{"statement", "kind", "confidence", "horizon_days"|null}], …}` or
`{"facts": []}`. The prompt: durable facts *about the user* only; **an empty
list is the expected answer** for routine activity (news, docs, ordinary
coding); never infer identity from content merely viewed; statements
first-person, self-contained, ≤ 200 chars; few-shot pairs: the wisdom-teeth
state (with `horizon_days`) and Paris-booking experience positives and a
news-reading negative. Parser
(`DistillParser`) hardens like `ObservationParser` today: malformed output →
episode marked `distill_failed`, loop never crashes. Cap: ≤ 3 facts accepted
per episode (take highest-confidence).

## Lifecycle engine (per candidate fact)

1. **Match:** `IMemoryService.Search(statement, filters: staged+active, top 5)`.
   Similarity bands (on mem0 score): ≥ 0.90 same fact; 0.75–0.90 same topic;
   below unrelated. Bands are config; the golden corpus calibrates them.
2. **Route:**
   - same fact vs **active** → *reinforce*: bump `reinforced`/`confidence`,
     extend `expires_at` for `state`, append episode id.
   - same fact vs **staged** → *promote* (budget permitting): `status: active`,
     `established_at`, confidence max(candidate, staged)+0.1.
   - same topic vs **active** → *arbitrate*: one `ArbitrationPrompt` call →
     `duplicate | supersedes | coexist`. `supersedes` → archive old (+
     `supersedes` link on new active memory). Pinned/user-edited targets are
     never auto-archived — emit a `needs_confirmation` decision instead.
   - unrelated → *stage* (`status: staged`, expires_at now + 14 d so dead
     candidates self-expire).
   - **high-signal fast path** (spec AC 4): distiller confidence ≥ 0.85 →
     promote directly, budget permitting.
3. **Budget:** promotions/day ≤ `capture.daily_budget` (default 10), counted in
   SQLite. At cap, candidates **stay staged** (deferred, not dropped) and the
   decision trail says so.
4. **Every step writes a decision row**: episode id, candidate statement,
   action (`closed·stored·reinforced·promoted·arbitrated·staged·deferred·
   distill_failed·skipped_gate·filtered`), reason, memory id. This table *is*
   the spec's explainability guarantee and the app's Activity feed.

## Recall (the hot path)

`POST /recall {query, k=5, kinds?}` →

1. memoryd search: `top_k = 3k`, filters
   `{user_id, status: "active", expires_at: {gt: now}}` (+ `kind: {in: kinds}`).
2. Local blend: `score = semantic × kindWeight × temporal × confidence`.
   Kind weights (config): state 1.15, preference/identity/project 1.0,
   experience 0.9. Temporal: 1.0 for non-experience; experience
   `max(0.6, exp(-ageDays/365))`.
3. Floor: drop blended < 0.47 (config; calibrated on the golden set — floor
   errs toward empty, spec AC 6). Return ≤ k with statement, kind,
   established_at, score.
   *Calibration note (T007):* the defaults survived the golden corpus as-is
   (floor 0.55, state 1.15, experience 0.9 with 365-day decay). One emergent
   property worth knowing: with the 0.6 decay floor, experience decay saturates
   at ~187 days — older experiences all carry the same 0.54× factor, which is
   the intent (gentle, never vanishing).

Zero generative calls; one embedding call (inside memoryd's search). Spike:
36 ms warm end-to-end locally, so cloud-embedder p50 < 500 ms holds with room.
A post-bulk warm-up query after batched writes absorbs the Qdrant settling
spike the probe saw. **MCP tool** `recall` (new, deliberately unprefixed,
alongside reworded existing tools): description explicitly instructs agents
to call it with the user's message *on every turn* and to expect (and respect)
empty results —
this text is a reviewed deliverable, not an afterthought.

## API contracts (agent, `/api/v1`)

| Endpoint | Semantics |
|---|---|
| `POST /recall` | as above |
| `GET /memories?kind&status&query&limit&offset` | library list (status defaults `active`; `staged` powers the staging UI) |
| `PATCH /memories/{id}` | `{statement?, pinned?, kind?, expires_at?}` → sets `user_edited`/`confidence:1.0`, `updated_reason: user_edit` |
| `DELETE /memories/{id}` | hard delete (user authority) |
| `POST /memories/{id}/confirm` | still-true confirmation: for `state`, extends `expires_at` by its horizon; else re-stamps `updated_reason: confirmed` |
| `POST /staging/{id}/promote` · `/dismiss` | manual promote (ignores budget — the user *is* the authority) / archive |
| `GET /episodes?since&limit` · `GET /decisions?since&limit` | Activity feed |
| `GET /system/status` | extended with today's budget/economy counters |

v1 `/memories` add/search endpoints keep working for MCP/CLI writers
(`lore remember`), now writing `kind: "preference"`-defaulted active memories
via the same lifecycle (explicit user statements are inherently high-signal).

## Migration (decided)

On first v2 start, existing mem0 rows (no `v` key) get a one-time sweep:
`status: "archived"`, `kind: "experience"`, sentinel `expires_at`, `v: 1`,
`updated_reason: "v1_archive"`. Nothing is deleted; the library's archived view
shows them; a future "distill my v1 history" batch tool is a labeled
invitation, not launch scope.

## File layout

```
agent/Capture/Episodes/{Episode,EpisodeBuilder,EpisodeOptions,IEpisodeProcessor}.cs (new; episodes/decisions tables live in agent/Storage/ActivityStore.cs)
agent/Distill/{Distiller,DistillPrompt,DistillParser,CandidateFact}.cs (new)
agent/Lifecycle/{LifecycleEngine,ArbitrationPrompt,PromotionBudget,DecisionTrail}.cs (new)
agent/Recall/{RecallService,RecallScorer,RecallOptions}.cs        (new)
agent/Api/Endpoints/{Recall,Staging,Episodes}Endpoints.cs         (new; Memories extended)
agent/Capture/{SmartGate*,CaptureAnalyzer,AnalysisPrompt,ObservationParser,…}.cs (retired with v2-002)
memoryd/lore_memoryd/{backend,routes,models}.py                   (raw mode, merge-patch, filter validation)
memoryd/lore_memoryd/reconcile.py                                 (deleted)
agent.tests/{Episodes,Distill,Lifecycle,Recall}/…                 (new suites + golden corpus JSON)
```

## Testing

- **Golden corpus** (`agent.tests/Recall/golden.json`): the wisdom-teeth and
  France journeys plus ~40 distractor memories and ~15 queries with expected
  hits/empties — drives scorer calibration and spec AC 1/2/6 as contract tests
  (fake embedder: deterministic per-text vectors so tests need no Ollama).
- **Segmentation/lifecycle/budget/parser:** pure-logic unit tests, mocked
  `IInferenceBackend`/`IMemoryService`; exhaustive routing table for the
  lifecycle bands (AC 3/4/5/7).
- **memoryd contract tests** (pinned mem0, separate CI job as today): raw-mode
  verbatim add, metadata-preserving patch, `ne`/`in`/`gt` filters, expiry
  exclusion — codifying the spike.
- **E2E acceptance harness:** scripted episode feed → live distill/lifecycle →
  recall assertions (AC 1/2), runnable locally against Ollama.
- Filter-chain tables run untouched (AC 8).

## Rollout inside this spec

Land order matches tasks.md: memoryd raw mode first (it's backward-compatible —
richer metadata simply passes through), then the C# schema/seam, then
episodes → distiller → lifecycle behind a `capture.pipeline: "v2"` config flag
defaulting on once the E2E harness is green, then recall + surfaces. The old
analyzer path is deleted only in v2-002's retirement PR, after the flag defaults
to v2 — the agent never ships without a working capture path (v2-002 risk).
