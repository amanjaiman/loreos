# lore-memoryd

The Lore memory sidecar: a FastAPI wrapper around [mem0](https://github.com/mem0ai/mem0),
spawned and health-checked by the Lore agent. Binds to `127.0.0.1` only.

Run it directly with `python -m lore_memoryd` (host/port from `LORE_MEMORYD_HOST` /
`LORE_MEMORYD_PORT`); the agent's supervisor launches it the same way.

## Routes

| Method · Path | Purpose |
|---|---|
| `GET /health` | liveness (`{"status":"ok"}`) |
| `POST /config` | (re)initialize mem0 from a provider config |
| `POST /memories` | add (extract + store + reconciliation pass) |
| `POST /memories/search` | ranked semantic search |
| `GET /memories` | list a user's memories (paged: `limit` + `offset`) |
| `GET·PATCH·DELETE /memories/{id}` | single-item ops |

mem0 + Qdrant are **pinned** (`mem0ai==2.0.5`); the engine is built by
[`mem0_factory.py`](lore_memoryd/mem0_factory.py) and reached only through the
typed [`backend.py`](lore_memoryd/backend.py) seam. The `follow_provider`
embedder policy uses the provider's own first-party embeddings when available
(OpenAI → `text-embedding-3-small`, 1536-dim), and otherwise falls back to the
local default (Ollama `nomic-embed-text`, 768-dim) so a chat-only key never
incurs surprise embedding spend — see [`../specs/002-memory-service/`](../specs/002-memory-service/).

## Develop

```sh
cd memoryd
pip install -e ".[dev]"
ruff check .
ruff format --check .
mypy
pytest
```

The default suite uses an in-memory fake backend, so it needs no mem0/Ollama and
runs in CI. To exercise the routes against a **real** mem0 + Qdrant + Ollama
(`nomic-embed-text` and a chat model pulled, `ollama serve` running):

```sh
LORE_TEST_OLLAMA=1 pytest tests/test_integration_ollama.py
```
