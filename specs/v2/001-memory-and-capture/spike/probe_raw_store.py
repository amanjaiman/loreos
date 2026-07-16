"""v2-001 spike — can mem0 2.0.5 run as a raw store for Lore-owned typed memories?

Questions (each maps to a section of spike-findings.md):
  Q1  infer=False stores the exact text with ZERO generative-LLM calls.
      (Proven by configuring a bogus LLM model: any LLM call fails loudly.)
  Q2  Arbitrary Lore metadata (kind/status/confidence/horizon/provenance)
      survives add -> get -> search.
  Q3  Which search/get_all filter syntaxes work on metadata keys against local
      Qdrant (equality, negation, in-lists, boolean AND/OR)?
  Q4  Can metadata be patched after add (lifecycle: staged->active, confidence
      bump, archive) — via Memory.update, or the vector-store seam, or not at all?
      Does a text update() preserve metadata?
  Q5  Filtered-search latency at ~400 memories with the local embedder.
  Q6  infer=True on the same config fails (control: proves Q1 made no LLM call).

Run:  spike-venv/Scripts/python.exe probe_raw_store.py --store <fresh-dir>
Output: human-readable log + JSON verdict block at the end.
"""

from __future__ import annotations

import argparse
import inspect
import json
import os
import statistics
import time
from pathlib import Path
from typing import Any

# Same kill-switch production memoryd applies in lore_memoryd/__init__.py —
# mem0's PostHog telemetry is on by default and must never phone home.
os.environ["MEM0_TELEMETRY"] = "False"

from mem0 import Memory  # noqa: E402 — must come after the telemetry kill-switch

OLLAMA_URL = "http://127.0.0.1:11434"
EMBED_MODEL = "nomic-embed-text"
EMBED_DIMS = 768
BOGUS_LLM = "lore-spike-bogus-model-never-pulled"  # any LLM call -> loud 404

USER = "default"

FINDINGS: dict[str, Any] = {}


def log(msg: str) -> None:
    print(msg, flush=True)


def build_memory(store_dir: Path) -> Memory:
    config = {
        # per 002 spike: never let mem0 default to the global ~/.mem0/history.db
        "history_db_path": str(store_dir / "history.db"),
        "llm": {
            "provider": "ollama",
            "config": {"model": BOGUS_LLM, "temperature": 0.0, "ollama_base_url": OLLAMA_URL},
        },
        "embedder": {
            "provider": "ollama",
            "config": {"model": EMBED_MODEL, "ollama_base_url": OLLAMA_URL},
        },
        "vector_store": {
            "provider": "qdrant",
            "config": {
                "path": str(store_dir / "qdrant"),
                "collection_name": "lore_v2_spike",
                "embedding_model_dims": EMBED_DIMS,
            },
        },
    }
    return Memory.from_config(config)


def results(raw: Any) -> list[dict[str, Any]]:
    items = raw.get("results", []) if isinstance(raw, dict) else raw
    return [i for i in items if isinstance(i, dict)] if isinstance(items, list) else []


WISDOM_META = {
    "kind": "state",
    "status": "active",
    "confidence": 0.8,
    "horizon": "2026-08-15",
    "episodes": ["ep-101", "ep-118"],
}
FRANCE_META = {
    "kind": "experience",
    "status": "active",
    "confidence": 0.9,
    "horizon": None,
    "episodes": ["ep-042"],
}


def q1_q2_raw_add_roundtrip(m: Memory) -> tuple[str, str]:
    log("\n=== Q1/Q2: infer=False raw add + metadata roundtrip ===")
    sig = inspect.signature(m.add)
    log(f"Memory.add signature: {sig}")
    FINDINGS["add_signature"] = str(sig)
    if "infer" not in sig.parameters:
        raise SystemExit("FATAL: mem0 2.0.5 Memory.add has no infer parameter")

    text1 = "I'm recovering from wisdom tooth extraction (mid-July 2026)."
    text2 = "I visited France in spring 2026."
    r1 = results(m.add(text1, user_id=USER, metadata=dict(WISDOM_META), infer=False))
    r2 = results(m.add(text2, user_id=USER, metadata=dict(FRANCE_META), infer=False))
    log(f"add#1 -> {r1}")
    log(f"add#2 -> {r2}")
    assert len(r1) == 1 and len(r2) == 1, "raw add should yield exactly one result each"
    id1, id2 = str(r1[0]["id"]), str(r2[0]["id"])

    stored_verbatim = r1[0].get("memory") == text1 and r2[0].get("memory") == text2
    FINDINGS["q1_raw_add_verbatim"] = stored_verbatim
    FINDINGS["q1_event"] = r1[0].get("event")

    got = m.get(id1)
    meta = (got or {}).get("metadata") or {}
    roundtrip_ok = (
        meta.get("kind") == "state"
        and meta.get("status") == "active"
        and abs(float(meta.get("confidence", 0)) - 0.8) < 1e-9
        and meta.get("horizon") == "2026-08-15"
        and meta.get("episodes") == ["ep-101", "ep-118"]
    )
    log(f"get({id1[:8]}...) metadata -> {meta}")
    FINDINGS["q2_metadata_roundtrip"] = roundtrip_ok
    return id1, id2


