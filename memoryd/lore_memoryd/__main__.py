"""Run memoryd under uvicorn: ``python -m lore_memoryd``.

The agent's supervisor (spec 002 T005) launches this and gates on ``/health``.
Host/port come from the environment so the supervisor controls them; binds to
loopback only.
"""

from __future__ import annotations

import os

import uvicorn

from .app import app


def main() -> None:
    host = os.environ.get("LORE_MEMORYD_HOST", "127.0.0.1")
    port = int(os.environ.get("LORE_MEMORYD_PORT", "7843"))
    uvicorn.run(app, host=host, port=port, log_level="warning")


if __name__ == "__main__":
    main()
