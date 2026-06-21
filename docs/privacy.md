# Privacy

> Lore reads what is on your screen. Its privacy posture is therefore the whole
> product, not a feature of it. This document is normative and kept in sync with
> the code: a PR that changes what leaves your machine **must** update it in the
> same change (constitution §4.3).

## Principles in one paragraph

Lore is **local by default, forever**: no account, no Lore-operated server in the
data path, everything bound to `127.0.0.1`. There is **zero telemetry** — not
opt-out, none. You **bring your own intelligence**: a model API key or a URL to a
model you control. Lore never proxies inference and never ships a key.

## Network egress

This is the **complete, closed list** of outbound network calls Lore may make.
Because every external system is reached through exactly one seam
(see [`architecture.md`](architecture.md)), this list is auditable by grepping
those seams.

| # | Destination | Who configures it | Seam | When |
|---|---|---|---|---|
| 1 | The **model endpoint you configured** (`provider.type`: Anthropic / OpenAI / Gemini API, or your `openai_compatible` `base_url`) | User (`config.json → provider`) | `IInferenceBackend` (capture analysis) and `memoryd`/mem0 (memory extraction + embeddings) | Whenever capture distills an observation or memory extracts/embeds. With a local `openai_compatible` endpoint (e.g. Ollama) this is loopback only |
| 2 | `memory.remote_url` (user-hosted mem0 server) | User (`config.json → memory.remote_url`) | `MemorydClient` | Only when `memory.engine: "remote"`; default `"embedded"` keeps memory on the machine. **Carries your provider config — including the model API key** — because extraction and embedding then run on *your* remote memoryd (see below) |

**This list is closed.** The model endpoint in row 1 is the single destination
for all model traffic — Lore never proxies inference and never ships a key (spec
004). The key is read from the Windows Credential Manager at call time and sent
only to that endpoint (for cloud providers) or kept on loopback (for a local
`openai_compatible` endpoint). When the provider is a local model (Ollama / LM
Studio / vLLM by `base_url`) and `memory.engine` is the default `"embedded"`,
**there are zero non-loopback outbound calls.** `memoryd` + mem0 run locally
against an on-disk Qdrant store; if you opt in to `engine: "remote"`,
`MemorydClient` calls the `remote_url` you host (row 2), not a Lore-operated
service.

**Remote mode sends your provider config to your server.** When
`memory.engine: "remote"`, the agent applies your provider to the remote memoryd
the same way it would a local one: it reads the key from the Windows Credential
Manager and POSTs the provider config (**including the key**) to `remote_url`, so
*your* remote memoryd can run extraction and embeddings server-side. That endpoint
is no longer loopback, so it is yours to secure — bind it to a trusted network or
front it with a tunnel/VPN. See [`multi-device.md`](multi-device.md). Nothing in
this path reaches a Lore-operated service.

### What is *never* an outbound call

- **No telemetry, analytics, crash reporting, or usage pings.** Any future usage
  insight would be a separate, opt-in, documented, inspectable feature — never a
  default, and it would appear in the table above.
- **No cloud sync, no account service, no hosted inference proxy.** These existed
  in the pre-open-source codebase and are deleted, not ported.
- **No update check.** Lore never reaches out to check for, or download, updates —
  there is no in-app version check, no "check for updates" button, no auto-updater.
  Updates flow entirely through your **package manager** (`winget upgrade Lore`),
  which checks only when *you* run it; the package manager is the trusted party that
  does the checking, not Lore. This is a deliberate design choice (spec 012) that
  keeps the egress table below unchanged: distributing updates this way adds **no**
  outbound call to the app or agent.
- **mem0 / Qdrant in embedded mode** are local. The bundled `memoryd` sidecar
  runs on loopback and does not leave the machine. **Note:** mem0 OSS ships
  anonymous PostHog telemetry (`us.i.posthog.com`) *enabled by default*;
  `memoryd` force-disables it (`MEM0_TELEMETRY=False`, set in
  `lore_memoryd/__init__.py` before mem0 loads) so it never fires. This is
  verified by a test (`tests/test_telemetry_disabled.py`) and is non-negotiable
  per constitution §1.2. In `engine: "remote"` mode the outbound call to the
  user-hosted mem0 server is row 2 of the egress table above.

## Where your data lives

- **Captured memories** are stored locally by mem0 in an embedded, on-disk Qdrant
  vector store under your user profile. Nothing is uploaded.
- **Secrets** (model API keys) live in the **Windows Credential Manager (DPAPI)**,
  referenced from `config.json` by handle. They are never written to `config.json`
  and never logged (constitution §4.2).
- **The repository contains no secrets.** `gitleaks` scans every PR and the full
  history; see [`SECURITY.md`](../SECURITY.md).

## Filtering before storage

Capture passes through the sensitivity filter chain (blocklist → UIA structural →
regex) **before** it is stored or could ever be sent to a configured model. That
chain is the project's most heavily tested code (constitution §6).

## How to verify these claims yourself

1. Watch the loopback interface while Lore runs — you should see traffic only to a
   model endpoint **you** configured, and nothing otherwise.
2. Grep the codebase: outbound HTTP exists only behind `IInferenceBackend` and
   `MemorydClient`; UI Automation only behind the capture extractor interfaces.
   Anything else is a bug — please report it (see [`SECURITY.md`](../SECURITY.md)).
