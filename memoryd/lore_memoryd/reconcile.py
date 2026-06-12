"""Post-add conflict reconciliation (spike T001, Option C).

mem0 2.0.x's ``add()`` is additive + exact-hash dedup only — it does not run the
LLM ADD/UPDATE/DELETE reconciliation older mem0 did, so semantically-duplicate or
contradictory facts accumulate (spike Finding 1). To keep acceptance criterion 3
("a contradicting fact updates rather than duplicates"), memoryd reconciles after
each add: for every newly-added memory it inspects near neighbors and asks an LLM
judge whether the new memory **duplicates**, **supersedes**, or is **unrelated** to
each, then deletes the redundant side so the store converges to one current fact.

The judge is injectable so the logic is unit-tested without a live LLM; the default
judge uses the configured provider's model via the mem0 ``Memory``.
"""

from __future__ import annotations

from collections.abc import Callable
from typing import Any, Literal, Protocol

from .models import AddedMemory

Verdict = Literal["duplicate", "supersedes", "unrelated"]
# (new_memory, existing_memory) -> how the new one relates to the existing one.
Judge = Callable[[str, str], Verdict]


class ReconcileMemory(Protocol):
    """The slice of mem0's ``Memory`` reconciliation needs (mocked in tests)."""

    def search(self, query: str, *, filters: dict[str, Any], top_k: int) -> Any: ...

    def delete(self, memory_id: str) -> Any: ...


def _results(raw: Any) -> list[dict[str, Any]]:
    items = raw.get("results", []) if isinstance(raw, dict) else raw
    return [i for i in items if isinstance(i, dict)] if isinstance(items, list) else []


_SYSTEM = (
    "You decide how a NEW memory about a user relates to an EXISTING memory. "
    "Answer with exactly one word: DUPLICATE, SUPERSEDES, or UNRELATED.\n"
    "- DUPLICATE: they assert the same fact (even if worded differently).\n"
    "- SUPERSEDES: the NEW memory updates, replaces, or contradicts the EXISTING "
    "one — the user's situation changed and NEW is the current truth.\n"
    "- UNRELATED: they are about different facts."
)


def make_llm_judge(memory: Any) -> Judge:
    """A `Judge` backed by the configured provider LLM (via mem0's ``Memory.llm``)."""

    def judge(new_memory: str, existing_memory: str) -> Verdict:
        response = memory.llm.generate_response(
            messages=[
                {"role": "system", "content": _SYSTEM},
                {
                    "role": "user",
                    "content": f"EXISTING: {existing_memory}\nNEW: {new_memory}\nAnswer:",
                },
            ]
        )
        return _parse_verdict(str(response))

    return judge


def _parse_verdict(text: str) -> Verdict:
    lowered = text.lower()
    if "duplicate" in lowered:
        return "duplicate"
    if "supersede" in lowered:
        return "supersedes"
    return "unrelated"


class Reconciler:
    """Reconciles newly-added memories against their near neighbors."""

    def __init__(
        self,
        memory: ReconcileMemory,
        judge: Judge,
        *,
        user_id_filter_key: str = "user_id",
        max_neighbors: int = 3,
        threshold: float = 0.5,
    ) -> None:
        self._mem = memory
        self._judge = judge
        self._key = user_id_filter_key
        self._max_neighbors = max_neighbors
        self._threshold = threshold

    def reconcile(self, added: list[AddedMemory], user_id: str) -> list[AddedMemory]:
        """Return the surviving memories with events adjusted (UPDATE when the new
        memory superseded an existing one; the new memory is dropped when it merely
        duplicated an existing one)."""
        survivors: list[AddedMemory] = []
        for am in added:
            if am.event != "ADD":
                survivors.append(am)
                continue
            survivor = self._reconcile_one(am, user_id)
            if survivor is not None:
                survivors.append(survivor)
        return survivors

    def _reconcile_one(self, am: AddedMemory, user_id: str) -> AddedMemory | None:
        event = "ADD"
        for neighbor in self._neighbors(am, user_id):
            verdict = self._judge(am.memory, str(neighbor.get("memory", "")))
            if verdict == "duplicate":
                if am.id:
                    self._mem.delete(am.id)
                return None
            if verdict == "supersedes":
                nid = str(neighbor.get("id", ""))
                if nid:
                    self._mem.delete(nid)
                    event = "UPDATE"
        return AddedMemory(id=am.id, memory=am.memory, event=event)

    def _neighbors(self, am: AddedMemory, user_id: str) -> list[dict[str, Any]]:
        raw = self._mem.search(
            am.memory, filters={self._key: user_id}, top_k=self._max_neighbors + 1
        )
        out: list[dict[str, Any]] = []
        for d in _results(raw):
            if str(d.get("id", "")) == am.id:
                continue  # the just-added memory itself
            score = d.get("score")
            if score is not None and score < self._threshold:
                continue
            out.append(d)
        return out[: self._max_neighbors]
