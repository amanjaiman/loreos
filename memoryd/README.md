# lore-memoryd

The Lore memory sidecar: a FastAPI wrapper around [mem0](https://github.com/mem0ai/mem0),
spawned and health-checked by the Lore agent. Binds to `127.0.0.1` only.

Spec 001 ships the importable package and `GET /health`; memory routes and the
pinned mem0 dependency arrive with spec 002.

## Develop

```sh
cd memoryd
pip install -e ".[dev]"
ruff check .
ruff format --check .
mypy
pytest
```
