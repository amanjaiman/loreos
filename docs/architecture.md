# Architecture

> How Lore is put together and the invariants that keep it trustworthy. This is
> the narrative form of [`constitution.md`](../constitution.md) §3–§4; where they
> disagree, the constitution wins.

## One behavior layer, many thin shims

Lore has exactly **one place where product behavior lives**: the agent's local
HTTP API on `127.0.0.1:7842`, backed by `IMemoryService`. Everything a user can
reach Lore through — the desktop app, the MCP server, the `lore` CLI, the
documented REST surface — is a **thin, stateless client of that API**. A new
integration surface is a translation layer, never a new copy of the logic.

```
                 user's own model endpoint
                 (OpenAI-compatible URL/key)
                            ▲
                            │  IInferenceBackend   ← the only model seam
                            │
   ┌────────────────────────────────────────────────────────┐
   │                    agent  (C# / .NET 8)                  │
   │                                                          │
   │   capture pipeline ──► filter chain ──► smart gating     │
   │        ▲                                     │           │
   │        │ UIA / Win32                         ▼           │
   │   extractor seams                    IMemoryService      │
   │                                            │  ▲          │
   │     local HTTP API  127.0.0.1:7842  ───────┘  │          │
   │            ▲  ▲  ▲  ▲                          │          │
   └────────────┼──┼──┼──┼──────────────────────────┼─────────┘
                │  │  │  │                           │ MemorydClient
        ┌───────┘  │  │  └────────┐                  ▼
        │          │  │           │            ┌───────────┐
      app        CLI  MCP       REST           │  memoryd  │ (Python/FastAPI)
   (Electron)         server   clients         │   + mem0  │
                                               │  + Qdrant │ (embedded, on-disk)
                                               └───────────┘
   └──────────── thin shims over the one API ───────────┘
```

## The invariants (why the boxes don't leak)

1. **One behavior layer.** All behavior sits behind the local API / `IMemoryService`.
   Surfaces never duplicate it. If a feature appears in two surfaces, it belongs in
   the API instead.
2. **One seam per external system.** Each outside system is reached through exactly
   one interface, and nothing else in the codebase talks to it:
   - **mem0** ↔ `IMemoryService` / `MemorydClient` only.
   - **model APIs** ↔ `IInferenceBackend` only.
   - **Win32 / UI Automation** ↔ the capture extractor interfaces only.

   No other file makes outbound HTTP or P/Invoke calls. This is what makes the
   egress list in [`privacy.md`](privacy.md) closed and auditable.
3. **The agent is the supervisor.** In `engine: "embedded"` mode it spawns the
   `memoryd` sidecar and health-gates it (`GET /health`); in `engine: "remote"`
   mode it health-gates the user-configured endpoint without spawning anything.
   The agent runs **headless** — the app is optional UI, never a runtime
   dependency.
4. **Localhost only, always.** No surface binds to an external interface. The MCP
   server speaks stdio or local Streamable HTTP. There is no remote listener and no
   Lore-operated server in the data path.
5. **Dependency injection, no global state.** Services are constructor-injected via
   the .NET generic host. Config is loaded once and refreshed by a file watcher,
   never re-read per access; there is no static mutable state.

## Components

| Component | Stack | Role |
|---|---|---|
| `agent` | C# / .NET 8 (`net8.0-windows`) | Capture pipeline, MCP server, local API host, supervisor |
| `cli` (`lore`) | C# / .NET 8 console | Thin client of the local API |
| `memoryd` | Python 3.11+, FastAPI, mem0 | Bundled sidecar wrapping mem0; Qdrant on-disk vector store. Alternatively, a user-hosted instance targeted via `engine: "remote"`. |
| `app` | Electron + React + TS | Onboarding, library, settings — talks only to the local API |

## Trust-critical path

Capture is **filtered before it is stored or sent**. The sensitivity filter chain
(blocklist → UIA structural → regex) is the highest-tested code in the project
(constitution §6). Everything downstream — gating, storage, serving — assumes the
filter has already removed what must never leave the machine.

## Reading order for a new contributor

[`constitution.md`](../constitution.md) → this file → the spec you are working
(`specs/NNN-name/`). The build/run commands for each component live in that
component's spec `plan.md` and are mirrored in the root [`README.md`](../README.md).
