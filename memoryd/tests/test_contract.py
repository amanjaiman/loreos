"""Contract tests against the PINNED mem0 (spec 002 T007).

These pin the mem0 behaviors memoryd depends on, so bumping mem0 is a deliberate,
test-guarded change. They run a real ``mem0.Memory`` + on-disk Qdrant but with
deterministic in-process stand-ins for the LLM and embedder, so they need no Ollama,
no network, and no torch — they run in CI.

What they guard:
- add / get_all / search / update / delete round-trip through mem0 + Qdrant.
- mem0's ``add()`` is *additive*: two semantically-equivalent facts both persist
  (this is exactly why memoryd reconciles — spike Finding 1).
- Acceptance criterion 3: a contradicting fact converges to ONE current memory via
  memoryd's reconciliation pass (`Reconciler` over mem0's real search + delete).
"""

from __future__ import annotations

import hashlib
from collections.abc import Iterator
from pathlib import Path

import pytest
from mem0 import Memory

from lore_memoryd.models import AddedMemory
from lore_memoryd.reconcile import Reconciler, Verdict

pytestmark = pytest.mark.contract

_DIM = 16
USER = "contract-user"


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
        # openai providers construct without any network call; we never invoke the
        # LLM (adds use infer=False; reconciliation uses an injected judge).
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


def _add(memory: Memory, text: str) -> str:
    result = memory.add(text, user_id=USER, infer=False)
    items = result.get("results", result)
    return str(items[0]["id"])


def _all_texts(memory: Memory) -> list[str]:
    result = memory.get_all(filters={"user_id": USER}, top_k=100)
    items = result.get("results", result)
    return [i["memory"] for i in items]


def test_add_get_search_update_delete_roundtrip(memory: Memory) -> None:
    mem_id = _add(memory, "The user is allergic to penicillin")

    assert _all_texts(memory) == ["The user is allergic to penicillin"]

    found = memory.search("penicillin allergy", filters={"user_id": USER}, top_k=5)
    hits = found.get("results", found)
    assert any("penicillin" in h["memory"] for h in hits)

    memory.update(mem_id, data="The user is allergic to penicillin and aspirin")
    assert "aspirin" in memory.get(mem_id)["memory"]

    memory.delete(mem_id)
    assert _all_texts(memory) == []


def test_native_add_is_additive(memory: Memory) -> None:
    # mem0 does not merge semantically-equivalent facts on its own — both persist.
    # This is the contract that justifies memoryd's reconciliation pass.
    _add(memory, "The user lives in Seattle")
    _add(memory, "The user lives in Seattle, Washington")
    assert len(_all_texts(memory)) == 2


def test_contradiction_converges_to_one_memory(memory: Memory) -> None:
    # Acceptance criterion 3, via the reconciliation pass over mem0's real search +
    # delete. The judge is injected (supersedes) so the test is deterministic.
    _add(memory, "The user lives in Seattle")
    austin_id = _add(memory, "The user just moved to Austin")

    def judge(_new: str, _existing: str) -> Verdict:
        return "supersedes"

    reconciler = Reconciler(memory, judge, threshold=0.0)
    survivors = reconciler.reconcile(
        [AddedMemory(id=austin_id, memory="The user just moved to Austin", event="ADD")], USER
    )

    texts = _all_texts(memory)
    assert len(texts) == 1
    assert "Austin" in texts[0]
    assert "Seattle" not in texts[0]
    assert survivors[0].event == "UPDATE"
