"""Follow-up: is the p95 spike steady-state or a post-bulk-add cold artifact?

Reopens the existing spike store (no new adds) and times 60 filtered searches.
"""

from __future__ import annotations

import statistics
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from probe_raw_store import USER, build_memory  # noqa: E402

store = Path(sys.argv[1])
m = build_memory(store)

queries = [
    "what food should I order", "travel destination ideas",
    "what programming projects am I working on", "weekend hobbies",
    "how is my health", "what editor do I use",
] * 10

lat: list[float] = []
for q in queries:
    t = time.perf_counter()
    m.search(q, filters={"user_id": USER, "kind": {"ne": "project"}}, top_k=5)
    lat.append((time.perf_counter() - t) * 1000)

lat.sort()
n = len(lat)
print(f"warm store, n={n}: p50 {statistics.median(lat):.0f} ms, "
      f"p90 {lat[int(n * 0.9) - 1]:.0f} ms, p95 {lat[int(n * 0.95) - 1]:.0f} ms, "
      f"max {lat[-1]:.0f} ms")
print("slowest five:", [f"{x:.0f}" for x in lat[-5:]])
