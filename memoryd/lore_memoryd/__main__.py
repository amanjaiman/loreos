"""Run memoryd under uvicorn: ``python -m lore_memoryd``.

The agent's supervisor (spec 002 T005) launches this and gates on ``/health``.
Host/port come from the environment so the supervisor controls them; binds to
loopback only.

``--selftest`` is the clean-VM packaging gate (spec 011 T001): the frozen exe
imports its entire dependency tree, builds the app, prints the bundled versions,
and exits 0 — so a missing PyInstaller hidden import surfaces as a hard failure on
a machine with no Python/Ollama rather than at first real use.
"""

from __future__ import annotations

import json
import os
import sys

import uvicorn

from . import __version__
from .app import app


def selftest() -> dict[str, str]:
    """Import the full dependency tree and build the app, returning bundled versions.

    Importing ``app`` pulls in the backend → mem0_factory → mem0 chain, so a
    successful return proves PyInstaller froze every transitive import. It does not
    construct a mem0 ``Memory`` (that needs a live model), only the FastAPI app.
    """
    import fastapi
    import mem0
    import qdrant_client

    return {
        "lore_memoryd": __version__,
        "app": app.title,
        "python": sys.version.split()[0],
        "frozen": str(getattr(sys, "frozen", False)),
        "mem0": getattr(mem0, "__version__", "unknown"),
        "fastapi": fastapi.__version__,
        "qdrant_client": getattr(qdrant_client, "__version__", "present"),
    }


def main(argv: list[str] | None = None) -> None:
    args = sys.argv[1:] if argv is None else argv
    if "--selftest" in args:
        print(json.dumps(selftest()))
        return

    host = os.environ.get("LORE_MEMORYD_HOST", "127.0.0.1")
    port = int(os.environ.get("LORE_MEMORYD_PORT", "7843"))
    uvicorn.run(app, host=host, port=port, log_level="warning")


if __name__ == "__main__":
    main()
