# 002 — Memory Service · Spike findings (T001)

> **Verdict: GO on mem0. GO on PyInstaller packaging.** One spec-affecting decision
> (acceptance criterion 3 / conflict-resolution) was surfaced and **ratified:
> Option C — reconcile in `memoryd`** (see Finding 1). Time-boxed evaluation per
> [`plan.md`](plan.md) "The spike (do this first)". Harness + raw results live in
> [`spike/`](spike/) (throwaway; not shipped).

## TL;DR

| Question the spike had to answer | Result |
|---|---|
| Does mem0 extract usable memories from screen-derived text? | **Yes — with a capable LLM.** Clean, self-contained facts; suppresses obvious restatements. A 2B model (`gemma3n`) is unusable (confabulates). |
| Does the **local embedder default** give relevant search? | **Yes, strongly.** `nomic-embed-text` (768-dim) → **10/10** ground-truth queries hit in top-5. |
| Is search latency acceptable (NFR < ~1.5 s)? | **Yes, by a wide margin.** p50 **49 ms**, p95 **50 ms** on a warm on-disk store. |
| Does `add` then `search` return the stored memory? | **Yes.** |
| Does a contradicting fact **UPDATE** rather than duplicate (criterion 3)? | **No — not in mem0 2.0.x's default pipeline.** This is the one finding that forces a spec change. See below. |
| Can the sidecar be a single Windows `.exe` (PyInstaller)? | **Yes.** Frozen exe imports the full mem0 tree and serves `/health`. 358 MB with torch, **175 MB** with torch excluded. |

## Stack under test

| Layer | Choice | Notes |
|---|---|---|
| Memory engine | **mem0 2.0.5** (latest on PyPI at spike time) | pinned candidate |
| Extraction LLM | Ollama `qwen2.5:7b-instruct` (capable) and `gemma3n:e2b` (weak, contrast) | stands in for the user's BYO capture model |
| Embedder | Ollama `nomic-embed-text`, 768-dim | the **local default** candidate |
| Vector store | Qdrant on-disk (local mode) | no external service |
| Packaging | PyInstaller 6.19, `--onefile` | Windows `win-x64` |

Method: ~200 synthetic distilled observations (no v1 `lore.db` was available, so the
corpus is **representative, not real** — see Caveats) with embedded ground truth —
dedup groups, contradiction pairs, and search queries with expected keywords. The
harness records mem0's per-add `event` (ADD/UPDATE/DELETE/NONE), add latency, and a
keyword-scored search hit-rate. Reproduce via [`spike/README.md`](spike/README.md).

---

## Finding 1 (CRITICAL) — mem0 2.0.x `add()` is additive + exact-hash dedup; no LLM conflict-resolution

The spec's acceptance criterion 3 assumes mem0's **classic** behavior: `add()`
extracts a fact, compares it to existing memories, and emits **UPDATE/DELETE** when
the new fact supersedes an old one. **mem0 2.0.5 no longer does this in the default
`add()` path.** Reading the installed source (`mem0/memory/main.py`,
`_add_to_vector_store`, the "V3 PHASED BATCH PIPELINE"):

- Phase 2 is a **single extraction call** using an `ADDITIVE_EXTRACTION_PROMPT`
  ("Your sole operation is ADD"). Retrieved existing memories are passed in only so
  the model can *avoid re-stating* them.
- Phase 5 dedup is **exact MD5-hash** of the memory text only
  (`main.py:825`). Byte-identical re-adds are skipped; anything else is a new ADD.
- Every history event in this path is hardcoded `"event": "ADD"` (`main.py:875`).
- The LLM-driven `_update_memory` (UPDATE event, `main.py:1711`) **exists** but is
  reached only via the explicit `update()` API, not via `add()`.

**Observed consequences** (clean run, qwen2.5:7b, 20 anchor observations → 25
memories):

