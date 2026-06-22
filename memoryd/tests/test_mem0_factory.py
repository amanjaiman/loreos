"""Unit tests for the mem0 config translation (no mem0/Ollama needed)."""

from __future__ import annotations

import pytest

from lore_memoryd.mem0_factory import EmbedderConfigError, build_mem0_config
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


def test_data_dir_env_override_wins_over_request(monkeypatch: pytest.MonkeyPatch) -> None:
    # Self-hosted multi-device (spec 011 T005): the host pins the shared store so a
    # connecting device's data_dir can't repoint it.
    monkeypatch.setenv("LORE_MEMORYD_DATA_DIR", "/srv/lore")
    cfg = build_mem0_config(_cfg())  # request still says /data/lore
    assert cfg["history_db_path"].replace("\\", "/").endswith("/srv/lore/history.db")
    assert cfg["vector_store"]["config"]["path"].replace("\\", "/").endswith("/srv/lore/qdrant")


def test_follow_provider_without_embeddings_raises_actionable_error() -> None:
    # A chat-only provider with no first-party embeddings and no explicit embedder must
    # NOT silently assume a local Ollama (spec 013) — it raises an actionable error naming
    # the embedder config to set.
    with pytest.raises(EmbedderConfigError, match="no first-party embeddings"):
        build_mem0_config(_cfg(type="anthropic", model="claude-haiku-4-5", api_key="sk-test"))


def test_self_hosted_ollama_embeds_at_its_own_base_url() -> None:
    # A self-hosted endpoint (keyless openai_compatible -> type 'ollama') serves its own
    # embeddings at *its* URL, never an implicit localhost (spec 013).
    cfg = build_mem0_config(_cfg(type="ollama", model="qwen2.5:7b", base_url="http://nas:11434"))
    assert cfg["embedder"]["provider"] == "ollama"
    assert cfg["embedder"]["config"]["model"] == "nomic-embed-text"
    assert cfg["embedder"]["config"]["ollama_base_url"] == "http://nas:11434"
    assert cfg["vector_store"]["config"]["embedding_model_dims"] == 768


def test_follow_provider_uses_openai_first_party_embeddings() -> None:
    # When the provider IS OpenAI-shaped, follow_provider should embed with the
    # provider's own model rather than the local fallback (T003 policy).
    cfg = ConfigRequest.model_validate(
        {
            "provider": {
                "type": "openai",
                "model": "gpt-4o",
                "api_key": "sk-test",
                "base_url": "http://proxy",
            },
            "embedder": {"type": "follow_provider"},
            "data_dir": "/data/lore",
            "collection_name": "lore",
        }
    )
    result = build_mem0_config(cfg)
    assert result["embedder"]["provider"] == "openai"
    assert result["embedder"]["config"]["model"] == "text-embedding-3-small"
    assert result["embedder"]["config"]["api_key"] == "sk-test"
    assert result["embedder"]["config"]["openai_base_url"] == "http://proxy"
    assert result["vector_store"]["config"]["embedding_model_dims"] == 1536


def test_follow_provider_openai_compatible_without_embedder_raises() -> None:
    # Keyed openai_compatible endpoints (Groq/OpenRouter) cannot be assumed to expose
    # embeddings, and their embed model id is unguessable — require an explicit embedder
    # rather than silently falling back to a local Ollama (spec 013).
    cfg = ConfigRequest.model_validate(
        {
            "provider": {
                "type": "openai_compatible",
                "model": "llama-3-70b",
                "api_key": "sk-test",
                "base_url": "http://proxy.example.com/v1",
            },
            "embedder": {"type": "follow_provider"},
            "data_dir": "/data/lore",
            "collection_name": "lore",
        }
    )
    with pytest.raises(EmbedderConfigError, match="no first-party embeddings"):
        build_mem0_config(cfg)


def test_explicit_embedder_uses_its_own_api_key_over_the_provider_key() -> None:
    # An explicit embedder on a different keyed host (spec 013): its own key wins, so a
    # provider without embeddings (e.g. Anthropic) can borrow another endpoint's embeddings.
    cfg = ConfigRequest.model_validate(
        {
            "provider": {
                "type": "anthropic",
                "model": "claude-haiku-4-5",
                "api_key": "anthropic-key",
            },
            "embedder": {
                "type": "openai",
                "model": "text-embedding-3-small",
                "api_key": "sk-embedder",
                "base_url": "https://api.openai.com/v1",
            },
            "data_dir": "/data/lore",
            "collection_name": "lore",
        }
    )
    result = build_mem0_config(cfg)
    assert result["llm"]["provider"] == "anthropic"
    assert result["embedder"]["provider"] == "openai"
    assert result["embedder"]["config"]["api_key"] == "sk-embedder"  # not the anthropic key
    assert result["embedder"]["config"]["openai_base_url"] == "https://api.openai.com/v1"
    assert result["vector_store"]["config"]["embedding_model_dims"] == 1536


def test_gemini_embedding_model_resolves_its_dimension() -> None:
    # Gemini's first-party embeddings via its OpenAI-compatible endpoint (spec 013):
    # gemini-embedding-001 is the model that endpoint serves, at 3072 dims (verified live),
    # reusing the provider key.
    cfg = ConfigRequest.model_validate(
        {
            "provider": {
                "type": "openai_compatible",
                "model": "gemini-2.5-flash",
                "api_key": "g-key",
                "base_url": "https://generativelanguage.googleapis.com/v1beta/openai/",
            },
            "embedder": {
                "type": "openai",
                "model": "gemini-embedding-001",
                "base_url": "https://generativelanguage.googleapis.com/v1beta/openai/",
            },
            "data_dir": "/data/lore",
            "collection_name": "lore",
        }
    )
    result = build_mem0_config(cfg)
    assert result["embedder"]["config"]["model"] == "gemini-embedding-001"
    assert result["embedder"]["config"]["api_key"] == "g-key"  # reused from the provider
    assert result["vector_store"]["config"]["embedding_model_dims"] == 3072


def test_unsupported_provider_raises() -> None:
    with pytest.raises(ValueError, match="Unsupported provider"):
        build_mem0_config(_cfg(type="mystery-llm"))


def test_anthropic_provider_without_api_key_raises() -> None:
    with pytest.raises(ValueError, match="Anthropic provider requires an API key"):
        build_mem0_config(_cfg(type="anthropic", model="claude-haiku-4-5"))


def test_openai_provider_without_api_key_raises() -> None:
    with pytest.raises(ValueError, match="openai provider requires an API key"):
        build_mem0_config(_cfg(type="openai", model="gpt-4o"))


def test_openai_embedder_without_api_key_raises() -> None:
    cfg = ConfigRequest.model_validate(
        {
            "provider": {"type": "ollama", "model": "qwen2.5:7b-instruct"},
            "embedder": {"type": "openai", "model": "text-embedding-3-small"},
            "data_dir": "/data/lore",
            "collection_name": "lore",
        }
    )
    with pytest.raises(ValueError, match="OpenAI embedder requires an API key"):
        build_mem0_config(cfg)


def test_openai_embedder_with_api_key_builds_config() -> None:
    cfg = ConfigRequest.model_validate(
        {
            "provider": {"type": "openai", "model": "gpt-4o", "api_key": "sk-test"},
            "embedder": {"type": "openai", "model": "text-embedding-3-small"},
            "data_dir": "/data/lore",
            "collection_name": "lore",
        }
    )
    result = build_mem0_config(cfg)
    assert result["embedder"]["provider"] == "openai"
    assert result["embedder"]["config"]["api_key"] == "sk-test"
    assert result["vector_store"]["config"]["embedding_model_dims"] == 1536
