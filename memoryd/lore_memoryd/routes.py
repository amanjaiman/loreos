"""memoryd routes: /config + /memories CRUD + search. All bound to 127.0.0.1.

Handlers depend on a `MemoryBackend` held in `app.state.backend`, (re)created by
`POST /config` via `app.state.backend_factory`. Until configured, memory routes
return 503 — the agent calls `/config` once at startup before first use.
"""

from __future__ import annotations

from fastapi import APIRouter, HTTPException, Request

from .backend import MemoryBackend
from .models import (
    AddRequest,
    AddResponse,
    ConfigRequest,
    ConfigResponse,
    MemoriesResponse,
    MemoryItem,
    SearchRequest,
    SearchResponse,
    StatusResponse,
    UpdateRequest,
)

router = APIRouter()


def _require_backend(request: Request) -> MemoryBackend:
    backend: MemoryBackend | None = getattr(request.app.state, "backend", None)
    if backend is None:
        raise HTTPException(status_code=503, detail="memoryd is not configured; POST /config first")
    return backend


@router.post("/config", response_model=ConfigResponse)
def configure(request: Request, body: ConfigRequest) -> ConfigResponse:
    """(Re)initialize mem0 from a provider config. Idempotent: replaces any
    existing engine. Secrets in the body are localhost-only and never persisted."""
    try:
        request.app.state.backend = request.app.state.backend_factory(body)
    except Exception as exc:  # surface actionable config errors (constitution §5)
        raise HTTPException(
            status_code=400, detail=f"failed to initialize memory engine: {exc}"
        ) from exc
    return ConfigResponse(collection_name=body.collection_name)


@router.post("/memories", response_model=AddResponse)
def add_memory(request: Request, body: AddRequest) -> AddResponse:
    backend = _require_backend(request)
    return AddResponse(results=backend.add(body.text, body.user_id, body.metadata))


@router.post("/memories/search", response_model=SearchResponse)
def search_memories(request: Request, body: SearchRequest) -> SearchResponse:
    backend = _require_backend(request)
    return SearchResponse(
        results=backend.search(body.query, body.user_id, body.limit, body.filters)
    )


@router.get("/memories", response_model=MemoriesResponse)
def list_memories(request: Request, user_id: str = "default", limit: int = 100) -> MemoriesResponse:
    backend = _require_backend(request)
    return MemoriesResponse(results=backend.get_all(user_id, limit))


@router.get("/memories/{memory_id}", response_model=MemoryItem)
def get_memory(request: Request, memory_id: str) -> MemoryItem:
    backend = _require_backend(request)
    item = backend.get(memory_id)
    if item is None:
        raise HTTPException(status_code=404, detail=f"no memory with id {memory_id!r}")
    return item


@router.patch("/memories/{memory_id}", response_model=MemoryItem)
def update_memory(request: Request, memory_id: str, body: UpdateRequest) -> MemoryItem:
    backend = _require_backend(request)
    item = backend.update(memory_id, body.text)
    if item is None:
        raise HTTPException(status_code=404, detail=f"no memory with id {memory_id!r}")
    return item


@router.delete("/memories/{memory_id}", response_model=StatusResponse)
def delete_memory(request: Request, memory_id: str) -> StatusResponse:
    backend = _require_backend(request)
    if not backend.delete(memory_id):
        raise HTTPException(status_code=404, detail=f"no memory with id {memory_id!r}")
    return StatusResponse()
