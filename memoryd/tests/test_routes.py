"""Route-level tests against the fake backend — wiring, serialization, errors."""

from __future__ import annotations

from typing import Any, NoReturn

from fastapi.testclient import TestClient

from lore_memoryd.app import create_app
from lore_memoryd.mem0_factory import EmbedderConfigError
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


def test_list_memories_paginates_with_limit_and_offset(
    client: TestClient, sample_config: dict[str, Any]
) -> None:
    _configure(client, sample_config)
    for i in range(5):
        client.post("/memories", json={"text": f"fact {i}", "user_id": "u1"})

    page1 = client.get("/memories", params={"user_id": "u1", "limit": 2, "offset": 0}).json()[
        "results"
    ]
    page2 = client.get("/memories", params={"user_id": "u1", "limit": 2, "offset": 2}).json()[
        "results"
    ]
    page3 = client.get("/memories", params={"user_id": "u1", "limit": 2, "offset": 4}).json()[
        "results"
    ]

    assert len(page1) == 2
    assert len(page2) == 2
    assert len(page3) == 1  # last partial page signals the end
    ids = {m["id"] for m in page1 + page2 + page3}
    assert len(ids) == 5  # no overlap across pages


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
    mem_id = client.post(
        "/memories",
        json={
            "text": "User likes tea.",
            "user_id": "u1",
            "metadata": {"kind": "preference", "status": "active"},
        },
    ).json()["results"][0]["id"]

    got = client.get(f"/memories/{mem_id}")
    assert got.status_code == 200 and got.json()["memory"] == "User likes tea."

    # Text-only patch must PRESERVE metadata (the mem0 wipe footgun — v2-001 spike Q4).
    patched = client.patch(f"/memories/{mem_id}", json={"text": "User likes coffee."})
    assert patched.status_code == 200 and patched.json()["memory"] == "User likes coffee."
    assert patched.json()["metadata"] == {"kind": "preference", "status": "active"}

    # Metadata-only patch merges keys and leaves the text alone.
    patched = client.patch(f"/memories/{mem_id}", json={"metadata": {"status": "archived"}})
    assert patched.status_code == 200
    assert patched.json()["memory"] == "User likes coffee."
    assert patched.json()["metadata"] == {"kind": "preference", "status": "archived"}

    assert client.delete(f"/memories/{mem_id}").status_code == 200
    assert client.get(f"/memories/{mem_id}").status_code == 404


def test_patch_requires_text_or_metadata(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)
    mem_id = client.post("/memories", json={"text": "x", "user_id": "u1"}).json()["results"][0][
        "id"
    ]
    assert client.patch(f"/memories/{mem_id}", json={}).status_code == 400


def test_list_and_search_accept_metadata_filters(
    client: TestClient, sample_config: dict[str, Any]
) -> None:
    _configure(client, sample_config)
    client.post(
        "/memories",
        json={"text": "User visited France.", "user_id": "u1", "metadata": {"kind": "experience"}},
    )
    client.post(
        "/memories",
        json={"text": "User visited the dentist.", "user_id": "u1", "metadata": {"kind": "state"}},
    )

    listed = client.get(
        "/memories", params={"user_id": "u1", "filters": '{"kind": "experience"}'}
    ).json()["results"]
    assert [m["memory"] for m in listed] == ["User visited France."]

    found = client.post(
        "/memories/search",
        json={"query": "visited somewhere", "user_id": "u1", "filters": {"kind": "state"}},
    ).json()["results"]
    assert [m["memory"] for m in found] == ["User visited the dentist."]


def test_unsupported_filters_return_400(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)
    # $-prefixed operator → actionable 400, not a deep mem0 ValueError.
    resp = client.post(
        "/memories/search",
        json={"query": "q", "user_id": "u1", "filters": {"status": {"$ne": "archived"}}},
    )
    assert resp.status_code == 400 and "operator" in resp.json()["detail"]
    # Boolean trees are not supported by the pinned mem0.
    resp = client.post(
        "/memories/search",
        json={"query": "q", "user_id": "u1", "filters": {"AND": [{"kind": "state"}]}},
    )
    assert resp.status_code == 400
    # Malformed list filters JSON → 400.
    assert (
        client.get("/memories", params={"user_id": "u1", "filters": "{not json"}).status_code == 400
    )


def test_missing_id_returns_404(client: TestClient, sample_config: dict[str, Any]) -> None:
    _configure(client, sample_config)
    assert client.get("/memories/nope").status_code == 404
    assert client.patch("/memories/nope", json={"text": "x"}).status_code == 404
    meta_patch = client.patch("/memories/nope", json={"metadata": {"status": "archived"}})
    assert meta_patch.status_code == 404
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


def test_configure_surfaces_actionable_embedder_error(sample_config: dict[str, Any]) -> None:
    # An EmbedderConfigError is actionable (the user must configure an embedder), so its
    # message is surfaced — but still redacted of any secret (spec 013).
    secret = "sk-shouldnotleak123"

    def failing_factory(_cfg: ConfigRequest) -> NoReturn:
        raise EmbedderConfigError(
            f"Provider 'anthropic' has no first-party embeddings (key {secret}); "
            "set embedder.type, embedder.model, embedder.base_url, embedder.dims."
        )

    error_client = TestClient(create_app(backend_factory=failing_factory))
    resp = error_client.post("/config", json=sample_config)
    assert resp.status_code == 400
    body = resp.json()["detail"]
    assert "no first-party embeddings" in body  # actionable guidance surfaced
    assert "embedder.type" in body
    assert secret not in body  # still redacted


def test_health_still_ok(client: TestClient) -> None:
    assert client.get("/health").json() == {"status": "ok"}


def test_redact_masks_sk_token() -> None:
    result = _redact("Incorrect API key: sk-proj-abc123XYZ")  # gitleaks:allow
    assert result == "Incorrect API key: [REDACTED]"
    assert _redact("Bearer eyJhbGc.some.token") == "[REDACTED]"
    assert _redact("no secrets here") == "no secrets here"


def test_reconfigure_closes_previous_engine(sample_config: dict[str, Any]) -> None:
    # /config is idempotent: a second call must release the prior engine (freeing
    # the on-disk Qdrant lock) before building the new one on the same data dir.
    from conftest import FakeBackend

    created: list[FakeBackend] = []

    def factory(_cfg: ConfigRequest) -> FakeBackend:
        backend = FakeBackend()
        created.append(backend)
        return backend

    client = TestClient(create_app(backend_factory=factory))
    assert client.post("/config", json=sample_config).status_code == 200
    assert client.post("/config", json=sample_config).status_code == 200
    assert created[0].closed is True  # first engine disposed
    assert created[1].closed is False  # second engine live
