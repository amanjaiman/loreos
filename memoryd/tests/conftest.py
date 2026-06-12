"""Shared test fixtures: an in-memory fake backend so route tests run without a
live mem0/Ollama stack. Real mem0 behavior is covered by the contract tests (T007)."""

from __future__ import annotations

import uuid
from datetime import UTC, datetime
from typing import Any

import pytest
from fastapi.testclient import TestClient

from lore_memoryd.app import create_app
from lore_memoryd.backend import MemoryBackend
from lore_memoryd.models import AddedMemory, ConfigRequest, MemoryItem


class FakeBackend:
    """A minimal, faithful-enough `MemoryBackend` for route tests."""

    def __init__(self) -> None:
        self._store: dict[str, dict[str, Any]] = {}
        self.closed = False

    def close(self) -> None:
        self.closed = True

    def add(self, text: str, user_id: str, metadata: dict[str, Any] | None) -> list[AddedMemory]:
        mem_id = str(uuid.uuid4())
        now = datetime.now(UTC).isoformat()
        self._store[mem_id] = {
            "id": mem_id,
            "memory": text,
            "user_id": user_id,
            "metadata": metadata,
            "created_at": now,
            "updated_at": now,
        }
        return [AddedMemory(id=mem_id, memory=text, event="ADD")]

    def search(
        self, query: str, user_id: str, limit: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]:
        terms = {t for t in query.lower().split() if len(t) > 2}
        hits = [
            self._item(d, score=1.0)
            for d in self._store.values()
            if d["user_id"] == user_id and any(t in d["memory"].lower() for t in terms)
        ]
        return hits[:limit]

    def get_all(self, user_id: str, limit: int) -> list[MemoryItem]:
        return [self._item(d) for d in self._store.values() if d["user_id"] == user_id][:limit]

    def get(self, memory_id: str) -> MemoryItem | None:
        d = self._store.get(memory_id)
        return self._item(d) if d else None

    def update(self, memory_id: str, text: str) -> MemoryItem | None:
        d = self._store.get(memory_id)
        if d is None:
            return None
        d["memory"] = text
        d["updated_at"] = datetime.now(UTC).isoformat()
        return self._item(d)

    def delete(self, memory_id: str) -> bool:
        return self._store.pop(memory_id, None) is not None

    @staticmethod
    def _item(d: dict[str, Any], score: float | None = None) -> MemoryItem:
        return MemoryItem(
            id=d["id"],
            memory=d["memory"],
            score=score,
            metadata=d.get("metadata"),
            created_at=d.get("created_at"),
            updated_at=d.get("updated_at"),
        )


@pytest.fixture
def fake_backend() -> FakeBackend:
    return FakeBackend()


@pytest.fixture
def client(fake_backend: FakeBackend) -> TestClient:
    """A TestClient whose /config installs the shared fake backend."""

    def factory(_cfg: ConfigRequest) -> MemoryBackend:
        return fake_backend

    return TestClient(create_app(backend_factory=factory))


@pytest.fixture
def sample_config() -> dict[str, Any]:
    return {
        "provider": {"type": "ollama", "model": "qwen2.5:7b-instruct"},
        "embedder": {"type": "follow_provider"},
        "data_dir": "/tmp/lore-test",
        "collection_name": "lore_test",
    }
