"""Minimal memoryd-shaped sidecar used only to prove PyInstaller packaging.

SPIKE CODE — throwaway, not part of the product (see tasks.md T001).

The packaging risk for the real ``memoryd`` is whether PyInstaller can freeze
mem0's *entire* transitive dependency tree (mem0 + qdrant-client + fastapi +
uvicorn + pydantic + their native bits) into one Windows ``.exe`` that still
imports and serves. So this proto imports the heavy stuff at module load — if
PyInstaller misses a hidden import, the frozen exe fails fast on startup rather
than pretending to work.

Endpoints:
  GET /health    -> {"status": "ok"}                         (liveness)
  GET /selftest  -> versions of the bundled heavy deps       (proves they froze)

It deliberately does NOT construct a mem0 ``Memory`` (that needs Ollama running);
the eval harness covers runtime behavior. This binary only answers "did the
dependency tree freeze into a working exe?".
"""

from __future__ import annotations

import sys

# Heavy imports at top level on purpose: force PyInstaller to bundle them and
# surface any missing hidden import as an immediate ImportError in the exe.
import fastapi
import mem0
import qdrant_client
import uvicorn
from fastapi import FastAPI

app = FastAPI(title="lore-memoryd-spike-proto", docs_url=None, redoc_url=None)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}


@app.get("/selftest")
def selftest() -> dict[str, str]:
    return {
        "python": sys.version.split()[0],
        "frozen": str(getattr(sys, "frozen", False)),
        "mem0": getattr(mem0, "__version__", "unknown"),
        "fastapi": fastapi.__version__,
        "qdrant_client": getattr(qdrant_client, "__version__", "present"),
    }


def main() -> None:
    # --selftest: import-and-print, then exit (CI/headless packaging check).
    if "--selftest" in sys.argv:
        import json

        print(json.dumps(selftest()))
        return
    uvicorn.run(app, host="127.0.0.1", port=7843, log_level="warning")


if __name__ == "__main__":
    main()
