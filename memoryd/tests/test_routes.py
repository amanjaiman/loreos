"""Route-level tests against the fake backend — wiring, serialization, errors."""

from __future__ import annotations

from typing import Any, NoReturn

from fastapi.testclient import TestClient

from lore_memoryd.app import create_app
from lore_memoryd.models import ConfigRequest
from lore_memoryd.routes import _redact


def test_memory_routes_require_config(client: TestClient) -> None:
    # No /config call yet → memory routes report 503, not a crash.
    assert client.post("/memories", json={"text": "hi"}).status_code == 503
    assert client.get("/memories").status_code == 503


def _configure(client: TestClient, sample_config: dict[str, Any]) -> None:
    resp = client.post("/config", json=sample_config)
    assert resp.status_code == 200, resp.text
    assert resp.json()["collection_name"] == "lore_test"


def test_add_then_search_and_get_all(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)

    added = client.post(
        "/memories", json={"text": "The user is allergic to penicillin.", "user_id": "u1"}
    )
    assert added.status_code == 200
    results = added.json()["results"]
    assert len(results) == 1 and results[0]["event"] == "ADD"

    found = client.post("/memories/search", json={"query": "allergy penicillin", "user_id": "u1"})
    assert found.status_code == 200
    memories = found.json()["results"]
    assert any("penicillin" in m["memory"].lower() for m in memories)

    all_for_user = client.get("/memories", params={"user_id": "u1"})
    assert all_for_user.status_code == 200
    assert len(all_for_user.json()["results"]) == 1


def test_search_is_user_scoped(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)
    client.post("/memories", json={"text": "User drives a Subaru.", "user_id": "u1"})
    # Different user → no leak.
    assert (
        client.post("/memories/search", json={"query": "subaru", "user_id": "u2"}).json()["results"]
        == []
    )


def test_get_update_delete_lifecycle(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)
    mem_id = client.post("/memories", json={"text": "User likes tea.", "user_id": "u1"}).json()[
        "results"
    ][0]["id"]

    got = client.get(f"/memories/{mem_id}")
    assert got.status_code == 200 and got.json()["memory"] == "User likes tea."

    patched = client.patch(f"/memories/{mem_id}", json={"text": "User likes coffee."})
    assert patched.status_code == 200 and patched.json()["memory"] == "User likes coffee."

    assert client.delete(f"/memories/{mem_id}").status_code == 200
    assert client.get(f"/memories/{mem_id}").status_code == 404


def test_missing_id_returns_404(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)
    assert client.get("/memories/nope").status_code == 404
    assert client.patch("/memories/nope", json={"text": "x"}).status_code == 404
    assert client.delete("/memories/nope").status_code == 404


def test_configure_error_returns_generic_message(sample_config: dict[str, Any]) -> None:
    secret = "fake-token-12345"

    def failing_factory(_cfg: ConfigRequest) -> NoReturn:
        raise ValueError(f"Invalid API key: {secret}")

    error_client = TestClient(create_app(backend_factory=failing_factory))
    resp = error_client.post("/config", json=sample_config)
    assert resp.status_code == 400
    body = resp.json()["detail"]
    assert secret not in body
    assert "provider config" in body


def test_health_still_ok(client: TestClient) -> None:
    assert client.get("/health").json() == {"status": "ok"}


def test_redact_masks_sk_token() -> None:
    result = _redact("Incorrect API key: sk-proj-abc123XYZ")  # gitleaks:allow
    assert result == "Incorrect API key: [REDACTED]"
    assert _redact("Bearer eyJhbGc.some.token") == "[REDACTED]"
    assert _redact("no secrets here") == "no secrets here"
