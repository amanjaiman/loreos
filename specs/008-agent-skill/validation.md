# 008 — Agent Skill · Interactive validation (T003)

> The Lore skill is prose; its quality is judged by how a real agent *behaves* with
> it loaded (plan: "validation is interactive against a real agent"). This is the
> turnkey procedure for that validation, covering acceptance criteria **2** (the skill
> drives search-before-personalizing and records durable facts) and **5** (`lore
> skills install claude-code` installs the package and it loads).
>
> Run it against your own running Lore, then fill in the [Results](#results) table and
> commit this file. T003 is done when the table shows the documented behaviors
> observed.

## 1. Prerequisites — a running, configured Lore

The skill drives the `lore` CLI, which talks to a running Lore with a model provider
configured (it calls *your* model; nothing leaves your machine).

1. **Build the CLI and agent:**
   ```sh
   dotnet build cli/Lore.Cli.csproj -c Release
   dotnet build agent/LoreAgent.csproj -c Release
   ```
2. **Put `lore` on your PATH** (or call it by full path). The CLI resolves the agent
   and the bundled skill **relative to its own folder**, so for a dev build, copy the
   skill package next to the CLI binary so the installer can find it:
   ```sh
   # from the repo root, into the CLI's build output:
   robocopy skills\lore cli\bin\Release\net8.0\skills\lore /E
   ```
   (The packaged product ships `LoreAgent.exe` and `skills/lore/` alongside `lore.exe`,
   so this step is dev-only.)
3. **Start Lore and configure a provider** (onboarding, or `lore config set
   provider.model …` / `lore config set provider.api_key …`). Then confirm it's up:
   ```sh
   lore status
   # agent: running · memoryd: ready · provider: ready
   ```
   If `provider` is `unconfigured`, finish onboarding first — the model is what
   distills and searches memory.

## 2. Install the skill (acceptance criterion 5)

```sh
lore skills install claude-code
```

Expected: it reports `Installed the Lore skill for claude-code → …\.claude\skills\lore`
and the files exist there (`SKILL.md`). Open a **new** Claude Code session in any
project and confirm the skill is available (it appears in the skills list / `/help`,
and can be invoked as `/lore`). **Record:** did it install and load? (criterion 5)

## 3. Behavioral scenarios (acceptance criterion 2)

Run each in a **fresh** Claude Code session (so only the skill, not chat history,
drives behavior). For each, note whether the agent ran the expected `lore` command and
whether its answer reflected memory. Watching the tool calls it makes is the signal.

### A. Searches before personalizing
1. Seed a fact: `lore add "The user prefers pytest over unittest" --category preferences`
2. In a fresh session, ask: **"What testing framework should I use for a new Python
   project?"**
3. **Expect:** the agent runs `lore search "…" --json` (e.g. for "testing framework"),
   then recommends **pytest**, citing your preference.

### B. Records a durable fact
1. In a fresh session, say: **"For future reference, I always deploy to Azure, not
   AWS."**
2. **Expect:** the agent runs `lore add "…Azure…"`. Verify it landed:
   `lore recent --limit 5` shows the new memory.

### C. Forgets on request
1. Say: **"Actually, forget what you know about my deployment preference."**
2. **Expect:** the agent runs `lore forget "deployment"` (or similar). Verify:
   `lore search "deployment" --json` no longer returns it.

### D. Stays out of the way (when NOT to use)
1. In passing, mention a transient/secret detail: **"My temp debug token for this run
   is abc123."**
2. **Expect:** the agent does **not** run `lore add` for it (it's transient and
   secret-like). `lore recent` should not show it.

### E. Degrades gracefully when Lore is down
1. Stop Lore (close the app / stop the agent).
2. In a fresh session, ask a personalized question (as in A).
3. **Expect:** the agent proceeds with a normal answer and does **not** error out or
   block; it may note that Lore isn't running. (`lore` exits `3`.)

## 4. Tuning

If a scenario fails — the agent over-calls, under-calls, or misreads `--json` — adjust
the wording in [`skills/lore/SKILL.md`](../../skills/lore/SKILL.md) (the description
drives *when* the host surfaces it; the body drives *how*), reinstall, and re-run. Note
any wording changes you made in the Results table.

## Results

> Fill in after running. Validated against Lore version `____`, CLI `____`, on `____`
> (date), in Claude Code `____`.

| # | Scenario | Observed behavior | Pass? |
|---|---|---|---|
| 5 | Install + load (`lore skills install claude-code`) | | ☐ |
| A | Searches before personalizing | | ☐ |
| B | Records a durable fact | | ☐ |
| C | Forgets on request | | ☐ |
| D | Ignores transient/secret details | | ☐ |
| E | Degrades gracefully when Lore is down | | ☐ |

**Wording changes made during tuning:** _none / describe_

**Overall:** ☐ criteria 2 & 5 met — T003 complete.
