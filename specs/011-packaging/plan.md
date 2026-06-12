# 011 — Packaging & Launch Readiness · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Bundle the three components into one installer, prove the first-run on clean VMs,
finalize the trust-grade docs, do a focused security pass, and stage the public
launch. This spec writes glue and docs, not product behavior.

## Structure

```
installer/
├── build.(ps1|js)        # orchestrates: publish agent, build app, PyInstaller memoryd, assemble
├── memoryd.spec          # PyInstaller spec → single-exe sidecar (from 002's proven approach)
└── forge.config.*        # electron-forge make (Squirrel) bundling everything + CLI on PATH
docs/
├── privacy.md            # FINAL egress list + filter explanation
├── multi-device.md       # self-hosted mem0 walkthrough (from 002/006 remote mode)
└── roadmap.md            # public roadmap with surface:* and platform labels
README.md                 # demo GIF + 3-step quickstart
```

## Packaging

- **agent**: `dotnet publish -c Release` (self-contained `win-x64`).
- **memoryd**: PyInstaller per `memoryd.spec` → single exe; the agent's supervisor
  (002) launches the packaged exe in production and `python -m lore_memoryd` in dev.
  Exclude `torch`, `transformers`, and `scipy`/`sklearn` (not used at runtime with the
  Ollama/local embedder) to keep the exe ~175 MB rather than ~358 MB
  (spec 002 spike Finding 4). Consider `--onedir` over `--onefile` if cold-start
  latency (temp-dir extraction on first launch) is too large for the supervisor's
  health-gate budget.
- **app**: electron-forge make (Squirrel installer) bundles the published agent, the
  memoryd exe, and puts `lore` (CLI) on PATH.
- **signing**: code-sign the agent, CLI, memoryd exe, and installer to limit AV
  false positives.
- CI builds the installer reproducibly from pinned inputs.

## First-run verification

On fresh Windows 10 and 11 VMs: install → app launches → memoryd supervised →
onboarding → provider setup with a green `/providers/test` → connect Claude Desktop
via `lore mcp install` → confirm a captured memory surfaces in Claude. This is the
"ten-minute test" from the project plan, run for real.

## Launch docs

- **README**: one-liner, demo GIF, prerequisites (none for users), 3-step quickstart
  (install → pick model → connect Claude), pointers to `docs/`.
- **privacy.md**: the final, authoritative version — what's captured, the three
  filter layers, where data lives, and the **complete** outbound-call list (only
  user-configured model endpoints). This file is a launch blocker.
- **multi-device.md**: stand up a self-hosted mem0 server, set `memory.engine:
  remote` on each device, verify cross-device recall — the original vision with zero
  Lore infrastructure.
- **roadmap.md**: macOS capture, Linux, browser extension, local review mode,
  encryption at rest, deferred buckets/retention, and future `surface:*` integrations.

## Security pass

Before public: re-review the filter chain (003), the loopback-only binding (005),
credential storage (004), and diff actual network egress against `privacy.md`. Run
`gitleaks` over the **full history**. Document the pass. (Reminder: the legacy
`lore/v1` repo and its embedded Supabase credentials are never used as a base.)

## Launch staging

Polish issue/PR templates; seed 5–10 good-first-issues across components; write the
launch checklist (Show HN, r/LocalLLaMA, mem0 community/Discord as "an ambient
capture agent for mem0", MCP server directories, skill/plugin marketplaces). When
the repo is made public, enable branch protection on the default branch requiring the
four CI checks (`csharp`, `python`, `app`, `secrets`) — this completes the deferred
spec 001 / T009 work (GitHub blocks branch protection on free private repos).

## Decisions

- **One installer, Python invisible** — bundled memoryd exe; users never see Python
  (BYO-model honesty doesn't extend to making them manage a runtime).
- **Privacy doc is a launch blocker**, not a nice-to-have — it is the trust artifact
  for screen-watching software.
- **Open-core preserved, not built** — nothing here forecloses a future managed
  offering, but no hosted code ships.

## Dependencies & order

Upstream: **001–009** (and **010** if the app ships at launch). Internal order:
PyInstaller memoryd → installer assembly + signing → first-run VM verification →
README/privacy/multi-device docs → security pass → roadmap + good-first-issues +
launch checklist. See [`tasks.md`](tasks.md).
