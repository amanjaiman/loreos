# Launch checklist

> The steps to take Lore public, in order. Several are **one-way, human-owned**
> actions (publishing screen-watching software, changing GitHub settings, posting to
> communities) — those are called out and are intentionally not automated.

## Gate: before the repo goes public

These must all be true first. The first is the sign-off everything else rides on.

- [ ] **Security pass signed off.** Read [security-pass.md](security-pass.md) end to
      end. It is the artifact standing behind "safe to watch your screen." Re-run the
      `origin/main` gitleaks scan if `main` advanced since the doc's stamped commit.
- [ ] **Full history is clean everywhere.** `origin/main` is verified clean in the
      security pass; before flipping public, **delete the `test/ci-probes` branch**
      (it carries a synthetic scanner-probe token by design) so *no* branch has a
      finding.
- [ ] **First-run verified on clean Windows 10 + 11 VMs** (T003): install → onboarding
      (privacy first) → green provider test → `lore mcp install` → Claude uses a Lore
      memory, with no developer tools on the VM.
- [ ] **README demo GIF recorded** (T004) and the 3-step quickstart matches what you
      saw on the VM.
- [ ] **Installer signed** (or a conscious decision to launch unsigned and accept
      SmartScreen warmup) — see `installer/README.md`.

## Go public (human, one-way)

- [ ] GitHub → Settings → **change visibility to Public.**
- [ ] GitHub → Settings → Branches → **branch protection on `main`**, requiring the
      four status checks: **`csharp`, `python`, `app`, `secrets`**, and PRs before
      merge. This completes the spec 001 / T009 deferral (GitHub blocks branch
      protection on free private repos, which is why it waited for launch).
- [ ] Publish a **GitHub Release** with the signed `LoreSetup.exe` attached, so the
      README's "download from Releases" link works. (The `installer` workflow builds
      the artifact; attach it to the tagged release.)

## Seed the project (human presses publish; drafts are ready)

- [ ] Create the **good-first-issues** from [good-first-issues.md](good-first-issues.md)
      (titles + bodies are paste-ready) and apply the labels from
      [roadmap.md](roadmap.md#labels).
- [ ] Confirm the **issue/PR templates** (`.github/ISSUE_TEMPLATE/`,
      `PULL_REQUEST_TEMPLATE.md`) read well on the public repo.

## Announce (human, after public + Release)

Lead with the honest one-liner — *an open-source, local-first ambient capture agent
for your personal memory layer; bring your own model; zero telemetry.* Link the
README, the demo GIF, and `privacy.md` prominently (it is the trust artifact).

- [ ] **Show HN** — "Show HN: Lore – local-first ambient memory for your AI tools (BYO model)".
- [ ] **r/LocalLLaMA** — emphasize local models (Ollama) + no cloud + the egress list.
- [ ] **mem0 community / Discord** — frame as *"an ambient capture agent for mem0"*.
- [ ] **MCP server directories** — submit the Lore MCP server.
- [ ] **Skill / plugin marketplaces** — submit the agent skill (`skills/lore`).

## After launch

- [ ] Watch the issue tracker; triage with the `roadmap.md` labels.
- [ ] Tackle the distribution-polish follow-ups (signing cert if launched unsigned,
      installer icon, redacting log sink).
