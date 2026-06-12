# 011 — Packaging & Launch Readiness · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** all of
> 001–009 (010 desktop app if shipped by launch).

## Overview

This spec turns the components into something a stranger can install and trust, and
prepares the repository to go public. It bundles the agent, the app, and the
PyInstaller'd `memoryd` sidecar into one Windows installer with a clean first-run
flow; writes the launch-grade docs (README with demo, privacy, multi-device); does a
focused security pass over the trust-critical paths; and stages the open-source
launch.

## User stories

- **As a new user**, I download one installer, run it, pick my model, connect Claude,
  and Lore works — in under ten minutes, with no Python or build tools visible.
- **As a skeptical developer**, I can read the repo and, in under 30 minutes,
  confirm the filter chain, the loopback-only binding, and the complete list of
  outbound calls.
- **As a multi-device user**, the docs show me how to point several devices at one
  self-hosted mem0 server with no Lore-operated infrastructure.

## Scope

### In scope

- **Installer**: bundles `agent` + `app` + PyInstaller'd `memoryd` (+ CLI on PATH);
  Windows installer (e.g. Squirrel/electron-forge make). Code-signed binaries.
- **First-run flow**: clean install → onboarding → provider setup → connect a client,
  verified on fresh Windows 10 and 11 VMs.
- **Launch docs**: `README.md` (demo GIF + 3-step quickstart: install → pick model →
  connect Claude), `docs/privacy.md` (final: what's captured, the filter layers,
  the complete egress list), `docs/multi-device.md` (self-hosted mem0 walkthrough).
- **Security pass**: review the trust-critical paths (filter chain, loopback
  binding, credential storage, egress list) before the repo flips public; run the
  secret scanner over the full history one more time.
- **Launch staging**: issue/PR templates polished, 5–10 good-first-issues seeded, a
  public roadmap doc (macOS capture, Linux, browser extension, local review mode,
  encryption at rest, future surfaces), and a launch checklist (Show HN,
  r/LocalLLaMA, mem0 community, MCP/skill directories).

### Out of scope

- Building product features (their own specs).
- macOS/Linux packaging (roadmap).
- Any hosted/cloud offering (explicitly out — see the project plan's open-core note;
  not architected away, not built now).

## Acceptance criteria

1. A single signed installer installs agent + app + bundled `memoryd` + CLI on a
   clean Windows 10/11 VM with no developer prerequisites; the app launches and
   memoryd is supervised.
2. First-run completes onboarding, a successful `/providers/test`, and a connected
   MCP client end-to-end on a fresh VM.
3. `README.md` has a demo GIF and a 3-step quickstart that matches reality; a
   newcomer reaches "Claude used a Lore memory" in under ten minutes.
4. `docs/privacy.md` enumerates every outbound call (only user-configured model
   endpoints) and matches the code; `docs/multi-device.md` walkthrough works.
5. The security pass is documented; `gitleaks` is clean over the full history; the
   trust-critical paths are confirmed.
6. Roadmap, good-first-issues, and the launch checklist exist; the repo is ready to
   be made public.

## Non-functional requirements

- **Reproducible installer build** in CI from pinned inputs.
- **No prerequisites** for end users (Python is bundled and invisible).
- **Clean history**: no secret ever existed in the public repo's history
  (constitution §4); the legacy `lore/v1` repo is never used as a base.

## Risks

- *PyInstaller bundle triggers AV false positives / bloats size.* Mitigation:
  code-sign; the packaging approach was de-risked in 002's spike; test on clean VMs.
- *A last-minute outbound call slips the egress list.* Mitigation: the security pass
  diffs actual network calls against `docs/privacy.md`; CI secret scan on full
  history.
