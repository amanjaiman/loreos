"""Contract tests against the PINNED mem0 (spec 002 T007, reshaped by v2-001 T002).

These pin the mem0 behaviors memoryd depends on, so bumping mem0 is a deliberate,
test-guarded change. They run a real ``mem0.Memory`` + on-disk Qdrant but with a
deterministic in-process embedder, so they need no Ollama, no network, and no
torch — they run in CI.

What they guard (v2-001 raw-store mode):
- ``add(…, infer=False)`` stores the caller's text VERBATIM (no extraction) and
  Lore's typed metadata round-trips intact.
- mem0's storage layer is additive: semantically-equivalent facts both persist.
  (This is why the agent's lifecycle engine — not the store — reconciles.)
- ``Mem0Backend.update`` preserves metadata on a text-only patch: the pinned
  mem0 wipes metadata to ``{}`` on a bare ``update()`` (spike Q4) and the
  backend's fetch-merge-resend is what protects against it.
- Query-time filters: equality, ``ne``, ``in``, numeric ``gt`` (the recall
  path's expiry exclusion), and rejection of unsupported shapes.
"""

from __future__ import annotations

import hashlib
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import pytest
from mem0 import Memory

from lore_memoryd.backend import Mem0Backend

pytestmark = pytest.mark.contract

_DIM = 16
USER = "contract-user"

# v2-001 sentinel for non-expiring memories (2100-01-01T00:00:00Z).
_FAR_FUTURE = 4102444800
_NOW = 1_800_000_000  # fixed "now" so tests are deterministic


class _FakeEmbedder:
    """Deterministic bag-of-words embedder: texts sharing words embed close."""

    def _vec(self, text: str) -> list[float]:
        vec = [0.0] * _DIM
        for word in text.lower().split():
            bucket = int(hashlib.md5(word.encode()).hexdigest(), 16) % _DIM
            vec[bucket] += 1.0
        norm = sum(x * x for x in vec) ** 0.5 or 1.0
        return [x / norm for x in vec]

    def embed(self, text: str, memory_action: str | None = None) -> list[float]:
        return self._vec(text)

    def embed_batch(self, texts: list[str], memory_action: str | None = None) -> list[list[float]]:
        return [self._vec(t) for t in texts]


@pytest.fixture
def memory(tmp_path: Path) -> Iterator[Memory]:
    config = {
        "history_db_path": str(tmp_path / "history.db"),
        # openai providers construct without any network call; raw-store adds
        # (infer=False) never invoke the LLM at all.
        "llm": {"provider": "openai", "config": {"model": "gpt-4o-mini", "api_key": "sk-fake"}},
        "embedder": {
            "provider": "openai",
            "config": {"model": "text-embedding-3-small", "api_key": "sk-fake"},
        },
        "vector_store": {
            "provider": "qdrant",
            "config": {
                "path": str(tmp_path / "qdrant"),
                "collection_name": "contract",
                "embedding_model_dims": _DIM,
            },
        },
    }
    mem = Memory.from_config(config)
    mem.embedding_model = _FakeEmbedder()  # deterministic, offline
    yield mem


@pytest.fixture
def backend(memory: Memory) -> Mem0Backend:
    return Mem0Backend(memory)


def _add(backend: Mem0Backend, text: str, metadata: dict[str, Any] | None = None) -> str:
    results = backend.add(text, USER, metadata)
    assert len(results) == 1
    return results[0].id


def _all_texts(backend: Mem0Backend) -> list[str]:
    return [m.memory for m in backend.get_all(USER, limit=100)]


def test_raw_add_is_verbatim_and_metadata_roundtrips(backend: Mem0Backend) -> None:
    meta = {
        "kind": "state",
        "status": "active",
        "confidence": 0.8,
        "expires_at": _NOW + 45 * 86400,
        "episodes": ["ep-1", "ep-2"],
    }
    text = "I'm recovering from wisdom tooth extraction (mid-July 2026)."
    mem_id = _add(backend, text, meta)

    got = backend.get(mem_id)
    assert got is not None
    assert got.memory == text  # verbatim: no extraction rewrote the fact
    assert got.metadata == meta  # floats, ints, lists all intact


