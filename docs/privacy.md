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
| 1 | The **model endpoint you configured** (`provider.type`: Anthropic / OpenAI / Gemini API, or your `openai_compatible` `base_url`) | User (`config.json → provider`) | `IInferenceBackend` (capture analysis) and `memoryd`/mem0 (memory embeddings) | Whenever capture distills an observation or memory embeds. With a local `openai_compatible` endpoint (e.g. Ollama) this is loopback only |
| 2 | `memory.remote_url` (user-hosted mem0 server) | User (`config.json → memory.remote_url`) | `MemorydClient` | Only when `memory.engine: "remote"`; default `"embedded"` keeps memory on the machine. **Carries your provider config — including the model API key** — because embedding then runs on *your* remote memoryd (see below) |
| 3 | `https://lore-hazel.vercel.app` — the **update feed**, a Vercel deployment operated by this project | Not configurable | `autoUpdate.ts` (Electron/Squirrel `autoUpdater`) | Packaged Windows builds only: at launch, then every 10 minutes. Sends nothing but the version you are on (it is in the request path) and what any HTTP request reveals — your IP and user agent. Serves back a release manifest and, when one exists, the update package |

**This list is closed.** The model endpoint in row 1 is the single destination
for all model traffic — Lore never proxies inference and never ships a key (spec
004). The key is read from the Windows Credential Manager at call time and sent
only to that endpoint (for cloud providers) or kept on loopback (for a local
`openai_compatible` endpoint). When the provider is a local model (Ollama / LM
Studio / vLLM by `base_url`) and `memory.engine` is the default `"embedded"`,
**the only remaining non-loopback call is the update check** (row 3); nothing
about your activity or memories is in it. `memoryd` + mem0 run locally against an
on-disk Qdrant store; if you opt in to `engine: "remote"`, `MemorydClient` calls
the `remote_url` you host (row 2), not a Lore-operated service.

**Row 3 is the one Lore-operated destination, and it deserves plain language.**
The update feed is not in the data path — it never sees your screen, your
memories, your config or your key. But a packaged Windows build contacts it every
ten minutes for as long as Lore is running, which since v2-006 can be whenever
your machine is on. That is a recurring connection to a host this project
operates, and it necessarily reveals your IP address and the version you are
running. We do not log or analyse it, and it is not telemetry in intent — but a
poll that regular is a heartbeat whether or not anyone counts it, and this
document exists to tell you it is there rather than to reassure you about it.

**There is currently no way to turn row 3 off** short of blocking the host. If
that matters to you, install through winget instead and the update *installs* on
your schedule — but the in-app check still runs. Making it opt-out is on the
[roadmap](roadmap.md).

**Remote mode sends your provider config to your server.** When
`memory.engine: "remote"`, the agent applies your provider to the remote memoryd
the same way it would a local one: it reads the key from the Windows Credential
Manager and POSTs the provider config (**including the key**) to `remote_url`, so
*your* remote memoryd can run embeddings server-side (mem0's config also requires
the chat provider, though raw-store memoryd never invokes it). That endpoint
is no longer loopback, so it is yours to secure — bind it to a trusted network or
front it with a tunnel/VPN. See [`multi-device.md`](multi-device.md). Nothing in
this path reaches a Lore-operated service.

### What is *never* an outbound call

- **No telemetry, analytics, crash reporting, or usage pings.** Any future usage
  insight would be a separate, opt-in, documented, inspectable feature — never a
  default, and it would appear in the table above.
- **No cloud sync, no account service, no hosted inference proxy.** These existed
  in the pre-open-source codebase and are deleted, not ported.
- **No usage, content, or identity in the update check.** Lore *does* check for
  updates now — row 3 above. This bullet used to say the opposite, and was wrong from
  the moment in-app updates landed until this correction. What the check sends is the
  version string already in the request path. It does not send, and there is no code
  path that could send, a machine identifier, an install ID, a user identifier,
  capture content, memories, or config.
- **The tray and background mode add nothing outbound.** Since v2-006 Lore keeps
  running when you close its window, and its Electron main process polls
  `GET http://127.0.0.1:7842/system/status` (every 5s) to draw the tray icon, plus
  `PATCH /config` when you pause or resume from the tray menu. Those are the same
  two calls the app window already makes, to the same loopback address — no new
  destination, and nothing leaves the machine. Running in the background changes
  *how long* Lore captures, not *where* anything goes.
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

1. Watch your network while Lore runs. You should see traffic to exactly two kinds
   of destination: a model endpoint **you** configured, and — on a packaged Windows
   build — `lore-hazel.vercel.app` at launch and every ten minutes thereafter.
   Anything else is a bug.
2. Grep the codebase: outbound HTTP exists only behind `IInferenceBackend`,
   `MemorydClient`, and the app's `autoUpdate.ts`; UI Automation only behind the
   capture extractor interfaces. Anything else is a bug — please report it (see
   [`SECURITY.md`](../SECURITY.md)).
3. Read `app/src/autoUpdate.ts` end to end — it is under 100 lines, and the feed URL
   it builds is the whole of what the update check sends.
