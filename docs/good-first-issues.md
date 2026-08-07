# Good first issues (paste-ready)

> Drafts for the 5–10 good-first-issues to seed at launch. Each block is ready to
> paste into a new GitHub issue: copy the **Title**, apply the **Labels**, and paste
> the body. They're real, well-scoped tasks grounded in the current code — not
> busywork. Apply [`good-first-issue`](roadmap.md#labels) to each.
>
> _This file is a staging draft; the issues themselves are created by a human at
> launch (the agent drafts, you press publish)._

---

## 1. Wire the redacting log sink so `/system/log` works in production

**Labels:** `good-first-issue` `area:app` `type:enhancement`

**Background.** `GET /system/log` tails `%LocalAppData%\LoreData\lore.log` via `LogTail`,
and a `RedactingLoggerProvider` already exists (`agent/Config/RedactingLogger.cs`) to
write that log with secrets scrubbed — but it isn't wired into the host yet, so the
endpoint returns an empty tail in production (see
[`specs/005-local-api/plan.md`](../specs/005-local-api/plan.md), the `/system/log`
note).

**Task.** Register `RedactingLoggerProvider` as a logging provider in the agent host
(`agent/Program.cs`) so logs are written to `LogTail.DefaultPath`. Confirm secrets
registered with `SecretRegistry` are redacted in the written file.

**Acceptance.** With the agent running, `GET /system/log` (and `lore` / the app's
Activity view) returns recent log lines; a deliberately-logged secret value appears
redacted, not in clear text. Add a test that the provider writes to the expected path.

**Difficulty.** Small.

---

## 2. Add a branded installer + app icon

**Labels:** `good-first-issue` `area:installer` `surface:app`

**Background.** The Squirrel installer and the app currently use the default Electron
icon (`app/forge.config.ts` sets no `setupIcon`/`icon`). There's a glyph to start from
at `app/src/renderer/design-system/assets/lore-glyph.svg`.

**Task.** Produce a multi-resolution `.ico` (and PNGs as needed) from the Lore glyph,
add it under `app/`, and wire `packagerConfig.icon` + `MakerSquirrel`'s `setupIcon`
(and `iconUrl`) in `forge.config.ts`.

**Acceptance.** `installer/build.ps1` produces an installer and app that show the Lore
icon (taskbar, Add/Remove Programs, the setup exe).

**Difficulty.** Small (asset + config).

---

## 3. Fix the MCP integration docs to lead with `lore mcp install`

**Labels:** `good-first-issue` `area:docs`

**Background.** `docs/integrations/claude-desktop.md` (and `cursor.md`) tell users to
hardcode `C:\Program Files\Lore\LoreAgent.exe`. The packaged app installs per-user
under a **versioned** Squirrel path that changes on update, so that hardcoded path is
wrong. The supported, path-stable way is `lore mcp install <client>` (the CLI on PATH,
which resolves the current agent path automatically — `cli/Commands/Install/`).

**Task.** Update the integration docs to lead with `lore mcp install claude-desktop`
(and `cursor`, `claude-code`) as step 1, and reframe the manual JSON as a fallback
that notes the path is the versioned install location, not `C:\Program Files\Lore`.

**Acceptance.** The integration docs no longer instruct users toward a path that
won't exist after a normal install; the `lore mcp install` flow is the primary path.

**Difficulty.** Small (docs).

---

## 4. Add a `lore mcp install` preset for another MCP client

**Labels:** `good-first-issue` `surface:mcp` `area:cli`

**Background.** `lore mcp install` supports `claude-desktop`, `claude-code`, and
`cursor` today (`cli/Commands/Install/McpInstallCommand.cs`, the `Clients` array +
the `ResolveConfigPath` switch). Other MCP clients (e.g. Windsurf, Continue, Zed) use
the same `mcpServers` JSON shape.

**Task.** Add one more client: extend the `Clients` list and the config-path switch
with its on-disk config location, reusing the existing `McpConfigInstaller` merge.
Add a CLI test mirroring the existing client tests, and a short doc under
`docs/integrations/`.

**Acceptance.** `lore mcp install <new-client>` writes a correct, idempotent `lore`
entry into that client's config; a test covers it.

**Difficulty.** Small–medium.

---

## 5. Ship a curated default blocklist seed for onboarding

**Labels:** `good-first-issue` `area:capture`

**Background.** The capture blocklist starts empty (`Blocklist.Empty`,
`agent/Capture/Blocklist.cs`); onboarding seeds it (spec 010 T004). A sensible default
set (password managers, banking/2FA apps, etc.) would make first-run safer out of the
box.

**Task.** Propose a small, well-justified default list of app executables/keywords
and wire it as the seed the onboarding "Tune capture" step starts from. Keep it
conservative and documented (each entry should have an obvious reason).

**Acceptance.** A fresh install's blocklist is pre-seeded with the curated defaults,
still fully editable by the user; the list is documented.

**Difficulty.** Small (plus a little judgment).

---

## 6. Add an app tray icon and keep capturing when the window is closed

**Labels:** `good-first-issue` `surface:app` `type:feature`

**Background.** The app spawns and supervises the agent for its lifetime
(`app/src/agentProcess.ts`), but closing the window quits the app and stops capture.
For an ambient-capture product, closing the window should minimize to a tray and keep
the agent running.

**Task.** Add a system tray icon (Open Lore / Pause / Quit), make window-close hide to
tray instead of quitting on Windows, and ensure `agent.stop()` still runs on a real
quit. (Pairs well with #2, the icon asset.)

**Acceptance.** Closing the window leaves Lore capturing with a tray presence;
"Quit" from the tray stops the agent cleanly.

**Difficulty.** Medium.

---

## 7. Expand the provider setup docs with more local + hosted presets

**Labels:** `good-first-issue` `area:providers` `area:docs`

**Background.** `docs/providers.md` documents choosing a model. The provider layer
supports Anthropic, OpenAI, Gemini, and any OpenAI-compatible `base_url` (Ollama, LM
Studio, vLLM, …).

**Task.** Add concrete, copy-pasteable setup snippets for the common local servers
(Ollama, LM Studio, vLLM via `base_url`) and a note on the minimum capable
extraction model (≈7B-class local or any frontier hosted model — see the spec 002
spike finding on weak models degrading memory quality).

**Acceptance.** A newcomer can configure a local model from `providers.md` alone,
with a clear steer away from too-small extraction models.

**Difficulty.** Small (docs).
