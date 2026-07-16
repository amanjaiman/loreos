# v2-001 — Memory Model & Capture · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> First spec of the v2 restart. Supersedes the memory-shaping intent of specs 002/003;
> the reset mechanics (what code survives, spec archive) are defined in v2-002.

## Overview

Lore v1 (specs 001–013) built a working pipeline but the wrong product shape: it is
an **activity log**. Every 15-second dwell produces an observation like *"I'm reading
about the French Revolution"*, stored forever, retrievable only when a client
explicitly searches. The result is a large pile of low-value, time-stamped activity
snapshots that no agent knows when to ask for.

v2 makes Lore a **memory layer**: a small, curated set of durable, first-person facts
about the user — who they are, what they're going through, what they like, what
they've done — surfaced to any AI agent at the moment the fact is relevant, the way
ChatGPT and Claude memory work. Two motivating incidents define the bar:

- **Wisdom teeth.** The user mentioned tooth pain days ago. Today they ask an
  unrelated question about ordering takeout. The agent should receive *"recovering
  from wisdom tooth extraction"* as relevant context — cross-domain, unprompted —
  and can advise against chewy food.
- **France.** The user took a trip to France in the past. When they later ask for
  trip ideas, the agent should receive *"visited France (spring 2026)"* and not
  re-suggest it.

Three consequences drive everything below:

1. **Memories are facts, not observations.** A memory is a durable statement about
   the user with a kind, a lifespan, and a lifecycle — not a diary entry.
2. **Capture is skeptical, not eager.** The default outcome of watching the screen
   is *nothing*. A fact is stored only when the evidence supports it, and the
   day's output is measured in a handful of memories, not hundreds.
3. **Recall is ambient, not pull-only.** Retrieval must be cheap and fast enough
   that a client calls it on **every user message**, and precise enough that what
   comes back is worth injecting into an agent's context.

## The memory model

