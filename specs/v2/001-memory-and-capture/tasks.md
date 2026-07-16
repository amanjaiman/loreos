# v2-001 — Memory Model & Capture · Tasks

> Atomic, dependency-ordered, one PR each (~1–4 h), every task ends at a green
> `no-mistakes` gate (constitution §7). See [`plan.md`](plan.md) for designs.

- [x] **T001 — Spike: mem0 raw-store mode.** Verdict GO; see
  [`spike-findings.md`](spike-findings.md). Deliverable is the report + probes
  under `spike/` (throwaway, committed for provenance).

- [ ] **T002 — memoryd: raw-store mode.** `infer=False` on every add; delete
  `reconcile.py` + its unit/contract tests (supersession note in the PR);
  `PATCH /memories/{id}` takes `text?`/`metadata?` and always writes
  fetch-merged full metadata; search/get_all validate the supported filter
  operators (`eq/ne/in/nin/gt/gte/lt/lte/contains/icontains`, plain dict,
  `user_id` required). New contract tests: verbatim add, metadata roundtrip,
  metadata-preserving text patch, `ne`/`in`/`gt` filters, expiry exclusion.
  Backward-compatible with the current agent.

- [ ] **T003 — Agent: memory schema + seam.** `MemoryMetadata` (typed, plan
  table) with serialization to/from the metadata dict; `IMemoryService` gains
  filtered search, metadata patch, and typed add; `MemorydClient` implements;
  sentinel/expiry helpers; unit tests incl. round-trip and non-null-keys
  invariant.

- [ ] **T004 — Agent: episode segmentation + stores.** `EpisodeBuilder` (pure
  logic: continuity/close rules from plan, config thresholds),
  `Episode`/`EpisodeStore` + `decisions` table in the local SQLite,
  representative-sample selection; wire into the capture loop behind
  `capture.pipeline: "v2"` (flag off → old path untouched). Unit tests on
  recorded observation traces; flush-on-shutdown test.

- [ ] **T005 — Agent: skeptical distiller.** `prompts/distill.txt` (few-shot per
  plan), `Distiller` + `DistillParser` (malformed output → `distill_failed`
  decision, never a crash), ≤ 3 facts/episode cap. Tests with mocked
  `IInferenceBackend`: happy path, empty-is-normal, garbage output, cap.

- [ ] **T006 — Agent: lifecycle engine + budget.** Similarity-band routing
  (reinforce/promote/arbitrate/stage), `prompts/arbitrate.txt`, high-signal
  fast path, pinned/user-edited protection (`needs_confirmation` decision),
  `PromotionBudget` (SQLite counter, defer-not-drop), full decision-trail
  writes. Exhaustive routing-table unit tests with mocked seams (AC 3/4/5/7).

- [ ] **T007 — Agent: recall service + endpoint + golden corpus.**
  `RecallService`/`RecallScorer` (blend, floor, kind/temporal weights from
  config), `POST /recall`, post-batch warm-up query. Golden corpus JSON +
  deterministic fake embedder; contract tests encode spec AC 1/2/6 (including
  the empty-below-floor cases); calibrate bands/floor and record chosen values
  in plan.md (living spec).

- [ ] **T008 — Surfaces: MCP `lore_recall` + CLI.** New MCP tool with the
  every-turn description (reviewed copy, spec deliverable); reword existing
  tool descriptions to the memory-layer story; `lore recall "<query>"` CLI
  command (`--json`); `lore remember` routes through the lifecycle as
  high-signal. Contract tests for tool schemas + one stdio round-trip.

- [ ] **T009 — App-facing API.** `GET /memories` (kind/status/query filters,
  pagination), `PATCH /memories/{id}` (user authority semantics),
  `POST /memories/{id}/confirm`, `POST /staging/{id}/promote|dismiss`,
  `GET /episodes` / `GET /decisions`, `/system/status` economy counters;
  OpenAPI updated (constitution §5). Endpoint tests against a stubbed
  `IMemoryService`/stores.

- [ ] **T010 — Migration + E2E acceptance + flag default.** v1-row archive sweep
  (idempotent, logged, tested against a seeded v1-shaped store); E2E harness
  driving the wisdom-teeth and France journeys against live Ollama (documented
  as a local/pre-release check, not CI); flip `capture.pipeline` default to
  `"v2"`; update `docs/` (memory model page, providers note re minimum capable
  distillation model). Old-path deletion happens in v2-002, not here.

**Parallelism:** T002 ∥ T003 after T001; T004/T005 ∥ T007-golden-corpus after
T003; T006 needs T004+T005; T008/T009 after T007/T006; T010 last.
