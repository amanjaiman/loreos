# Lore

**The open-source ambient capture agent for your personal memory layer.**

Lore watches what you do on your machine — locally, auditably, with aggressive
filtering — distills what matters with a model *you* control, stores it in
[mem0](https://github.com/mem0ai/mem0), and serves it to every AI tool you use
through MCP, a CLI, an agent skill, and a documented REST API. No account, no
telemetry, no Lore-operated server: your models, your keys, your disk, your
memory. The rules that keep it that way live in
[constitution.md](constitution.md).

> **Status:** pre-release. The repository is being built spec by spec
> ([specs/](specs/)); component skeletons exist and CI enforces the
> constitution, but capture and memory behavior are still landing.

## Prerequisites (Windows 10/11)

| Tool | Version | Pinned by |
|---|---|---|
| .NET SDK | 8.0.2xx | [`global.json`](global.json) |
| Python | 3.11+ | [`memoryd/pyproject.toml`](memoryd/pyproject.toml) |
| Node.js | 20+ (LTS) | [`app/package.json`](app/package.json) `engines` |

## Build every component

```sh
# Agent + CLI + tests (C#)
dotnet build Lore.sln
dotnet test Lore.sln

# Memory sidecar (Python)
cd memoryd
pip install -e ".[dev]"
pytest
cd ..

# Desktop app (Electron + React)
cd app
npm ci
npm run build
npm start   # opens the placeholder window
```

## Repository layout

| Directory | Contents |
|---|---|
| `agent/` | C# capture agent: pipeline, MCP server, local API host |
| `cli/` | `lore` CLI, a thin client of the local API |
| `memoryd/` | Python FastAPI sidecar wrapping mem0 |
| `app/` | Electron + React desktop app |
| `skills/` | Agent Skill packages (spec 008) |
| `docs/` | Architecture, privacy, integration guides |
| `installer/` | Windows packaging (spec 011) |
| `specs/` | Spec-driven development artifacts, one folder per feature |

## Contributing

Work flows through specs and ships through the `no-mistakes` delivery gate —
see [CONTRIBUTING.md](CONTRIBUTING.md), [AGENTS.md](AGENTS.md), and
[constitution.md](constitution.md). Security policy:
[SECURITY.md](SECURITY.md).

## License

[Apache 2.0](LICENSE)