def try_filter(m: Memory, label: str, filters: dict[str, Any]) -> dict[str, Any]:
    try:
        hits = results(m.search("travel and trips", filters=filters, top_k=10))
        kinds = sorted({(h.get("metadata") or {}).get("kind") for h in hits})
        out = {"ok": True, "n": len(hits), "kinds": kinds}
    except Exception as exc:  # noqa: BLE001 — probe: the failure mode IS the data
        out = {"ok": False, "error": f"{type(exc).__name__}: {exc}"}
    log(f"  filter {label:<34} -> {out}")
    return out


def q3_filter_syntaxes(m: Memory) -> None:
    log("\n=== Q3: filter syntax probe (search) ===")
    probes = {
        "plain user_id only": {"user_id": USER},
        "equality kind=experience": {"user_id": USER, "kind": "experience"},
        "equality kind=state": {"user_id": USER, "kind": "state"},
        "equality no-match kind=project": {"user_id": USER, "kind": "project"},
        "op ne {'ne': 'archived'}": {"user_id": USER, "status": {"ne": "archived"}},
        "op $ne {'$ne': 'archived'}": {"user_id": USER, "status": {"$ne": "archived"}},
        "op in {'in': [...]}": {"user_id": USER, "kind": {"in": ["experience", "state"]}},
        "op $in {'$in': [...]}": {"user_id": USER, "kind": {"$in": ["experience", "state"]}},
        "bool AND list": {"AND": [{"user_id": USER}, {"kind": "experience"}]},
        "bool OR list": {
            "AND": [
                {"user_id": USER},
                {"OR": [{"kind": "experience"}, {"kind": "state"}]},
            ]
        },
    }
    FINDINGS["q3_filters"] = {label: try_filter(m, label, f) for label, f in probes.items()}

    log("--- get_all with kind filter ---")
    try:
        raw = m.get_all(filters={"user_id": USER, "kind": "experience"}, top_k=50)
        n = len(results(raw))
        FINDINGS["q3_get_all_kind_filter"] = {"ok": True, "n": n}
        log(f"  get_all(kind=experience) -> {n} items")
    except Exception as exc:  # noqa: BLE001
        FINDINGS["q3_get_all_kind_filter"] = {"ok": False, "error": str(exc)}
        log(f"  get_all(kind=experience) -> ERROR {exc}")


def q4_metadata_patch(m: Memory, mem_id: str) -> None:
    log("\n=== Q4: metadata update paths ===")
    sig = inspect.signature(m.update)
    log(f"Memory.update signature: {sig}")
    FINDINGS["update_signature"] = str(sig)

    # 4a. does Memory.update accept metadata?
    patched_via_update = False
    if "metadata" in sig.parameters:
        try:
            m.update(mem_id, data="I'm recovering from wisdom tooth extraction (mid-July 2026).",
                     metadata={**WISDOM_META, "status": "archived"})
            got = m.get(mem_id) or {}
            patched_via_update = ((got.get("metadata") or {}).get("status") == "archived")
        except Exception as exc:  # noqa: BLE001
            log(f"  update(metadata=...) raised {type(exc).__name__}: {exc}")
    FINDINGS["q4_update_accepts_metadata"] = patched_via_update
    log(f"  Memory.update patches metadata: {patched_via_update}")

    # 4b. does a plain text update PRESERVE existing metadata?
    before = (m.get(mem_id) or {}).get("metadata") or {}
    m.update(mem_id, data="I'm recovering from wisdom tooth extraction; healing well (late July 2026).")
    after_item = m.get(mem_id) or {}
    after = after_item.get("metadata") or {}
    preserved = all(after.get(k) == v for k, v in before.items() if k in ("kind", "horizon", "episodes"))
    FINDINGS["q4_text_update_preserves_metadata"] = preserved
    log(f"  text update preserves metadata: {preserved} (before={before} after={after})")

    # 4c. vector-store seam: patch payload directly (the memoryd-internal fallback).
    patched_via_store = False
    try:
        vs = m.vector_store
        vs_sig = inspect.signature(vs.update)
        log(f"  vector_store.update signature: {vs_sig}")
        FINDINGS["q4_vs_update_signature"] = str(vs_sig)
        rec = vs.get(mem_id)
        payload = dict(getattr(rec, "payload", None) or {})
        payload["status"] = "archived"
        payload["confidence"] = 0.95
        vs.update(mem_id, payload=payload)
        got = m.get(mem_id) or {}
        meta = got.get("metadata") or {}
        # mem0 may fold payload keys into metadata or top level; accept either
        flat = {**got, **meta}
        patched_via_store = flat.get("status") == "archived"
        log(f"  after vs.update -> metadata={meta}")
    except Exception as exc:  # noqa: BLE001
        log(f"  vector_store.update failed: {type(exc).__name__}: {exc}")
    FINDINGS["q4_vector_store_patch_works"] = patched_via_store

    # 4d. does the archived memory still come back from a filtered search that
    # excludes archived (the recall path's expiry filter)?
    working_ne = next(
        (lbl for lbl, r in FINDINGS["q3_filters"].items() if "ne" in lbl and r.get("ok")), None)
    if patched_via_store and working_ne:
        f = {"user_id": USER, "status": {"ne": "archived"}} if "'ne'" in working_ne else {
            "user_id": USER, "status": {"$ne": "archived"}}
        hits = results(m.search("wisdom tooth pain", filters=f, top_k=10))
        excluded = all(str(h.get("id")) != mem_id for h in hits)
        FINDINGS["q4_archived_excluded_by_ne_filter"] = excluded
        log(f"  archived memory excluded by ne-filter search: {excluded}")


