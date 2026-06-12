"""Run mem0 + on-disk Qdrant + local Ollama over the synthetic observations and
score extraction / dedup / update / search / latency.

SPIKE CODE — throwaway, not part of the product (see tasks.md T001).

Stack under test (all local, no cloud):
  * LLM       : Ollama ``gemma3n:e2b``        (mem0's extraction/update reasoning)
  * Embedder  : Ollama ``nomic-embed-text``   (768-dim, the local default candidate)
  * Vector DB : Qdrant on-disk under a temp dir

mem0's ``add()`` returns an ``event`` per memory (ADD / UPDATE / DELETE / NONE),
which is exactly the dedup/conflict signal acceptance criterion 3 cares about, so
we record events rather than guessing from counts alone.

Usage:
    python run_eval.py [--limit N] [--out results.json]
"""

from __future__ import annotations

import argparse
import json
import shutil
import time
from pathlib import Path
from tempfile import mkdtemp

from mem0 import Memory

USER_ID = "spike-user"
OLLAMA_URL = "http://localhost:11434"
LLM_MODEL = "qwen2.5:7b-instruct"
EMBED_MODEL = "nomic-embed-text"
EMBED_DIMS = 768

# Relevance ground truth: a query is "hit" if any expected keyword (case-
# insensitive) appears in the top-k returned memory texts. Reflects post-update
# truth where the corpus contradicts an earlier fact.
QUERY_KEYWORDS: dict[str, list[str]] = {
    "What editor does the user use?": ["neovim"],
    "Where does the user live?": ["austin"],
    "Does the user have any allergies?": ["penicillin"],
    "What are the user's dietary restrictions?": ["pescatarian", "fish"],
    "Who is the user's manager?": ["priya"],
    "What flights has the user booked?": ["tokyo", "united"],
    "What instrument is the user learning?": ["cello"],
    "What car does the user drive?": ["subaru", "outback"],
    "When is the memory service spec due?": ["june 20", "june"],
    "What book is the user reading?": ["three-body", "liu cixin"],
}


