"""Translate a Lore provider config into a mem0 ``Memory``.

This is the only place mem0's configuration vocabulary lives. T002 establishes the
translation and the on-disk layout; T003 completes the `follow_provider` embedder
policy (prefer the provider's first-party embeddings, fall back to a local model)
and adds the reconciliation pass (`reconcile.py`).
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from mem0 import Memory

from .models import ConfigRequest, EmbedderConfig, ProviderConfig

# Validated local embedder default (spike T001): Ollama nomic-embed-text, 768-dim.
# Used when the provider has no first-party embeddings, so a user with only a
# chat-model key never incurs surprise embedding spend.
_LOCAL_EMBED_MODEL = "nomic-embed-text"
_LOCAL_EMBED_DIMS = 768
_DEFAULT_OLLAMA_URL = "http://localhost:11434"

# Known embedding dimensions so Qdrant's collection is sized correctly.
_EMBED_DIMS = {
    "nomic-embed-text": 768,
    "mxbai-embed-large": 1024,
    "text-embedding-3-small": 1536,
    "text-embedding-3-large": 3072,
}


def _llm_config(p: ProviderConfig) -> dict[str, Any]:
    """Map a provider into mem0's `{provider, config}` LLM block."""
    common: dict[str, Any] = {"model": p.model, "temperature": p.temperature}
    if p.type == "ollama":
        return {
            "provider": "ollama",
            "config": {**common, "ollama_base_url": p.base_url or _DEFAULT_OLLAMA_URL},
        }
    if p.type == "anthropic":
        if not p.api_key:
            raise ValueError("Anthropic provider requires an API key; set provider.api_key")
        return {"provider": "anthropic", "config": {**common, "api_key": p.api_key}}
    if p.type in ("openai", "openai_compatible"):
        if not p.api_key:
            raise ValueError(f"{p.type} provider requires an API key; set provider.api_key")
        cfg = {**common, "api_key": p.api_key}
        if p.base_url:
            cfg["openai_base_url"] = p.base_url
        return {"provider": "openai", "config": cfg}
    raise ValueError(f"Unsupported provider type: {p.type!r}")


def _embedder_config(emb: EmbedderConfig, provider: ProviderConfig) -> tuple[dict[str, Any], int]:
    """Map the embedder into mem0's `{provider, config}` block + its dimension.

    T002 honors an explicit embedder and otherwise uses the local default. T003
    extends `follow_provider` to use the provider's own embeddings when it has
    them (e.g. OpenAI) before falling back here.
    """
    if emb.type == "ollama" and emb.model:
        dims = emb.dims or _EMBED_DIMS.get(emb.model, _LOCAL_EMBED_DIMS)
        return {
            "provider": "ollama",
            "config": {"model": emb.model, "ollama_base_url": emb.base_url or _DEFAULT_OLLAMA_URL},
        }, dims
    if emb.type == "openai" and emb.model:
        api_key = provider.api_key if provider.type in ("openai", "openai_compatible") else None
        if not api_key:
            raise ValueError(
                "OpenAI embedder requires an API key; set provider.api_key or use a local embedder"
            )
        dims = emb.dims or _EMBED_DIMS.get(emb.model, 1536)
        cfg: dict[str, Any] = {"model": emb.model, "api_key": api_key}
        if emb.base_url or provider.base_url:
            cfg["openai_base_url"] = emb.base_url or provider.base_url
        return {"provider": "openai", "config": cfg}, dims

    # follow_provider (or unspecified): local default.
    return {
        "provider": "ollama",
        "config": {"model": _LOCAL_EMBED_MODEL, "ollama_base_url": _DEFAULT_OLLAMA_URL},
    }, _LOCAL_EMBED_DIMS


def build_mem0_config(cfg: ConfigRequest) -> dict[str, Any]:
    """Produce mem0's nested config dict from a Lore ConfigRequest."""
    data_dir = Path(cfg.data_dir)
    embedder_block, dims = _embedder_config(cfg.embedder, cfg.provider)
    return {
        # Never the global ~/.mem0 default: that leaks per-user "Last k Messages"
        # across instances and poisons extraction (spike T001 Finding 2).
        "history_db_path": str(data_dir / "history.db"),
        "llm": _llm_config(cfg.provider),
        "embedder": embedder_block,
        "vector_store": {
            "provider": "qdrant",
            "config": {
                "path": str(data_dir / "qdrant"),
                "collection_name": cfg.collection_name,
                "embedding_model_dims": dims,
            },
        },
    }


def build_memory(cfg: ConfigRequest) -> Memory:
    """Build a mem0 ``Memory`` for the given config, creating the data dir."""
    Path(cfg.data_dir).mkdir(parents=True, exist_ok=True)
    return Memory.from_config(build_mem0_config(cfg))
