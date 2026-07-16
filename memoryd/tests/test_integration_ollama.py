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


def test_raw_add_then_search_against_real_mem0(tmp_path: Path) -> None:
    # v2-001 raw-store mode: the add stores the caller's text VERBATIM (no LLM
    # extraction — the chat model is configured but never invoked), metadata
    # rides along, and real-embedder search retrieves it with a filter applied.
    client = TestClient(create_app())  # default factory → real mem0
    config = {
        "provider": {"type": "ollama", "model": "qwen2.5:7b-instruct"},
        "embedder": {"type": "ollama", "model": "nomic-embed-text", "dims": 768},
        "data_dir": str(tmp_path / "lore"),
        "collection_name": "lore_it",
    }
    assert client.post("/config", json=config).status_code == 200, "config should initialize mem0"

    text = "I'm allergic to penicillin."
    added = client.post(
        "/memories",
        json={
            "text": text,
            "user_id": "it-user",
            "metadata": {"kind": "identity", "status": "active"},
        },
    )
    assert added.status_code == 200, added.text
    results = added.json()["results"]
    assert len(results) == 1
    assert results[0]["memory"] == text  # verbatim — nothing rewrote the fact

    found = client.post(
        "/memories/search",
        json={
            "query": "What allergies does the user have?",
            "user_id": "it-user",
            "filters": {"status": "active"},
        },
    )
    assert found.status_code == 200, found.text
    memories = found.json()["results"]
    assert any(m["memory"] == text for m in memories)
    hit = next(m for m in memories if m["memory"] == text)
    assert hit["metadata"] == {"kind": "identity", "status": "active"}
