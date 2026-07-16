# 011 — Packaging & Launch Readiness · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — PyInstaller the memoryd sidecar  `[deps: spec 002]`
Write `installer/memoryd.spec` and build a single-exe `memoryd` (from 002's proven
packaging approach). Exclude `torch`, `transformers`, and `scipy`/`sklearn` (unused
at runtime) to target ~175 MB (vs ~358 MB with the full ML stack — spec 002 spike
Finding 4). Decide `--onefile` vs `--onedir` based on the supervisor health-gate
budget: `--onefile` unpacks to a temp dir on first launch (cold-start overhead);
`--onedir` avoids extraction at the cost of a flat file tree. The spike's
`lore-memoryd-proto.exe --selftest` pattern (prints frozen dep versions, exits 0) is
the clean-VM gate: copy the exe to a machine with no Python/Ollama and confirm it
exits clean. Make the 002 supervisor launch the packaged exe in production.
**Done when:** the memoryd exe runs standalone on a clean VM and the agent supervises
it in a Release build.

### T002 — Installer assembly + signing  `[deps: T001, spec 005, spec 007]`
Configure electron-forge make (Squirrel) to bundle the published agent, the memoryd
exe, and put `lore` (CLI) on PATH. Code-sign agent, CLI, memoryd, and installer.
Build it reproducibly in CI.
**Done when:** CI produces one signed installer from pinned inputs (acceptance
criterion 1, build half).

### T003 — First-run verification on clean VMs  `[deps: T002]`
Install on fresh Windows 10 and 11 VMs; run onboarding → provider setup → green
`/providers/test` → `lore mcp install` → confirm a captured memory surfaces in
Claude Desktop.
**Done when:** the end-to-end first-run works on both VMs and the steps are recorded
(acceptance criteria 1, 2).

### T004 — README + quickstart + demo GIF  `[deps: T003]`
Write `README.md`: one-liner, demo GIF, "what Lore is", 3-step quickstart (install →
pick model → connect Claude) matching reality.
**Done when:** a newcomer following the README reaches "Claude used a Lore memory" in
under ten minutes (acceptance criterion 3).

### T005 — Final privacy + multi-device docs  `[deps: T003]`
Finalize `docs/privacy.md` (filter layers + complete egress list, matched to code)
and `docs/multi-device.md` (self-hosted mem0 walkthrough, verified).
**Done when:** privacy egress list matches actual network calls; the multi-device
walkthrough works (acceptance criterion 4).

### T006 — Security pass  `[deps: T004, T005]`
Re-review filter chain (003), loopback binding (005), credential storage (004); diff
actual egress against `privacy.md`; run `gitleaks` over full history. Document the
pass.
**Done when:** the pass is documented, `gitleaks` is clean over history, and the
trust-critical paths are confirmed (acceptance criterion 5).

### T007 — Roadmap + good-first-issues + launch checklist  `[deps: T006]`
Write `docs/roadmap.md` (macOS/Linux/browser-ext/local-review/encryption/deferred
buckets+retention/future surfaces with labels); seed 5–10 good-first-issues; write
the launch checklist. **When the repo is made public, enable branch protection on
the default branch requiring the four CI checks (`csharp`, `python`, `app`,
`secrets`)** — this is the deferred completion of spec 001 / T009, which GitHub
blocks on free private repos.
**Done when:** roadmap, issues, and checklist exist; the repo has been made public
and branch protection requiring the four CI checks is active on the default branch
(completing spec 001 / T009).

---

## Definition of done for spec 011

A single signed installer delivers a working, trust-documented Lore on clean
Windows VMs; the README gets a newcomer running fast; privacy and multi-device docs
are accurate; the security pass is clean; launch material is staged. Lore is ready
to go open source.
