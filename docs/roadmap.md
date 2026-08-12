# Roadmap

> Where Lore is headed. This is a direction, not a commitment or a dated plan —
> priorities shift with what people actually need. Issues labeled
> [`good-first-issue`](https://github.com/amanjaiman/loreos/labels/good-first-issue)
> are the easiest way in.
>
> **What won't change:** the [constitution](../constitution.md) — local-by-default,
> zero telemetry, bring-your-own-model, a closed and documented egress list. Nothing
> below trades those away. There is **no hosted/cloud offering** on this roadmap; the
> architecture leaves that door open (open-core) but none of it ships here.

## Now (shipped or landing)

- ✅ Windows capture → filter → distill → mem0, exposed over a loopback API.
- ✅ Surfaces: MCP (stdio + HTTP), the `lore` CLI, an agent skill, a REST API, the
  desktop app.
- ✅ One signed-ready Windows installer bundling everything (Python invisible).
- ✅ Tag-driven releases + **winget** distribution: `winget install Lore` /
  `winget upgrade Lore` (spec 012), since joined by in-app Squirrel updates against
  a Hazel feed — spec 012's "no in-app check" stance was reversed, and
  [`privacy.md`](privacy.md) row 3 is the disclosure.
- ✅ Background app: system tray with running/paused/stopped, lifecycle control from
  the tray and the app, start with Windows (spec v2-006).
- ⏳ Distribution polish: a code-signing certificate (signing is wired, opt-in), a
  branded installer icon, and a redacting production log sink for `/system/log`.

## Next

| Item | What | Labels |
|---|---|---|
| **macOS capture** | A capture backend for macOS (Accessibility API / screen text), behind the existing extractor seam. | `platform:macos` `area:capture` |
| **Homebrew distribution** | Distribute + update on macOS through **Homebrew** (`brew install lore` / `brew upgrade lore`), mirroring the winget package-manager model on Windows (spec 012) — the same user-pulled, zero-egress update story, no bespoke updater. | `platform:macos` `area:installer` |
| **Local review mode** | An opt-in "hold for review" path where captured observations are queued for you to approve/redact before they're stored — for the most sensitive workflows. | `area:capture` `type:feature` |
| **Encryption at rest** | Encrypt the on-disk memory store (Qdrant + history) so a stolen disk doesn't expose memories. | `area:memory` `type:feature` |
| **Memory buckets + retention** | Deferred from the memory spec: scope memories into buckets (work/personal/…) and set retention/expiry policies. | `area:memory` `type:feature` |
| **More MCP surfaces** | First-class `lore mcp install` presets for additional clients beyond Claude Desktop / Claude Code / Cursor. | `surface:mcp` `area:cli` |
| **Remote memoryd auth** | Optional authentication for a self-hosted memoryd so multi-device doesn't depend solely on network trust. | `area:memory` `type:feature` |
| **Opt out of the update check** | A setting to disable the in-app update poll ([`privacy.md`](privacy.md) row 3) for users who want the machine to make no unconfigured outbound calls at all, leaving winget as their update path. | `area:app` `type:feature` |

## Later

| Item | What | Labels |
|---|---|---|
| **Linux capture** | A Linux capture backend (Wayland/X11 text), behind the extractor seam. | `platform:linux` `area:capture` |
| **Browser extension** | A `surface:browser` capture/recall surface for in-page context. | `surface:browser` `type:feature` |
| **Future surfaces** | More `surface:*` integrations as the MCP/skill ecosystem grows. | `surface:*` `type:feature` |

## Labels

Issues and roadmap items are tagged so you can filter to your interest:

- **`platform:`** `windows` · `macos` · `linux`
- **`surface:`** `mcp` · `cli` · `app` · `skill` · `browser`
- **`area:`** `capture` · `memory` · `providers` · `app` · `cli` · `installer` · `docs`
- **`type:`** `feature` · `enhancement` · `bug`
- **`good-first-issue`** — small, well-scoped, a good place to start.

## Explicitly not planned

- A hosted/managed Lore service or cloud sync (constitution §1).
- Telemetry/analytics of any kind, opt-in or otherwise (constitution §1.2).
- Proxying inference through Lore-operated infrastructure (constitution §2).