def q5_latency(m: Memory, store_dir: Path) -> None:
    log("\n=== Q5: filtered-search latency at ~400 memories ===")
    kinds = ["identity", "preference", "state", "experience", "project"]
    t0 = time.perf_counter()
    for i in range(400):
        meta = {
            "kind": kinds[i % 5],
            "status": "archived" if i % 7 == 0 else "active",
            "confidence": 0.5 + (i % 5) * 0.1,
            "horizon": None,
            "episodes": [f"ep-{i}"],
        }
        m.add(f"Synthetic fact number {i}: the user enjoys hobby #{i} on weekends.",
              user_id=USER, metadata=meta, infer=False)
    add_s = time.perf_counter() - t0
    log(f"  400 raw adds in {add_s:.1f}s ({add_s / 400 * 1000:.0f} ms/add incl. embedding)")
    FINDINGS["q5_ms_per_raw_add"] = round(add_s / 400 * 1000)

    queries = ["what food should I order", "travel destination ideas",
               "what programming projects am I working on", "weekend hobbies",
               "how is my health"] * 8
    lat: list[float] = []
    for q in queries:
        t = time.perf_counter()
        m.search(q, filters={"user_id": USER, "kind": "state"}, top_k=5)
        lat.append((time.perf_counter() - t) * 1000)
    lat.sort()
    p50 = statistics.median(lat)
    p95 = lat[int(len(lat) * 0.95) - 1]
    FINDINGS["q5_filtered_search_ms"] = {"p50": round(p50), "p95": round(p95), "n": len(lat)}
    log(f"  filtered search over ~400: p50 {p50:.0f} ms, p95 {p95:.0f} ms (n={len(lat)})")


def q6_llm_control(m: Memory) -> None:
    log("\n=== Q6: control — infer=True with bogus LLM must fail ===")
    try:
        m.add("I switched from VS Code to Neovim last month.", user_id=USER, infer=True)
        FINDINGS["q6_infer_true_failed_loudly"] = False
        log("  UNEXPECTED: infer=True succeeded — bogus LLM was not called?!")
    except Exception as exc:  # noqa: BLE001
        FINDINGS["q6_infer_true_failed_loudly"] = True
        log(f"  infer=True raised {type(exc).__name__} (expected) -> Q1 made zero LLM calls")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--store", required=True, help="fresh dir for qdrant + history db")
    args = ap.parse_args()
    store = Path(args.store)
    store.mkdir(parents=True, exist_ok=True)

    m = build_memory(store)
    id1, _id2 = q1_q2_raw_add_roundtrip(m)
    q3_filter_syntaxes(m)
    q4_metadata_patch(m, id1)
    q5_latency(m, store)
    q6_llm_control(m)

    log("\n=== VERDICT JSON ===")
    log(json.dumps(FINDINGS, indent=2, default=str))
    (store / "findings.json").write_text(json.dumps(FINDINGS, indent=2, default=str))


if __name__ == "__main__":
    main()
