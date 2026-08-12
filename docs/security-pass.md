# Pre-launch security pass

> Point-in-time audit of Lore's trust-critical paths before the repository is made
> public (spec 011 T006). It re-reviews the filter chain, the loopback binding, and
> credential storage; diffs the code's actual network egress against
> [privacy.md](privacy.md); and records a full-history secret scan. The living
> guarantees live in [SECURITY.md](../SECURITY.md) and [privacy.md](privacy.md); this
> file is the dated sign-off that those held when the repo went public.

**Scanned:** `origin/main` @ `9222a38` · **Date:** 2026-06-19 · **gitleaks:** 8.30.1

## Verdict

The trust-critical paths are sound and the **publishable history (`origin/main`) is
clean** of secrets. One pre-public action remains and is **not** something the agent
performs — see [Action items](#action-items-human-before-going-public).

## 1. Capture filter chain (spec 003)

`agent/Capture/SensitivityFilter.cs` is one **ordered, fail-closed** chain that every
capture clears before anything analyzes, stores, or transmits it. No downstream code
sees text a layer blocked, and a block carries **no text**.

| Order | Layer | Blocks |
|---|---|---|
| 1 | Blocklist | user's own apps (`process executable`) and keywords (title **and** body) |
| 2 | UIA structural | password fields / secure windows, via `IWindowSecurityProbe` (fails closed) |
| 3 | Regex | SSNs and Luhn-valid payment-card numbers, in title **and** body |

- First match wins and returns a typed `FilterReason`; the document-import variant
  (`ApplyToText`, spec 009) reuses the **same** keyword + regex layers, so the trust
  bar doesn't drop because text came from a file.
- Both the window title and the extracted body are screened, so a sensitive title
  can't ride in on benign body text or vice versa.
- This is, by constitution §6, the project's most heavily tested code.

**Confirmed.** Ordered, fail-closed, no text on a block, symmetric title/body
screening, shared detector across capture and import.

## 2. Loopback-only binding (spec 005)

`agent/Api/ApiHost.cs` binds the local API to `127.0.0.1:7842` and **cannot be widened**:

- `WebHost.UseUrls("http://127.0.0.1:7842")` is hard-coded and takes precedence over
  `ASPNETCORE_URLS` / appsettings, so configuration can't move the bind.
- Defense in depth: `EnsureLoopbackOnly` runs before `Build()` and **refuses to
  start** if any configured URL targets a non-loopback interface. It rejects
  unparseable/wildcard hosts (`http://+:7842`) as not-provably-loopback. It is
  separated out so the guarantee is unit-tested against arbitrary inputs.
- The memoryd sidecar likewise defaults to `127.0.0.1` (`LORE_MEMORYD_HOST`). The
  only way memory leaves the machine is the **user-opt-in** `memory.engine: "remote"`
  path (row 2 of the egress list), which the user configures and secures themselves
  (see [multi-device.md](multi-device.md)).

**Confirmed.** Hard-coded loopback bind plus a fail-fast assertion; no remote listener.

## 3. Credential storage (spec 004)

`agent/Config/WindowsCredentialStore.cs` keeps model API keys in the **Windows
Credential Manager** (generic credentials, DPAPI-encrypted under the user account):

- `config.json` stores only a **handle** (the credential target name); the secret is
  never written to config or to disk in plaintext by Lore.
- Every secret read or written is registered with `SecretRegistry`, which scrubs it
  from logs — so a key can't surface in a log line (constitution §4.2).
- The P/Invoke surface (`CredRead/Write/Delete`) is pinned to `System32`
  (`DefaultDllImportSearchPaths`) to defeat DLL-hijacking (CA5392).

**Confirmed.** Keys at rest in DPAPI, referenced by handle, scrubbed from logs,
hardened P/Invoke.

## 4. Network egress vs. privacy.md

Audited every outbound surface in the codebase against the two-row egress list in
[privacy.md](privacy.md):

- **Agent** — outbound HTTP exists *only* behind the provider backends
  (`HttpInferenceBackend` → Anthropic / OpenAI / Gemini / OpenAI-compatible = the
  configured model endpoint, row 1) and `MemorydClient` + the health probe (the
  memoryd base address: loopback when embedded, `remote_url` when remote = row 2).
- **memoryd** — adds only loopback Ollama (`localhost:11434`) and, via mem0, the same
  configured provider. mem0's PostHog telemetry is force-disabled
  (`MEM0_TELEMETRY=False` in `lore_memoryd/__init__.py`, before mem0 imports), proven
  by `tests/test_telemetry_disabled.py`.
- **Remote-mode nuance** (already documented in privacy.md / multi-device.md): in
  `engine: "remote"`, the agent sends the provider config **including the API key** to
  your `remote_url` so memoryd can extract/embed server-side. That endpoint is yours
  to secure.

**Confirmed.** The egress list is complete and matches the code. No telemetry,
analytics, crash reporting, update check, account, or sync path exists.

> **Superseded on 2026-08-12** (this file is a dated record, so the finding above is
> left as it stood). Lore has since gained an **in-app update check** against a
> project-operated feed — row 3 of [privacy.md](privacy.md). The rest of the finding
> stands: still no telemetry, analytics, crash reporting, account, or sync path. A
> re-run of this audit should treat the egress list as three rows, not two.

## 5. Secret scan over full history (gitleaks 8.30.1)

- **`origin/main`: clean** — `gitleaks detect --log-opts="origin/main"` → *no leaks
  found* (137 commits). The history that will be published carries no secrets.
- **Config posture:** `.gitleaks.toml` runs gitleaks' full default ruleset with **no
  allowlist** (not even "public by design" keys). `.gitleaksignore` suppresses only
  **two synthetic test-fixture fingerprints** in `memoryd/tests/test_routes.py` (fake
  values like `sk-proj-abc123XYZ`, used to test log redaction — not real secrets).
- **One repo-wide finding, by design and off the default branch:** the branch
  `test/ci-probes` (commit `05e8593`, *"TEMP: stronger CI probes — do not merge"*)
  contains a **synthetic fake `ghp_` token** in `ci_probe_secret.txt`. It is a
  deliberate probe that the secret scanner catches real-looking tokens; it is **not
  an ancestor of `main`**, which is why CI (a fresh checkout of the PR's reachable
  history) is green. It is a fake value, not a real credential — but it should not
  ship in the public repo (see action items).

## Residual risks / out of scope

- **Installer code-signing is build-ready but not yet applied** (no certificate
  provisioned — spec 011 T002). Until signed, SmartScreen/AV false positives are more
  likely; this is a distribution-trust gap, not a code-trust one.
- **Remote memoryd has no built-in authentication.** Securing a self-hosted endpoint
  (VPN/tunnel/TLS proxy) is the operator's responsibility (multi-device.md).
- **Screen capture is best-effort by nature;** the filter chain (§1) is the
  mitigation and is held to the highest test bar.

## Action items (human, before going public)

1. **Delete the `test/ci-probes` remote branch.** It exists only to exercise the
   secret scanner and carries a synthetic token; there is no reason to publish it.
   After deletion, a full-repo gitleaks scan is clean everywhere, not just on `main`.
2. **Read and sign off this document.** It is the artifact standing behind "safe to
   watch your screen." Re-run the `origin/main` scan if `main` has advanced since
   `9222a38`.
3. **Then** make the repository public and enable branch protection requiring the four
   CI checks (`csharp`, `python`, `app`, `secrets`) — completing the spec 001 / T009
   deferral. (GitHub blocks branch protection on free private repos, so it can only be
   done after the public flip.)

> These three steps are intentionally **manual**: publishing screen-watching software
> is a one-way, high-stakes decision and must be a human's, taken only after reading
> this sign-off.
