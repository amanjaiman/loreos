# v2-001 — Memory Model & Capture · Spike findings

> **Verdict: GO on raw-store mode.** mem0 2.0.5 (`infer=False`) works as a pure
> embedding + vector + CRUD layer with Lore-owned typed metadata and query-time
> filtered recall. Lore (the agent) owns extraction and lifecycle; `memoryd`'s
> LLM reconciliation pass (002's Option C) is **retired**. Harness:
> [`spike/probe_raw_store.py`](spike/probe_raw_store.py) +
> [`spike/probe_warm_latency.py`](spike/probe_warm_latency.py); the expiry
> check was an ad-hoc follow-up in the same session (same store and filters,
> not a committed probe); run against pinned `mem0ai==2.0.5`, Qdrant on-disk,
> Ollama `nomic-embed-text` (768-dim) on 2026-07-15.

## TL;DR

| Question | Result |
|---|---|
| Does `add(…, infer=False)` store Lore's fact verbatim with **zero LLM calls**? | **Yes.** Byte-identical storage, `event: ADD`; proven with a bogus LLM model that would 404 on any call. |
| Does Lore's typed metadata survive add → get → search? | **Yes** — strings, floats, nulls, lists (`kind`, `status`, `confidence`, `horizon`, `episodes`) round-trip intact. |
| Can recall filter on metadata at query time? | **Yes**, with mem0 2.0.5's syntax: plain top-level dict that must include `user_id`; operators `eq/ne/in/nin/gt/gte/lt/lte/contains/icontains`. **No** `$`-prefixed ops, **no** `AND`/`OR` trees. |
| Can expired memories be excluded at query time? | **Yes.** Numeric `expires_at` (epoch seconds; sentinel `4102444800` = year 2100 for non-expiring) + `{"expires_at": {"gt": now}}` — verified: expired `state` excluded, unexpired `state` and sentinel `identity` returned. |
| Can lifecycle patch metadata after add (staged→active, archive, confidence)? | **Yes** — `Memory.update(memory_id, data, metadata=…)` patches both. **Footgun:** a text-only `update()` **wipes metadata to `{}`** — every update must re-send the full merged metadata. (`vector_store.update(payload=…)` also works as a low-level fallback; not needed.) |
| Is filtered search fast enough for the every-turn recall path? | **Yes.** Warm store @ ~400 memories: **p50 36 ms, p95 40 ms, max 49 ms** (incl. Ollama query embedding) — well inside the spec's local budget. Raw add: ~50 ms incl. embedding. |
| Anything surprising? | Two things. (1) Immediately after a 400-add burst, a few searches spiked to ~2.2 s (Qdrant settling); steady-state re-measured clean — recall should tolerate/absorb post-bulk cold spikes (e.g. warm-up query after batch writes). (2) With `infer=True`, mem0 **swallows** LLM failures ("LLM extraction failed" log, no exception) — irrelevant to our path (we never use `infer=True`), but memoryd must never rely on mem0 to surface extraction errors. |
| Telemetry | mem0 ships PostHog telemetry **on by default**; production `lore_memoryd` already kill-switches it at import (`MEM0_TELEMETRY=False`) with a guard test. The spike probes set the same env var. Raw-store mode does not change the egress picture. |

## Architectural consequence (the decision this spike existed to make)

**mem0 runs as a raw store. Lore owns extraction and lifecycle.**

- The agent's distiller (v2-001) produces typed facts; `memoryd` stores them
  verbatim (`infer=False`) — no double extraction, no mem0 prompt in the path.
- Lifecycle arbitration (reinforce / revise / contradict) moves to the **agent**
  (C#, via `IInferenceBackend`), where the constitution says behavior lives
  (§3.1). `memoryd` becomes thinner: typed CRUD + filtered search.
- `lore_memoryd/reconcile.py` (002's Option C reconciler and its LLM judgment) is
  **deleted** — its job no longer exists when nothing additive-and-untyped enters
  the store. 002's criterion-3 contract test is superseded by v2-001's lifecycle
  tests at the agent seam.
- Staging lives in mem0 too (`status: "staged"`): candidates are embedded and
  searchable for candidate-matching, and recall's `status: "active"` filter keeps
  them invisible to clients until promoted by a metadata patch.

## Binding implementation rules extracted from the probes

1. `memoryd` **always** calls `add(…, infer=False)`; the `infer=True` path is
   unreachable in production code.
2. Every `update()` re-sends the complete merged metadata (fetch → merge → write);
   a contract test asserts a text-only patch preserves metadata through our API.
3. Search filters are validated against the supported operator set above and are
   always a plain top-level dict including `user_id`.
4. `expires_at` is epoch seconds, non-null on every memory (sentinel for
   non-expiring); recall filters `status eq "active"` + `expires_at gt now`.
5. `history_db_path` stays pinned under the Lore data dir (002 lesson; the
   history db is still written even in raw mode).
6. The `MEM0_TELEMETRY=False` guard and its test remain load-bearing.

_Raw outputs: `spike/` harness prints + `findings.json` in the run store. Spike
code is throwaway; this report is the deliverable._
