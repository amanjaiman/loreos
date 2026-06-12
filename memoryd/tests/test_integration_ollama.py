"""End-to-end route test against a REAL mem0 + on-disk Qdrant + local Ollama.

Opt-in: runs only when ``LORE_TEST_OLLAMA=1`` *and* an Ollama server is reachable.
This keeps it out of the default suite (CI has no Ollama; the local gate would
otherwise pay a slow real-LLM round-trip on every run). Run it deliberately with
`nomic-embed-text` + a chat model pulled. The pinned-mem0 contract suite (T007)
is the CI-enforced version of this.
"""

from __future__ import annotations

import os
import urllib.error
import urllib.request
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from lore_memoryd.app import create_app

OLLAMA_URL = "http://localhost:11434"


def _ollama_up() -> bool:
    try:
        with urllib.request.urlopen(f"{OLLAMA_URL}/api/tags", timeout=2) as resp:
            return bool(resp.status == 200)
    except (urllib.error.URLError, TimeoutError, OSError):
        return False


pytestmark = pytest.mark.skipif(
    os.environ.get("LORE_TEST_OLLAMA") != "1" or not _ollama_up(),
    reason="set LORE_TEST_OLLAMA=1 with a reachable Ollama to run the real-mem0 e2e test",
)


def test_add_then_search_against_real_mem0(tmp_path: Path) -> None:
    client = TestClient(create_app())  # default factory → real mem0
    config = {
        "provider": {"type": "ollama", "model": "qwen2.5:7b-instruct"},
        "embedder": {"type": "ollama", "model": "nomic-embed-text", "dims": 768},
        "data_dir": str(tmp_path / "lore"),
        "collection_name": "lore_it",
    }
    assert client.post("/config", json=config).status_code == 200, "config should initialize mem0"

    added = client.post(
        "/memories",
        json={"text": "The user is allergic to penicillin.", "user_id": "it-user"},
    )
    assert added.status_code == 200, added.text

    found = client.post(
        "/memories/search",
        json={"query": "What allergies does the user have?", "user_id": "it-user"},
    )
    assert found.status_code == 200, found.text
    memories = " ".join(m["memory"].lower() for m in found.json()["results"])
    assert "penicillin" in memories


def _config(tmp_path: Path) -> dict[str, object]:
    return {
        "provider": {"type": "ollama", "model": "qwen2.5:7b-instruct"},
        "embedder": {"type": "ollama", "model": "nomic-embed-text", "dims": 768},
        "data_dir": str(tmp_path / "lore"),
        "collection_name": "lore_it",
    }


def test_contradiction_converges_to_one_memory(tmp_path: Path) -> None:
    # Acceptance criterion 3, via the Option-C reconciliation pass: a contradicting
    # fact should update rather than duplicate — the stale city should be gone.
    client = TestClient(create_app())
    assert client.post("/config", json=_config(tmp_path)).status_code == 200

    client.post("/memories", json={"text": "The user lives in Seattle.", "user_id": "u"})
    client.post("/memories", json={"text": "The user just moved to Austin, Texas.", "user_id": "u"})

    everything = " ".join(
        m["memory"].lower()
        for m in client.get("/memories", params={"user_id": "u"}).json()["results"]
    )
    assert "austin" in everything  # current truth retained
    assert "seattle" not in everything  # stale fact reconciled away