| Ground-truth case | Expected (criterion 3) | Actual |
|---|---|---|
| "lives in Seattle" + "lives in Seattle, Washington" | one memory | **both stored** (different hash) |
| "vegetarian" → "pescatarian, eats fish now" | update/replace | **both stored, coexist** |
| "main editor VS Code" → "switched to Neovim" | update/replace | **both stored, coexist** |
| byte-identical re-add | skip | **skipped** ✓ (hash dedup works) |
| obvious restatement same call ("editing in VS Code again today") | not re-added | **not re-added** ✓ (extractor suppressed it) |

So mem0 *does* avoid trivial duplication (exact-hash + extractor judgment within a
call), but it does **not** reconcile semantically-equivalent or contradictory facts
across calls. A consumer asking "where does the user live?" gets **both** Austin and
Seattle back.

**At scale the store bloats.** The full 200-observation run produced **1077 ADD
events (~5.4× the observation count), zero UPDATE/DELETE** — un-reconciled near-dups
and restatements accumulate roughly linearly. (Search stayed fast anyway: p50 56 ms
over 1000+ memories — see Finding 3.) Left unaddressed, an additive store grows
several times faster than the user's actual distinct facts.

**Why this is a GO, not a NO-GO:** search still surfaces the correct/newest fact in
top-k (see Finding 3 — Austin and Neovim both rank in top-5), the seam
(`IMemoryService`) fully isolates the behavior, and the issue is a *version-behavior*
mismatch, not a mem0 capability gap. But criterion 3 as written is **not satisfiable
on mem0 2.0.x out of the box** and must change.

### Decision (product-behavior — escalated and RATIFIED: Option C)

The conflict-resolution strategy was escalated as a human call and **ratified as
Option C — reconcile in `memoryd`.** Keep mem0 2.0.x on the maintained line; add an
explicit reconciliation step in the sidecar so criterion 3 is literally satisfied.

Options that were weighed:

- ~~Option A — accept additive semantics, resolve at read time~~ (newest-wins
  ranking in `IMemoryService`; cheapest, but stale facts linger and the store bloats
  ~5×).
- ~~Option B — pin a classic pre-2.0 mem0 whose `add()` reconciles~~ (keeps
  criterion 3 free, but runs an older/less-maintained mem0).
- **Option C — reconcile in `memoryd` (CHOSEN).** After each `add`, query mem0 for
  near-neighbors of the new memory; when a neighbor is a semantic duplicate or a
  contradiction, call mem0's `update()`/`delete()` explicitly so the store converges
  to one current fact. Most faithful to spec intent; the reconciliation logic lives
  in our seam (Python `memoryd`), so it is fully testable and isolated.

**Implications for production tasks:**

- **T002** pins `mem0ai==2.0.5`; the `add` route returns the reconciled result.
- **T003**'s factory wires a reconciliation pass (near-neighbor search threshold +
  an LLM "supersedes / duplicate / unrelated" judgment using the configured LLM,
  then `update`/`delete`). This is net-new `memoryd` logic, scoped here so T003 plans
  for it rather than discovering it.
- **T007**'s criterion-3 contract test asserts that adding a contradicting fact
  leaves **one** memory reflecting the new truth (UPDATE/DELETE happened), exercised
  against the pinned mem0 via the reconciliation path.

---

## Finding 2 — Extraction quality is strongly model-bound

| Model | 1 sentence ("editor is VS Code") → | Quality |
|---|---|---|
| `gemma3n:e2b` (2B) | 4–6 memories, **confabulated** unrelated facts, typos ("ccurrently") | **unusable** |
| `qwen2.5:7b-instruct` | 1 clean, self-contained memory | **good** |

Implication: the BYO **capture/extraction model** (from spec 004) materially
determines memory quality. `memoryd`/docs should state a **minimum capable model**
for extraction (≈7B-class local, or any frontier hosted model) and the spike's weak
result should be cited so users don't point a tiny model at capture and get garbage.

