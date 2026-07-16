"""Request/response models — the memoryd HTTP contract.

This is the wire contract the agent's C# `MemorydClient` (spec 002 T004) mirrors,
so it is deliberately mem0-agnostic: no mem0 vocabulary leaks across the boundary.
memoryd normalizes mem0's looser dict shapes into these typed models.
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field

# --------------------------------------------------------------------------- #
# Configuration (POST /config)
# --------------------------------------------------------------------------- #


class ProviderConfig(BaseModel):
    """The user's model provider (from spec 004), forwarded by the agent.

    `api_key` is resolved by the agent from the OS keystore before the call and is
    only ever transmitted over `127.0.0.1` — memoryd never persists or logs it.
    """

    type: str = Field(description="Provider kind: openai | anthropic | ollama | openai_compatible")
    model: str
    base_url: str | None = None
    api_key: str | None = None
    temperature: float = 0.0


class EmbedderConfig(BaseModel):
    """How to embed memories. `follow_provider` lets memoryd pick the provider's own
    embeddings; otherwise the agent sends an explicit embedder (spec 013). `api_key` is
    set only when the embedder targets a different keyed host than the provider — when
    omitted, the openai embedder reuses the provider's key. Resolved by the agent from
    the keystore and only ever transmitted over `127.0.0.1`; never persisted or logged."""

    type: str = "follow_provider"
    model: str | None = None
    base_url: str | None = None
    api_key: str | None = None
    dims: int | None = None


class ConfigRequest(BaseModel):
    """(Re)initialize mem0. The vector store and history db live under `data_dir`."""

    provider: ProviderConfig
    embedder: EmbedderConfig = EmbedderConfig()
    data_dir: str = Field(description="Lore data dir; Qdrant + history.db are created here")
    collection_name: str = "lore"
    user_id: str = "default"


class ConfigResponse(BaseModel):
    status: str = "ok"
    collection_name: str


# --------------------------------------------------------------------------- #
# Memory items + CRUD
# --------------------------------------------------------------------------- #


class MemoryItem(BaseModel):
    """A single stored memory, normalized from mem0's result dict."""

    id: str
    memory: str
    score: float | None = None
    metadata: dict[str, Any] | None = None
    created_at: str | None = None
    updated_at: str | None = None


class AddedMemory(BaseModel):
    """One outcome of an add. In raw-store mode (v2-001) an add stores exactly the
    caller's text, so this is a single-item list with event ADD; the event field
    survives for wire compatibility."""

    id: str
    memory: str
    event: str = Field(description="ADD | UPDATE | DELETE | NONE")


class AddRequest(BaseModel):
    text: str
    user_id: str = "default"
    metadata: dict[str, Any] | None = None


class AddResponse(BaseModel):
    results: list[AddedMemory]


class SearchRequest(BaseModel):
    query: str
    user_id: str = "default"
    limit: int = 10
    filters: dict[str, Any] | None = None


class SearchResponse(BaseModel):
    results: list[MemoryItem]


class MemoriesResponse(BaseModel):
    results: list[MemoryItem]


class UpdateRequest(BaseModel):
    """Patch a memory's text and/or metadata. At least one must be present.

    Metadata is MERGED into the existing metadata key-by-key (send a key to
    overwrite it); memoryd always rewrites the full merged dict because the
    pinned mem0 wipes metadata on text-only updates (v2-001 spike Q4)."""

    text: str | None = None
    metadata: dict[str, Any] | None = None


class StatusResponse(BaseModel):
    status: str = "ok"
