"""The typed boundary over mem0.

`MemoryBackend` is the narrow surface memoryd's routes depend on. `Mem0Backend`
implements it over a mem0 ``Memory``, translating method calls and normalizing
mem0's loosely-typed dict results into our `MemoryItem` / `AddedMemory` models —
so the untyped mem0 surface stops here and routes/tests speak only typed models.
T003 hooks the post-add reconciliation pass into `Mem0Backend.add`.
"""

from __future__ import annotations

import contextlib
import logging
from typing import Any, Protocol

from mem0 import Memory

from .models import AddedMemory, MemoryItem
from .reconcile import Reconciler

_log = logging.getLogger(__name__)


class MemoryBackend(Protocol):
    """What the routes need from a memory engine. Mocked in tests."""

    def add(
        self, text: str, user_id: str, metadata: dict[str, Any] | None
    ) -> list[AddedMemory]: ...

    def search(
        self, query: str, user_id: str, limit: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]: ...

    def get_all(self, user_id: str, limit: int, offset: int) -> list[MemoryItem]: ...

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

    def __init__(self, memory: Memory, reconciler: Reconciler | None = None) -> None:
        self._mem = memory
        self._reconciler = reconciler

    def add(self, text: str, user_id: str, metadata: dict[str, Any] | None) -> list[AddedMemory]:
        raw = self._mem.add(text, user_id=user_id, metadata=metadata)
        added = [
            AddedMemory(
                id=str(d.get("id", "")),
                memory=str(d.get("memory", "")),
                event=str(d.get("event", "ADD")),
            )
            for d in _results(raw)
        ]
        # Option C: mem0's add is additive-only, so converge duplicates/contradictions
        # against existing memories before returning (spike T001 Finding 1).
        if self._reconciler is not None:
            try:
                return self._reconciler.reconcile(added, user_id)
            except Exception as exc:
                _log.warning(
                    "reconciliation failed (%s); returning un-reconciled memories",
                    type(exc).__name__,
                )
        return added

    def search(
        self, query: str, user_id: str, limit: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]:
        merged: dict[str, Any] = {**(filters or {}), "user_id": user_id}
        raw = self._mem.search(query, filters=merged, top_k=limit)
        return [_to_item(d) for d in _results(raw)]

    def get_all(self, user_id: str, limit: int, offset: int = 0) -> list[MemoryItem]:
        # mem0's get_all has no offset, so fetch through the requested window and
        # slice. Pagination lets the agent enumerate a whole store (MemorydClient
        # loops pages); a single huge top_k could otherwise hit an engine cap.
        # Trade-off: each page call fetches top_k = offset + limit items, so a full
        # enumeration over N items costs O(N²) in vector-store round-trips. Acceptable
        # at personal-assistant scale; a native cursor would eliminate this.
        raw = self._mem.get_all(filters={"user_id": user_id}, top_k=offset + limit)
        window = _results(raw)[offset : offset + limit]
        return [_to_item(d) for d in window]

    def get(self, memory_id: str) -> MemoryItem | None:
        raw = self._mem.get(memory_id)
        return _to_item(raw) if isinstance(raw, dict) and raw.get("id") else None

    def update(self, memory_id: str, text: str) -> MemoryItem | None:
        if self.get(memory_id) is None:
            return None
        self._mem.update(memory_id, data=text)
        return self.get(memory_id)

    def delete(self, memory_id: str) -> bool:
        if self.get(memory_id) is None:
            return False
        self._mem.delete(memory_id)
        return True

    def close(self) -> None:
        """Release the on-disk Qdrant lock so a later /config can rebuild on the
        same data dir. Best-effort: local Qdrant holds an exclusive file lock, so
        the old engine must let go before a new one opens the same path."""
        client = getattr(getattr(self._mem, "vector_store", None), "client", None)
        close = getattr(client, "close", None)
        if callable(close):
            # best-effort teardown; never fail a reconfigure on it
            with contextlib.suppress(Exception):
                close()
