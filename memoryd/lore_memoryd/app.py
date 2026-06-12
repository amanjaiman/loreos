"""FastAPI application factory for memoryd.

Spec 001 shipped `/health` only. Spec 002 (T002) grows it into the full memory
service: `/config` plus `/memories` CRUD and search, backed by mem0. The mem0
engine is created lazily by `POST /config` (the agent calls it once at startup),
or eagerly in the lifespan from `LORE_MEMORYD_CONFIG` when that env var points at a
config JSON. Binds to `127.0.0.1` only.
"""

from __future__ import annotations

import json
import os
from collections.abc import AsyncIterator, Callable
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI

from .backend import Mem0Backend, MemoryBackend
from .mem0_factory import build_memory
from .models import ConfigRequest
from .reconcile import Reconciler, make_llm_judge
from .routes import router

BackendFactory = Callable[[ConfigRequest], MemoryBackend]


def _default_factory(cfg: ConfigRequest) -> MemoryBackend:
    memory = build_memory(cfg)
    reconciler = Reconciler(memory, make_llm_judge(memory))
    return Mem0Backend(memory, reconciler)


def create_app(backend_factory: BackendFactory | None = None) -> FastAPI:
    """Build the memoryd application.

    `backend_factory` is injectable so tests can supply a fake engine and exercise
    the routes without a live mem0/Ollama stack.
    """
    factory = backend_factory or _default_factory

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        # Optional eager bootstrap from a config file (production/uvicorn path).
        config_path = os.environ.get("LORE_MEMORYD_CONFIG")
        if config_path and Path(config_path).is_file():
            cfg = ConfigRequest.model_validate(
                json.loads(Path(config_path).read_text(encoding="utf-8"))
            )
            app.state.backend = factory(cfg)
        yield

    app = FastAPI(title="lore-memoryd", docs_url=None, redoc_url=None, lifespan=lifespan)
    # Set on the app directly (not only in lifespan) so the factory + initial
    # state are present even when the lifespan hasn't run (e.g. TestClient used
    # without its context manager).
    app.state.backend_factory = factory
    app.state.backend = None

    @app.get("/health")
    def health() -> dict[str, str]:
        """Liveness probe used by the supervising agent (spec 002 T005)."""
        return {"status": "ok"}

    app.include_router(router)
    return app


app = create_app()
