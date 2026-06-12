"""Unit tests for the mem0 config translation (no mem0/Ollama needed)."""

from __future__ import annotations

import pytest

from lore_memoryd.mem0_factory import build_mem0_config
from lore_memoryd.models import ConfigRequest


def _cfg(**provider: object) -> ConfigRequest:
    base: dict[str, object] = {"type": "ollama", "model": "qwen2.5:7b-instruct"}
    base.update(provider)
    return ConfigRequest.model_validate(
        {"provider": base, "data_dir": "/data/lore", "collection_name": "lore"}
    )


def test_ollama_provider_maps_to_mem0_block() -> None:
    cfg = build_mem0_config(_cfg(base_url="http://host:11434"))
    assert cfg["llm"]["provider"] == "ollama"
    assert cfg["llm"]["config"]["model"] == "qwen2.5:7b-instruct"
    assert cfg["llm"]["config"]["ollama_base_url"] == "http://host:11434"


def test_vector_store_and_history_live_under_data_dir() -> None:
    cfg = build_mem0_config(_cfg())
    # Never the global ~/.mem0 default (spike Finding 2).
    assert cfg["history_db_path"].replace("\\", "/").endswith("/data/lore/history.db")
    assert cfg["vector_store"]["provider"] == "qdrant"
    assert cfg["vector_store"]["config"]["path"].replace("\\", "/").endswith("/data/lore/qdrant")
    assert cfg["vector_store"]["config"]["collection_name"] == "lore"


def test_follow_provider_uses_local_embedder_default() -> None:
    # A chat-only provider with no embeddings must still get a working embedder
    # locally — no surprise embedding spend (acceptance criterion 4).
    cfg = build_mem0_config(_cfg(type="anthropic", model="claude-haiku-4-5", api_key="sk-test"))
    assert cfg["llm"]["provider"] == "anthropic"
    assert cfg["embedder"]["provider"] == "ollama"
    assert cfg["embedder"]["config"]["model"] == "nomic-embed-text"
    assert cfg["vector_store"]["config"]["embedding_model_dims"] == 768


def test_unsupported_provider_raises() -> None:
    with pytest.raises(ValueError, match="Unsupported provider"):
        build_mem0_config(_cfg(type="mystery-llm"))
