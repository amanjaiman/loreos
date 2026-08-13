# Lore

**The open-source memory layer for every AI tool you use.**

<!-- Demo GIF (spec 011 T004): record per docs/assets/README.md, save as
docs/assets/demo.gif, then replace the line below with:
![Lore in action](docs/assets/demo.gif) -->

> 🎬 _Demo GIF coming._

Mention tooth pain this week; next week, when you ask any connected assistant
about ordering dinner, it already knows to steer you toward soft foods. That's
Lore: it watches what you do on your machine — locally, auditably, with
aggressive filtering — and keeps a small set of **durable, typed facts about
you** (never a log of your day), distilled skeptically by a model *you*
control. Every AI tool you use can check that memory on **every message**
through MCP, a CLI, an agent skill, and a documented REST API. No account, no
telemetry, no Lore-operated server: your models, your keys, your disk, your
memory. The rules that keep it that way live in
[constitution.md](constitution.md).

> **Status:** the v2 memory-layer restart has landed (specs/v2). One signed Windows
> installer bundles everything; the privacy and trust docs are the launch contract.

## Quickstart (Windows 10/11) — under ten minutes

No Python, .NET, or build tools required — the installer bundles them.

1. **Install.** Either install with winget:

   ```sh
   winget install Lore
   ```

   or download the latest **`LoreSetup.exe`** from
   [Releases](https://github.com/amanjaiman/loreos/releases) and run it. Lore opens
   when it finishes; the memory engine starts in the background, and Lore keeps
   running in your system tray when you close the window. It updates itself: it
   checks a project-operated feed at launch and every ten minutes, and installs
   quietly in the background. `winget upgrade Lore` also works. The update check is
   the one call Lore makes that you did not configure — what it does and does not
   send is row 3 of [docs/privacy.md](docs/privacy.md).
2. **Pick your model.** Onboarding shows the privacy explainer first, then asks you
   to connect a model — a cloud key (Anthropic / OpenAI / Gemini) or a URL to a local
   model you run (Ollama, LM Studio, …). **Test** confirms it works before you
   continue. (More: [docs/providers.md](docs/providers.md).)
3. **Connect Claude.** In any terminal:

   ```sh
   lore connect
   ```

   Restart Claude Desktop, then ask: *"Using my Lore memory, what have I been working
   on?"* — Claude calls Lore and answers from your memory.

That's it: Claude now reads and writes your Lore memory. Other clients and the raw
REST/MCP surfaces are in [docs/integrations/](docs/integrations/).

## What Lore is

Lore is the connective tissue between what you do and the AI tools you use. A
small **capture agent** reads on-screen text and passes it through a
heavily-tested **sensitivity filter chain** (blocklist → structural → regex)
*before* anything else sees it. Related activity groups into **episodes**; your
model is asked one skeptical question per episode — *"what durable fact about
the user does this support?"* — where **"nothing" is the expected answer**. Real
facts stage first and are kept once a second episode supports them, capped by a
daily budget. What's kept is typed (identity · preference · state · experience ·
project), expires when it stops being true, lives in a local
[mem0](https://github.com/mem0ai/mem0) store, and is **recalled by relevance on
every message** through one loopback API — with a decision trail that always
answers "why does (or doesn't) Lore know that?" The model and how it works:
[docs/memory-model.md](docs/memory-model.md).

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
| [docs/memory-model.md](docs/memory-model.md) | What Lore remembers, how skeptical capture and recall work |
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
| `specs/v2/` | The active spec set (the v2 memory-layer restart); `specs/v1-archive/` keeps the retired v1 specs for provenance |

## Contributing

Work flows through specs and ships through the `no-mistakes` delivery gate —
see [CONTRIBUTING.md](CONTRIBUTING.md), [AGENTS.md](AGENTS.md), and
[constitution.md](constitution.md). Security policy: [SECURITY.md](SECURITY.md).

## License

[Apache 2.0](LICENSE)
