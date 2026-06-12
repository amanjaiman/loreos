# Security Policy

Lore reads what is on your screen. That makes its security posture the whole
product. The non-negotiables, enforced in CI and review (constitution §4):

- **Local by default, forever.** Every surface binds to `127.0.0.1`. There is
  no Lore-operated server in the data path and no remote listener.
- **Zero telemetry.** The only outbound calls Lore makes are to the model
  endpoints you explicitly configure. The closed egress list lives in
  [docs/privacy.md](docs/privacy.md).
- **No secrets in the repo.** `gitleaks` scans every PR and the full history.
  Your API keys live in Windows Credential Manager, referenced by handle —
  never in config files or logs.
- **Capture is filtered before it is stored or sent.** The sensitivity filter
  chain is held to the project's highest test bar.

## Reporting a vulnerability

Please report suspected vulnerabilities **privately**:

1. Preferred: GitHub → **Security** → **Report a vulnerability** (private
   vulnerability reporting on this repository).
2. Or email **amanjaiman@outlook.com** with `[lore security]` in the subject.

Please include reproduction steps and the commit or release you tested. You can
expect an acknowledgment within **7 days**. Please do not open public issues
for security reports, and allow a fix to land before public disclosure.

## Scope notes

- Reports about the legacy pre-open-source codebase are out of scope; it is
  unpublished and unsupported.
- Vulnerabilities in dependencies (mem0, Electron, FastAPI, …) are best
  reported upstream, but a heads-up here is welcome when Lore's usage is
  affected.
