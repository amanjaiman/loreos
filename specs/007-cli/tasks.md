# 007 — `lore` CLI · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — CLI root + API client + output layer  `[deps: spec 005]`
Grow `cli/` into a System.CommandLine app: root command, global `--json`/`--api-url`,
`ApiClient` (loopback HTTP), and `Output` (human vs JSON, secret-scrubbed).
**Done when:** `lore --help` works; `ApiClient` talks to the local API; output layer
renders both modes; covered by tests.

### T002 — Read commands + `--json` envelope  `[deps: T001]`
Implement `search`, `recent`, `get`, `list` with the versioned `{schema_version, ok,
data, error}` envelope under `--json`.
**Done when:** each read command works against a seeded store in both modes; the
`--json` schema is covered by a contract test (acceptance criterion 2).

### T003 — Write commands  `[deps: T001]`
Implement `add`, `forget` over the local API.
**Done when:** add/forget round-trip through the API and reflect in subsequent reads.

### T004 — status · config · export  `[deps: T001]`
Implement `status` (agent/memoryd/provider health), `config get/set` (secrets routed
to 004's store, never printed), and `export --format json|markdown`.
**Done when:** status reflects real health; config set stores secrets via the
keystore and never echoes them; export matches the API's export.

### T005 — Exit codes + agent-not-running UX  `[deps: T002, T003, T004]`
Implement the documented exit-code scheme and the "Lore isn't running" path
(connection-refused → exit 3, clear message, no stack trace).
**Done when:** each failure class returns its documented exit code; agent-down is
friendly; all asserted in tests (acceptance criteria 3, 6).

### T006 — `lore mcp install`  `[deps: T001]`
Implement `mcp install <client>`: locate + back up + **merge** a `lore` MCP entry
into the client config; idempotent.
**Done when:** installing into a sample Claude Desktop config adds a correct entry,
is idempotent, and never clobbers existing entries (acceptance criterion 4).

### T007 — `lore skills install` + CLI docs  `[deps: T001, spec 008]`
Implement `skills install <client>` (copy 008's `skills/lore/` into the client's
skills dir, idempotent). Write `docs/integrations/cli.md` (commands, `--json`
schema, exit codes).
**Done when:** the skill installs to the right location; docs match behavior
(acceptance criterion 5).

---

## Definition of done for spec 007

Every command works as a thin shim over the local API; `--json` + exit codes form a
stable machine contract; installers safely wire Lore into other tools; agent-down is
handled gracefully. Coding agents and scripts can now use Lore without MCP.
