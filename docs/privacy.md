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
| 1 | `memory.remote_url` (user-hosted mem0 server) | User (`config.json → memory.remote_url`) | `MemorydClient` | Only when `memory.engine: "remote"`; default `"embedded"` makes zero outbound calls |

**With the default `memory.engine: "embedded"` there are zero outbound calls.**
No model provider exists yet (the model seam, `IInferenceBackend`, arrives with
spec 004), and `memoryd` + mem0 run locally against an on-disk Qdrant store. If
you opt in to `engine: "remote"`, `MemorydClient` calls the `remote_url` you
configure — a server you host, not a Lore-operated service. As soon as a model
provider lands, the only permitted destination will be the **model endpoint the
user explicitly configured** — enumerated in the table above, in the same PR that
adds it.

### What is *never* an outbound call

- **No telemetry, analytics, crash reporting, or usage pings.** Any future usage
  insight would be a separate, opt-in, documented, inspectable feature — never a
  default, and it would appear in the table above.
- **No cloud sync, no account service, no hosted inference proxy.** These existed
  in the pre-open-source codebase and are deleted, not ported.
- **No update check** unless and until added here explicitly.
- **mem0 / Qdrant in embedded mode** are local. The bundled `memoryd` sidecar
  runs on loopback and does not leave the machine. **Note:** mem0 OSS ships
  anonymous PostHog telemetry (`us.i.posthog.com`) *enabled by default*;
  `memoryd` force-disables it (`MEM0_TELEMETRY=False`, set in
  `lore_memoryd/__init__.py` before mem0 loads) so it never fires. This is
  verified by a test (`tests/test_telemetry_disabled.py`) and is non-negotiable
  per constitution §1.2. In `engine: "remote"` mode the outbound call to the
  user-hosted mem0 server is listed in row 1 of the egress table above.

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
