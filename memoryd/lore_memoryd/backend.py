"""The typed boundary over mem0.

`MemoryBackend` is the narrow surface memoryd's routes depend on. `Mem0Backend`
implements it over a mem0 ``Memory``, translating method calls and normalizing
mem0's loosely-typed dict results into our `MemoryItem` / `AddedMemory` models —
so the untyped mem0 surface stops here and routes/tests speak only typed models.
T003 hooks the post-add reconciliation pass into `Mem0Backend.add`.
"""

from __future__ import annotations

from typing import Any, Protocol

from mem0 import Memory

from .models import AddedMemory, MemoryItem


class MemoryBackend(Protocol):
    """What the routes need from a memory engine. Mocked in tests."""

    def add(
        self, text: str, user_id: str, metadata: dict[str, Any] | None
    ) -> list[AddedMemory]: ...

    def search(
        self, query: str, user_id: str, limit: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]: ...

    def get_all(self, user_id: str, limit: int) -> list[MemoryItem]: ...

    def get(self, memory_id: str) -> MemoryItem | None: ...

    def update(self, memory_id: str, text: str) -> MemoryItem | None: ...

    def delete(self, memory_id: str) -> bool: ...


def _results(raw: Any) -> list[dict[str, Any]]:
    """mem0 returns either a `{"results": [...]}` dict or a bare list."""
    items = raw.get("results", []) if isinstance(raw, dict) else raw
    return [i for i in items if isinstance(i, dict)] if isinstance(items, list) else []


def _to_item(d: dict[str, Any]) -> MemoryItem:
    return MemoryItem(
        id=str(d.get("id", "")),
        memory=str(d.get("memory", "")),
        score=d.get("score"),
        metadata=d.get("metadata"),
        created_at=d.get("created_at"),
        updated_at=d.get("updated_at"),
    )


class Mem0Backend:
    """`MemoryBackend` over a mem0 ``Memory``."""

    def __init__(self, memory: Memory) -> None:
        self._mem = memory

    def add(self, text: str, user_id: str, metadata: dict[str, Any] | None) -> list[AddedMemory]:
        raw = self._mem.add(text, user_id=user_id, metadata=metadata)
        return [
            AddedMemory(
                id=str(d.get("id", "")),
                memory=str(d.get("memory", "")),
                event=str(d.get("event", "ADD")),
            )
            for d in _results(raw)
        ]

    def search(
        self, query: str, user_id: str, limit: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]:
        merged: dict[str, Any] = {"user_id": user_id, **(filters or {})}
        raw = self._mem.search(query, filters=merged, top_k=limit)
        return [_to_item(d) for d in _results(raw)]

    def get_all(self, user_id: str, limit: int) -> list[MemoryItem]:
        raw = self._mem.get_all(filters={"user_id": user_id}, top_k=limit)
        return [_to_item(d) for d in _results(raw)]

    def get(self, memory_id: str) -> MemoryItem | None:
        raw = self._mem.get(memory_id)
        return _to_item(raw) if isinstance(raw, dict) and raw.get("id") else None

    def update(self, memory_id: str, text: str) -> MemoryItem | None:
        self._mem.update(memory_id, data=text)
        return self.get(memory_id)

    def delete(self, memory_id: str) -> bool:
        self._mem.delete(memory_id)
        return True