A **memory** is a first-person factual statement (e.g. *"I'm recovering from wisdom
tooth extraction (mid-July 2026)"*) with:

- **Kind** — one of a closed set that determines default lifespan and recall weight:
  - `identity` — stable traits (job, family, home city). Long-lived.
  - `preference` — tastes, opinions, working styles. Long-lived, revisable.
  - `state` — temporary conditions (recovering from surgery, job hunting,
    training for a marathon). **Decays**: has an expected horizon after which it
    stops surfacing and is flagged for confirmation or archival.
  - `experience` — episodic past events (visited France, shipped a launch).
    Permanent but recency-weighted.
  - `project` — ongoing endeavors with an active/dormant/done lifecycle.
- **Temporal validity** — created-at plus, for `state`, an expected horizon;
  "current" vs "past" is explicit, never inferred from timestamps by the reader.
- **Provenance & confidence** — which capture episodes support it and how strongly.
  Repeated supporting evidence reinforces; a memory seen once is held at lower
  confidence than one confirmed across days.
- **Lifecycle** — new evidence can **reinforce** (same fact again), **revise**
  (*"pain is gone"* supersedes *"in pain"* — the old fact is archived, not deleted),
  or **contradict** (conflicting facts trigger LLM-arbitrated resolution at capture
  time, never at recall time).
- **User authority** — the user can edit, delete, or pin any memory; a user edit
  outranks all inferred evidence and is never silently overwritten by capture.

mem0 remains the store (constitution §1.5); this model rides on mem0's
add/update/search events plus Lore-owned metadata (kind, horizon, confidence,
provenance). Lore's vocabulary stays mem0-agnostic behind `IMemoryService`.

## Capture: from dwell-triggered to evidence-triggered

The v1 sensitivity filter chain (blocklist → structural → regex) and the extractor
seams survive unchanged — they are trust-critical and proven. What changes is
everything after filtering:

1. **Episodes, not snapshots.** Raw window observations accumulate locally into an
   **episode** — a contiguous stretch of related activity (a research session, a
   booking flow, a long chat). Episodes, not individual dwells, are the unit of
   analysis. Cheap local signals (window/title continuity, text similarity, time
   proximity) build episodes without any inference calls.
2. **Skeptical distillation.** When an episode closes (activity moves on) it is sent
   to the user's model with the v2 question: *"What durable fact about this user, if
   any, does this episode support?"* The prompt's default answer is **nothing** —
   reading the news, routine coding, or idle browsing produces no memory. The
   distiller returns candidate facts typed by kind, or an explicit no-op.
3. **Evidence accumulation.** A candidate fact is not immediately a memory. It is
   staged; a second supporting episode (or one very-high-signal episode, e.g. a
   confirmed purchase or booking) promotes it. Staging prevents one misread page
   from becoming a "fact" about the user.
4. **A daily budget, honestly reported.** Promotion is capped (order of ~10 new
   memories/day, configurable). The cap forces ranking — only the strongest
   candidates promote — and bounds inference spend on metered keys. Capture metrics
   report what was considered, staged, promoted, and dropped, so the user can always
   answer "why did/didn't Lore remember that?" (no silent truncation).

## Recall: the every-turn contract

Recall is a new surface with a hard performance contract, because its consumer calls
it on every user message:

- **Input:** the user's message (or an agent-built query) + optional client hint.
- **Output:** ≤ K (default 5) memories ranked by blended relevance — semantic
  similarity × kind weight × temporal validity (expired `state` never surfaces;
  `experience` decays gently) × confidence — each with its statement, kind,
  when-established, and score.
- **No generative model call in the recall path.** Recall makes at most one
  embedding request (the query, via the user's configured embedder) and zero
  generative-inference requests; ranking is vector search + local scoring math.
  Local processing adds < 50 ms; end-to-end targets are p50 < 500 ms with a cloud
  embedder and p50 < 150 ms with a local one. Whether to offer a bundled local
  query-embedder as a latency upgrade is a `plan.md` decision, not assumed here.
- **Relevance floor.** Below-threshold matches return empty rather than padding to
  K — an empty result is the common, correct case, and clients must be able to
  trust that what returns is worth context-window space.
- Served through the local REST API and MCP (constitution §8), with tool
  descriptions written so agents call it ambiently (per message), not only when the
  user says "check my memory". The MCP tool description is part of the spec's
  deliverable: it is the recall contract's marketing to the calling agent.

## User stories

- **As a user**, days after Lore learned I'm recovering from wisdom tooth
  extraction, any connected agent I ask about food gets that context and adapts —
  without me repeating myself. (Wisdom-teeth test.)
- **As a user**, when I ask a connected agent for travel ideas, it knows I already
  visited France and doesn't re-suggest it. (France test.)
- **As a user**, my memory library reads like a profile — dozens of curated facts I
  recognize as true about myself — not thousands of "I looked at a webpage" entries.
- **As a user**, when something stops being true (my tooth heals, I change jobs),
  Lore stops surfacing the stale fact — by observing the change, by horizon expiry,
  or by my one-click correction.
- **As a privacy-sensitive user**, everything still passes the full filter chain
  before analysis or storage, and staged candidates are inspectable and deletable
  like memories.
- **As a user on a metered key**, my inference spend tracks genuine episodes (tens
  per day), not wall-clock dwells (thousands), and the daily budget caps worst-case
  cost.

## Scope

### In scope

- The memory schema (kind, temporal validity, confidence, provenance, lifecycle,
  pin/edit authority) and its mapping onto mem0 + Lore metadata.
- Episode segmentation, skeptical distillation prompt, staging/promotion, daily
  budget, and capture metrics for the full decision trail.
- Revision/contradiction handling at capture time.
- The recall endpoint + scoring model, exposed via REST and MCP, including the MCP
  tool descriptions.
- The full app-facing local-API resources for the new objects: memories (list /
  edit / delete / pin / confirm-expire), staging (list / promote / dismiss), and
  the episode/decision feed. v2-003 renders these; this spec defines them.
- Migration: existing v1 mem0 data is either distilled into v2 memories by a
  one-time offline pass or archived read-only — decided in `plan.md`; silent
  deletion is not an option.

### Out of scope

- The app UI for the memory library (v2-003 design spec; this spec only guarantees
  the data supports view/edit/delete/pin).
- Provider layer changes (004 survives as-is; both distillation and embeddings ride
  the existing seams).
- The repo/spec reset mechanics (v2-002).
- Non-Windows capture; capture sources beyond the active window (browser
  extensions, file watchers — future specs).
- Client-side injection policy (how an agent uses returned memories is the
  client's business; we ship the tool description, not the client).

## Acceptance criteria

1. **Wisdom-teeth test (end-to-end):** seed episodes showing dental-surgery
   research/aftercare across two days; then a recall query about ordering food
   returns the extraction-recovery memory above the relevance floor. After the
   horizon passes (or a "feeling better" episode), the same query returns nothing.
2. **France test (end-to-end):** seed a past trip-to-France memory; a recall query
   about choosing a travel destination surfaces it; an unrelated cooking query does
   not.
3. **Skepticism:** a corpus of low-signal episodes (news reading, routine coding,
   doc lookups) produces **zero** promoted memories; the decision trail shows them
   considered and dropped.
4. **Evidence accumulation:** a fact seen in one ordinary episode stays staged; the
   same fact in a second episode promotes it; one high-signal episode (booking
   confirmation) promotes directly.
5. **Lifecycle:** a contradicting episode revises the old fact (archived, not
   deleted, decision logged); a user edit is never overwritten by later capture.
6. **Recall contract:** on the reference corpus (1k memories), local ranking adds
   < 50 ms and end-to-end meets p50 < 500 ms / p95 < 1 s with a cloud embedder
   (p50 < 150 ms local); exactly one embedding call and zero generative calls per
   recall; below-floor queries return empty; expired `state` memories never surface.
7. **Budget & metrics:** with the cap set to N, a heavy synthetic day promotes ≤ N
   with the strongest candidates winning, and metrics account for every considered
   candidate.
8. **Filter chain unchanged:** the existing filter test table passes untouched;
   episodes are built only from post-filter text.

## Non-functional requirements

- **Filter-before-everything** is inherited verbatim from v1 (constitution §4.4).
- **Recall latency** budgets as in AC 6; recall is engineered as a hot path.
- **Bounded spend:** worst-case daily inference calls ≈ episode count + promotion
  arbitration, never per-dwell; the budget caps it hard.
- **Explainability:** every memory traces to its supporting episodes; every
  drop/skip has a recorded reason. "Why does/doesn't Lore know X?" is always
  answerable from local data.
- **Seam discipline:** mem0 only via `IMemoryService`, models only via
  `IInferenceBackend`, Win32/UIA only via extractor interfaces (constitution §3.2).

## Risks

- **Skeptical capture misses real facts** (the France trip never promotes because
  evidence was thin). Mitigation: high-signal fast path (AC 4), tunable budget, and
  the staging area is visible in the library so near-misses are recoverable by a
  user tap.
- **Recall precision is the product.** If irrelevant memories surface, clients stop
  calling. Mitigation: relevance floor + kind/temporal weighting are unit-tested
  against a golden query/corpus set that includes both motivating tests; the floor
  errs toward empty.
- **mem0's own add/update heuristics fight Lore's lifecycle.** Mitigation: spike in
  `plan.md` — decide early whether mem0 runs in raw-store mode with Lore owning
  extraction, or Lore adapts to mem0's events; contract tests pin the choice.
- **Episode segmentation is heuristic.** Bad splits blur distillation. Mitigation:
  segmentation is pure, unit-testable logic on recorded traces; thresholds are
  config, not code.
