"""Reconciliation logic (spike Option C) — tested with a fake memory + judge."""

from __future__ import annotations

from typing import Any

from lore_memoryd.models import AddedMemory
from lore_memoryd.reconcile import Reconciler, Verdict, _parse_verdict


class FakeReconcileMem:
    """Records deletes; returns a fixed neighbor set from search()."""

    def __init__(self, neighbors: list[dict[str, Any]]) -> None:
        self._neighbors = neighbors
        self.deleted: list[str] = []

    def search(self, query: str, *, filters: dict[str, Any], top_k: int) -> dict[str, Any]:
        return {"results": self._neighbors}

    def delete(self, memory_id: str) -> None:
        self.deleted.append(memory_id)


def _judge_returning(verdict: Verdict) -> Any:
    def judge(new_memory: str, existing_memory: str) -> Verdict:
        return verdict

    return judge


_NEW = [AddedMemory(id="new", memory="The user is relocating to Austin.", event="ADD")]


def test_supersedes_deletes_old_and_marks_update() -> None:
    mem = FakeReconcileMem([{"id": "old", "memory": "The user lives in Seattle.", "score": 0.9}])
    survivors = Reconciler(mem, _judge_returning("supersedes")).reconcile(_NEW, "u1")
    assert mem.deleted == ["old"]  # stale fact removed
    assert len(survivors) == 1 and survivors[0].id == "new" and survivors[0].event == "UPDATE"


def test_duplicate_drops_the_new_memory() -> None:
    mem = FakeReconcileMem(
        [{"id": "old", "memory": "The user is moving to Austin.", "score": 0.95}]
    )
    survivors = Reconciler(mem, _judge_returning("duplicate")).reconcile(_NEW, "u1")
    assert mem.deleted == ["new"]  # new memory dropped, existing kept
    assert survivors == []


def test_unrelated_keeps_both_as_add() -> None:
    mem = FakeReconcileMem([{"id": "old", "memory": "The user owns a cat.", "score": 0.8}])
    survivors = Reconciler(mem, _judge_returning("unrelated")).reconcile(_NEW, "u1")
    assert mem.deleted == []
    assert len(survivors) == 1 and survivors[0].event == "ADD"


def test_neighbor_below_threshold_is_skipped() -> None:
    mem = FakeReconcileMem([{"id": "old", "memory": "irrelevant", "score": 0.1}])
    survivors = Reconciler(mem, _judge_returning("supersedes")).reconcile(_NEW, "u1")
    assert mem.deleted == []  # never judged → never deleted
    assert survivors[0].event == "ADD"


def test_self_is_excluded_from_neighbors() -> None:
    mem = FakeReconcileMem(
        [{"id": "new", "memory": "The user is relocating to Austin.", "score": 1.0}]
    )
    survivors = Reconciler(mem, _judge_returning("duplicate")).reconcile(_NEW, "u1")
    assert mem.deleted == []  # would-be self-match ignored
    assert survivors[0].event == "ADD"


def test_non_add_events_pass_through_untouched() -> None:
    mem = FakeReconcileMem([{"id": "old", "memory": "x", "score": 0.9}])
    none_event = [AddedMemory(id="n", memory="m", event="NONE")]
    survivors = Reconciler(mem, _judge_returning("supersedes")).reconcile(none_event, "u1")
    assert mem.deleted == [] and survivors == none_event


def test_supersedes_neighbor_without_id_skips_delete_and_survives_as_add() -> None:
    mem = FakeReconcileMem([{"memory": "The user lives in Seattle.", "score": 0.9}])
    survivors = Reconciler(mem, _judge_returning("supersedes")).reconcile(_NEW, "u1")
    assert mem.deleted == []
    assert len(survivors) == 1 and survivors[0].id == "new" and survivors[0].event == "ADD"


def test_parse_verdict() -> None:
    assert _parse_verdict("DUPLICATE") == "duplicate"
    assert _parse_verdict("The answer is SUPERSEDES.") == "supersedes"
    assert _parse_verdict("unrelated") == "unrelated"
    assert _parse_verdict("garbage") == "unrelated"  # safe default keeps both