def build_memory(store_dir: str) -> Memory:
    # NOTE (spike correctness): mem0 keeps a per-user message history in a SQLite
    # db that defaults to a *global* ~/.mem0/history.db, and feeds the "Last k
    # Messages" of that history into the extraction prompt. Sharing it across runs
    # poisons extraction with prior runs' messages. Pin it inside the per-run
    # store dir so every run starts with empty working memory.
    config = {
        "history_db_path": str(Path(store_dir) / "history.db"),
        "llm": {
            "provider": "ollama",
            "config": {
                "model": LLM_MODEL,
                "temperature": 0.0,
                "ollama_base_url": OLLAMA_URL,
            },
        },
        "embedder": {
            "provider": "ollama",
            "config": {"model": EMBED_MODEL, "ollama_base_url": OLLAMA_URL},
        },
        "vector_store": {
            "provider": "qdrant",
            "config": {
                "path": store_dir,
                "collection_name": "lore_spike",
                "embedding_model_dims": EMBED_DIMS,
            },
        },
    }
    return Memory.from_config(config)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--limit", type=int, default=0, help="cap observations (0 = all)")
    parser.add_argument("--out", default="results.json")
    args = parser.parse_args()

    here = Path(__file__).parent
    corpus = json.loads((here / "observations.json").read_text(encoding="utf-8"))
    observations = corpus["observations"]
    if args.limit:
        # keep all anchors (non-filler) + fill up to limit so ground truth survives
        anchors = [o for o in observations if not o["tags"] or o["tags"] != ["filler"]]
        filler = [o for o in observations if o["tags"] == ["filler"]]
        observations = (anchors + filler)[: args.limit]

    store_dir = mkdtemp(prefix="lore_spike_qdrant_")
    print(f"Qdrant on-disk store: {store_dir}")
    mem = build_memory(store_dir)

    # ---- ADD phase: record per-observation events + latency --------------
    add_records = []
    event_counts = {"ADD": 0, "UPDATE": 0, "DELETE": 0, "NONE": 0}
    t0 = time.perf_counter()
    for i, o in enumerate(observations):
        start = time.perf_counter()
        try:
            res = mem.add(o["text"], user_id=USER_ID, metadata={"obs_id": o["id"]})
        except Exception as exc:  # noqa: BLE001 - spike: capture & continue
            add_records.append({"obs_id": o["id"], "error": str(exc)})
            print(f"[{i}] ERROR on {o['id']}: {exc}")
            continue
        elapsed = time.perf_counter() - start
        results = res.get("results", res) if isinstance(res, dict) else res
        events = [r.get("event", "?") for r in results] if isinstance(results, list) else []
        for e in events:
            event_counts[e] = event_counts.get(e, 0) + 1
        add_records.append(
            {"obs_id": o["id"], "latency_s": round(elapsed, 3), "events": events,
             "memories": [r.get("memory") for r in results] if isinstance(results, list) else []}
        )
        if i % 10 == 0:
            print(f"[{i}/{len(observations)}] {o['id']} events={events} {elapsed:.2f}s")
    add_total = time.perf_counter() - t0

    # ---- final state -----------------------------------------------------
    all_mem = mem.get_all(filters={"user_id": USER_ID}, top_k=1000)
    all_results = all_mem.get("results", all_mem) if isinstance(all_mem, dict) else all_mem
    final_count = len(all_results) if isinstance(all_results, list) else 0

    # ---- SEARCH phase ----------------------------------------------------
    search_records = []
    for q in corpus["search_queries"]:
        query = q["query"]
        keywords = QUERY_KEYWORDS.get(query, [])
        start = time.perf_counter()
        sres = mem.search(query, filters={"user_id": USER_ID}, top_k=5)
        elapsed = time.perf_counter() - start
        hits = sres.get("results", sres) if isinstance(sres, dict) else sres
        texts = [h.get("memory", "") for h in hits] if isinstance(hits, list) else []
        joined = " || ".join(texts).lower()
        hit = any(kw.lower() in joined for kw in keywords) if keywords else None
        search_records.append(
            {"query": query, "latency_s": round(elapsed, 3), "hit": hit,
             "expect_keywords": keywords, "top": texts[:3]}
        )

    add_latencies = [r["latency_s"] for r in add_records if "latency_s" in r]
    search_latencies = [r["latency_s"] for r in search_records]
    hits = [r for r in search_records if r["hit"]]
    scored = [r for r in search_records if r["hit"] is not None]

    summary = {
        "stack": {"llm": LLM_MODEL, "embedder": EMBED_MODEL, "embed_dims": EMBED_DIMS,
                  "vector_store": "qdrant-on-disk"},
        "observations_in": len(observations),
        "memories_out": final_count,
        "event_counts": event_counts,
        "add_total_s": round(add_total, 1),
        "add_latency_s": _stats(add_latencies),
        "search_latency_s": _stats(search_latencies),
        "search_hit_rate": f"{len(hits)}/{len(scored)}" if scored else "n/a",
    }

    out = {
        "summary": summary,
        "add_records": add_records,
        "search_records": search_records,
        "final_memories": [r.get("memory") for r in all_results] if isinstance(all_results, list) else [],
    }
    out_path = here / args.out
    out_path.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print("\n=== SUMMARY ===")
    print(json.dumps(summary, indent=2))
    print(f"\nFull results -> {out_path}")

    shutil.rmtree(store_dir, ignore_errors=True)


def _stats(xs: list[float]) -> dict:
    if not xs:
        return {"n": 0}
    s = sorted(xs)
    n = len(s)
    return {
        "n": n,
        "min": s[0],
        "p50": s[n // 2],
        "p95": s[min(n - 1, int(n * 0.95))],
        "max": s[-1],
        "mean": round(sum(s) / n, 3),
    }


if __name__ == "__main__":
    main()
