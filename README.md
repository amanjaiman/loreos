# Lore

**The open-source ambient capture agent for your personal memory layer.**

<!-- Demo GIF (spec 011 T004): record per docs/assets/README.md, save as
docs/assets/demo.gif, then replace the line below with:
![Lore in action](docs/assets/demo.gif) -->

> 🎬 _Demo GIF coming._

Lore watches what you do on your machine — locally, auditably, with aggressive
filtering — distills what matters with a model *you* control, stores it in
[mem0](https://github.com/mem0ai/mem0), and serves it to every AI tool you use
through MCP, a CLI, an agent skill, and a documented REST API. No account, no
telemetry, no Lore-operated server: your models, your keys, your disk, your
memory. The rules that keep it that way live in
[constitution.md](constitution.md).

> **Status:** preparing the open-source launch (spec 011). One signed Windows
> installer bundles everything; the privacy and trust docs are the launch contract.

## Quickstart (Windows 10/11) — under ten minutes

No Python, .NET, or build tools required — the installer bundles them.

1. **Install.** Download the latest **`LoreSetup.exe`** from
   [Releases](https://github.com/amanjaiman/loreos/releases) and run it. Lore opens
   when it finishes; the memory engine starts in the background.
2. **Pick your model.** Onboarding shows the privacy explainer first, then asks you
   to connect a model — a cloud key (Anthropic / OpenAI / Gemini) or a URL to a local
   model you run (Ollama, LM Studio, …). **Test** confirms it works before you
   continue. (More: [docs/providers.md](docs/providers.md).)
3. **Connect Claude.** In any terminal:

   ```sh
   lore mcp install claude-desktop      # also: claude-code, cursor
   ```

   Restart Claude Desktop, then ask: *"Using my Lore memory, what have I been working
   on?"* — Claude calls Lore and answers from your memory.

That's it: Claude now reads and writes your Lore memory. Other clients and the raw
REST/MCP surfaces are in [docs/integrations/](docs/integrations/).

## What Lore is

Lore is the connective tissue between what you do and the AI tools you use. It runs a
small **capture agent** that reads on-screen text, passes it through a
heavily-tested **sensitivity filter chain** (blocklist → structural → regex) *before*
anything is stored, and asks **your** model to distill the durable facts. Those go
into a local [mem0](https://github.com/mem0ai/mem0) store and are exposed to every
client through one loopback API.

What makes it trustworthy:

- **Local by default, forever** — no account, no cloud sync, everything bound to
  `127.0.0.1`. Point several devices at one server *you* host if you want shared
  memory ([docs/multi-device.md](docs/multi-device.md)).
- **Bring your own model** — a key or a local URL; Lore never proxies inference and
  never ships a key.
- **Zero telemetry; a closed, documented egress list** — the *only* outbound calls
  are to the model endpoint you configured. See [docs/privacy.md](docs/privacy.md);
  it is normative and kept in sync with the code.

## Docs

| Doc | What |
|---|---|
| [docs/privacy.md](docs/privacy.md) | What's captured, the filter layers, the complete egress list |
| [docs/architecture.md](docs/architecture.md) | The seams: capture, memory, providers, the local API |
| [docs/providers.md](docs/providers.md) | Choosing and configuring your model |
| [docs/multi-device.md](docs/multi-device.md) | Share one memory store across machines (self-hosted) |
| [docs/integrations/](docs/integrations/) | Connect Claude Desktop, Claude Code, Cursor, the CLI, raw MCP/HTTP |
| [docs/api.md](docs/api.md) | The local REST API |

## Build from source (developers)

Prerequisites: .NET SDK 8.0.2xx ([`global.json`](global.json)), Python 3.11+
([`memoryd/pyproject.toml`](memoryd/pyproject.toml)), Node.js 20+
([`app/package.json`](app/package.json)).

```sh
# Agent + CLI + tests (C#)
dotnet build Lore.sln
dotnet test Lore.sln

# Memory sidecar (Python)
cd memoryd && pip install -e ".[dev]" && pytest && cd ..

# Desktop app (Electron + React)
cd app && npm ci && npm start
```

Build the installer (publishes the agent + CLI, freezes memoryd, runs
electron-forge): [`installer/build.ps1`](installer/build.ps1) — see
[`installer/README.md`](installer/README.md).

## Repository layout

| Directory | Contents |
|---|---|
| `agent/` | C# capture agent: pipeline, MCP server, local API host |
| `cli/` | `lore` CLI, a thin client of the local API |
| `memoryd/` | Python FastAPI sidecar wrapping mem0 |
| `app/` | Electron + React desktop app |
| `skills/` | Agent Skill packages (spec 008) |
| `docs/` | Architecture, privacy, providers, integration guides |
| `installer/` | Windows packaging (spec 011) |
| `specs/` | Spec-driven development artifacts, one folder per feature |

## Contributing

Work flows through specs and ships through the `no-mistakes` delivery gate —
see [CONTRIBUTING.md](CONTRIBUTING.md), [AGENTS.md](AGENTS.md), and
[constitution.md](constitution.md). Security policy: [SECURITY.md](SECURITY.md).

## License

[Apache 2.0](LICENSE)