def test_storage_is_additive_lifecycle_reconciles_elsewhere(backend: Mem0Backend) -> None:
    # mem0 does not merge semantically-equivalent facts in raw mode — both
    # persist. The agent's lifecycle engine (v2-001 T006) owns reconciliation;
    # this pin documents why the store cannot be trusted to do it.
    _add(backend, "The user lives in Seattle")
    _add(backend, "The user lives in Seattle, Washington")
    assert len(_all_texts(backend)) == 2


def test_text_only_patch_preserves_metadata(backend: Mem0Backend) -> None:
    meta = {"kind": "preference", "status": "active", "confidence": 0.7}
    mem_id = _add(backend, "The user prefers tea over coffee.", meta)

    updated = backend.update(mem_id, "The user prefers oolong tea.", None)
    assert updated is not None
    assert updated.memory == "The user prefers oolong tea."
    # The pinned mem0 wipes metadata on a bare update(); the backend's
    # fetch-merge-resend must preserve it.
    assert updated.metadata == meta


def test_metadata_only_patch_merges_keys(backend: Mem0Backend) -> None:
    mem_id = _add(backend, "The user is job hunting.", {"kind": "state", "status": "staged"})

    updated = backend.update(mem_id, None, {"status": "active", "confidence": 0.9})
    assert updated is not None
    assert updated.memory == "The user is job hunting."  # text untouched
    assert updated.metadata == {"kind": "state", "status": "active", "confidence": 0.9}


def test_equality_ne_and_in_filters(backend: Mem0Backend) -> None:
    _add(backend, "I visited France in spring 2026.", {"kind": "experience", "status": "active"})
    _add(backend, "I am recovering from surgery.", {"kind": "state", "status": "active"})
    _add(backend, "I used to use VS Code.", {"kind": "preference", "status": "archived"})

    hits = backend.search("visited places trips", USER, 10, {"kind": "experience"})
    assert [h.memory for h in hits] == ["I visited France in spring 2026."]

    active = backend.search("anything about me", USER, 10, {"status": {"ne": "archived"}})
    assert all((h.metadata or {}).get("status") == "active" for h in active)

    subset = backend.get_all(USER, 10, 0, {"kind": {"in": ["experience", "state"]}})
    assert {(m.metadata or {}).get("kind") for m in subset} == {"experience", "state"}


def test_expiry_exclusion_via_numeric_gt(backend: Mem0Backend) -> None:
    # The recall path's contract: expired state never surfaces; sentinel-dated
    # kinds always survive the expires_at gt-now filter.
    _add(
        backend,
        "I am training for a marathon.",
        {"kind": "state", "status": "active", "expires_at": _NOW + 90 * 86400},
    )
    _add(
        backend,
        "I was on crutches after a sprained ankle.",
        {"kind": "state", "status": "active", "expires_at": _NOW - 10 * 86400},
    )
    _add(
        backend,
        "I live in Boston.",
        {"kind": "identity", "status": "active", "expires_at": _FAR_FUTURE},
    )

    # The fake embedder is whitespace-split bag-of-words, so the query repeats
    # each memory's tokens verbatim (punctuation included) — this test is about
    # the expiry FILTER, not semantic ranking.
    hits = backend.search("marathon. crutches Boston.", USER, 10, {"expires_at": {"gt": _NOW}})
    texts = [h.memory for h in hits]
    assert not any("crutches" in t for t in texts)  # expired excluded
    assert any("marathon" in t for t in texts)  # unexpired state present
    assert any("Boston" in t for t in texts)  # sentinel identity present


def test_unsupported_filter_shapes_raise_before_reaching_mem0(backend: Mem0Backend) -> None:
    with pytest.raises(ValueError, match="operator"):
        backend.search("q", USER, 5, {"status": {"$ne": "archived"}})
    with pytest.raises(ValueError, match="boolean"):
        backend.search("q", USER, 5, {"AND": [{"kind": "state"}]})


def test_delete_roundtrip(backend: Mem0Backend) -> None:
    mem_id = _add(backend, "Ephemeral fact.")
    assert backend.delete(mem_id) is True
    assert backend.get(mem_id) is None
    assert backend.delete(mem_id) is False
