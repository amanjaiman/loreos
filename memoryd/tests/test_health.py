"""Acceptance test for spec 001: GET /health returns 200 {"status": "ok"}."""

from fastapi.testclient import TestClient

from lore_memoryd.app import create_app


def test_health_returns_ok() -> None:
    client = TestClient(create_app())
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
