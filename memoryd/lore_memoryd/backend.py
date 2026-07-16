"""The typed boundary over mem0, running mem0 as a RAW STORE (v2-001 T002).

`MemoryBackend` is the narrow surface memoryd's routes depend on. `Mem0Backend`
implements it over a mem0 ``Memory``, translating method calls and normalizing
mem0's loosely-typed dict results into our `MemoryItem` / `AddedMemory` models —
so the untyped mem0 surface stops here and routes/tests speak only typed models.

Raw-store rules (v2-001 spike, binding):
- Every add passes ``infer=False``: the agent's distiller already produced the
  fact, so mem0 must store it verbatim — no second extraction, no LLM call.
- Every update re-sends the full **merged** metadata: mem0 2.0.5 wipes metadata
  to ``{}`` on a text-only ``update()`` (spike Q4), so the merge here is what
  keeps a text patch from destroying Lore's typed fields.
- Search/list filters are validated against mem0 2.0.5's supported operator set
  before they reach the engine, so callers get an actionable 400 instead of a
  ValueError from deep inside mem0.
"""

from __future__ import annotations

import contextlib
import logging
from typing import Any, Protocol

from mem0 import Memory

from .models import AddedMemory, MemoryItem

_log = logging.getLogger(__name__)

# mem0 2.0.5's supported per-field filter operators (spike Q3). `$`-prefixed
# spellings and AND/OR trees are NOT supported by the pinned version.
_ALLOWED_FILTER_OPS = frozenset(
    {"eq", "ne", "in", "nin", "gt", "gte", "lt", "lte", "contains", "icontains"}
)


def validate_filters(filters: dict[str, Any] | None) -> None:
    """Reject filter shapes the pinned mem0 does not support, with guidance.

    Valid: a flat dict of ``field: value`` (equality) or ``field: {op: value}``
    with ops from `_ALLOWED_FILTER_OPS`. Raises ``ValueError`` otherwise.
    """
    if not filters:
        return
    for field, value in filters.items():
        if field in ("AND", "OR", "NOT"):
            raise ValueError(
                f"unsupported filter {field!r}: the pinned mem0 takes a flat "
                "{field: value} or {field: {op: value}} dict, not boolean trees"
            )
        if isinstance(value, dict):
            unknown = set(value) - _ALLOWED_FILTER_OPS
            if unknown:
                raise ValueError(
                    f"unsupported filter operator(s) for field {field!r}: "
                    f"{sorted(unknown)}; supported: {sorted(_ALLOWED_FILTER_OPS)}"
                )


class MemoryBackend(Protocol):
    """What the routes need from a memory engine. Mocked in tests."""

    def add(
        self, text: str, user_id: str, metadata: dict[str, Any] | None
    ) -> list[AddedMemory]: ...

    def search(
        self, query: str, user_id: str, limit: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]: ...

    def get_all(
        self, user_id: str, limit: int, offset: int, filters: dict[str, Any] | None
    ) -> list[MemoryItem]: ...

    def get(self, memory_id: str) -> MemoryItem | None: ...

    def update(
        self, memory_id: str, text: str | None, metadata: dict[str, Any] | None
    ) -> MemoryItem | None: ...

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
    """`MemoryBackend` over a mem0 ``Memory`` in raw-store mode."""

    def __init__(self, memory: Memory) -> None:
        self._mem = memory

    def add(self, text: str, user_id: str, metadata: dict[str, Any] | None) -> list[AddedMemory]:
        # infer=False: store the caller's fact verbatim, zero LLM calls (v2-001).
        raw = self._mem.add(text, user_id=user_id, metadata=metadata, infer=False)
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
        validate_filters(filters)
        merged: dict[str, Any] = {**(filters or {}), "user_id": user_id}
        raw = self._mem.search(query, filters=merged, top_k=limit)
        return [_to_item(d) for d in _results(raw)]

    def get_all(
        self, user_id: str, limit: int, offset: int = 0, filters: dict[str, Any] | None = None
    ) -> list[MemoryItem]:
        # mem0's get_all has no offset, so fetch through the requested window and
        # slice. Pagination lets the agent enumerate a whole store (MemorydClient
        # loops pages); a single huge top_k could otherwise hit an engine cap.
        # Trade-off: each page call fetches top_k = offset + limit items, so a full
        # enumeration over N items costs O(N²) in vector-store round-trips. Acceptable
        # at personal-assistant scale; a native cursor would eliminate this.
        validate_filters(filters)
        merged: dict[str, Any] = {**(filters or {}), "user_id": user_id}
        raw = self._mem.get_all(filters=merged, top_k=offset + limit)
        window = _results(raw)[offset : offset + limit]
        return [_to_item(d) for d in window]

    def get(self, memory_id: str) -> MemoryItem | None:
        raw = self._mem.get(memory_id)
        return _to_item(raw) if isinstance(raw, dict) and raw.get("id") else None

    def update(
        self, memory_id: str, text: str | None, metadata: dict[str, Any] | None
    ) -> MemoryItem | None:
        existing = self.get(memory_id)
        if existing is None:
            return None
        # Always send text AND the full merged metadata: mem0 2.0.5 wipes
        # metadata on a text-only update (spike Q4) — the merge is load-bearing.
        new_text = text if text is not None else existing.memory
        merged_meta = {**(existing.metadata or {}), **(metadata or {})}
        self._mem.update(memory_id, data=new_text, metadata=merged_meta)
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