> Harness bug found & fixed mid-spike (documented so results are trustworthy): mem0
> feeds a per-`user_id` "Last k Messages" history (default **global**
> `~/.mem0/history.db`) into the extraction prompt. Sharing it across runs poisoned
> extraction with prior runs' messages. Fix: pin `history_db_path` per run
> (`spike/run_eval.py:build_memory`). Production `memoryd` must likewise set
> `history_db_path` under the Lore data dir, never the global default.

---

## Finding 3 — Local embedder default + search: excellent

- **Relevance:** **10/10** ground-truth queries returned the expected fact in top-5
  (qwen extraction + `nomic-embed-text`), including post-conflict truths (Neovim,
  Austin) ranking in top-5 *despite* the stale fact still being stored.
- **Latency (warm, on-disk Qdrant):** search p50 **49 ms**, p95 **50 ms** — far
  under the < ~1.5 s NFR. Add latency (extraction, LLM-bound) p50 **3.0 s**, mean
  **4.6 s** — irrelevant to interactive read latency; capture (003) is async.

This validates the spec's **local embedder default**: a user with only a chat-model
key gets relevant search with zero embedding spend.

---

## Finding 4 — PyInstaller packaging: works, with a size lever

- `--onefile` exe **imports the full mem0 tree and serves `/health`** when frozen
  (`{"frozen":"true", "mem0":"2.0.5", ...}`). Packaging is viable.
- **Size: 358 MB** with `--collect-all mem0` (pulls in **torch**, a transitive dep
  not needed when using the Ollama/local embedder). Excluding torch + transformers +
  scipy/sklearn → **175 MB**. Production `memoryd` should exclude the ML stack it
  doesn't use at runtime.
- `--onefile` unpacks to a temp dir on launch (~few seconds cold). The supervisor's
  `/health` gate (T005) must allow for this cold-start; consider `--onedir` for
  faster startup if the size/footprint tradeoff is acceptable.

> **Clean-VM caveat:** per the session's agreed scope, the packaging proof ran on
> the **dev machine**, not a pristine Windows VM. The `--selftest` exit-code path
> exists precisely so a real clean-VM check is a one-liner
> (`lore-memoryd-proto.exe --selftest`); running it on a VM with no Python/Ollama
> remains a pre-launch to-do (tracked for the installer spec).

---

## Caveats (what would strengthen this verdict)

1. **Synthetic corpus.** No v1 `lore.db` was available; observations are
   hand-authored to be representative of screen-derived distillation, not real
   captures. Real data may extract messier. The seam de-risks a later swap.
2. **Clean-VM packaging** not yet executed (see Finding 4).
3. **Single embedder/LLM pair** evaluated locally. Hosted providers (the common
   case) will extract better than a local 7B and should be spot-checked in T003/T007.

---

## Decisions carried into production tasks

1. **GO on mem0 2.0.x** behind `IMemoryService`; pin **`mem0ai==2.0.5`** +
   `qdrant-client` in `memoryd/pyproject.toml` (T002).
2. **Conflict-resolution: Option C — reconcile in `memoryd`** (ratified). Net-new
   reconciliation pass after `add` (near-neighbor search → LLM supersede/duplicate
   judgment → `update`/`delete`). T003 builds it; T007's criterion-3 contract test
   asserts a contradicting fact converges to one current memory.
3. **`history_db_path` is set under the Lore data dir** in `mem0_factory.py`, never
   the global `~/.mem0` default (T003).
4. **Local embedder default = Ollama `nomic-embed-text` (768-dim)** validated; this
   is the `follow_provider` fallback when the provider has no first-party embeddings
   (T003).
5. **Document a minimum capable extraction model**; weak models degrade memory
   quality badly (Finding 2).
6. **Packaging excludes the unused ML stack** (torch/transformers) to keep the
   sidecar ~175 MB (installer spec); `--selftest` is the clean-VM gate.

_Spike harness and raw result JSONs: [`spike/`](spike/). Spike code is not merged
into the product (`agent/`, `memoryd/`); this report is the deliverable._
