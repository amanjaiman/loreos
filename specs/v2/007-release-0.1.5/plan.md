# v2-007 — Release 0.1.5 polish · Plan

## Decisions

- Set Windows window app details as well as the existing executable/window icon and AUMID;
  these cover distinct Windows icon-resolution paths.
- Hold only live capture enable/blocklist values in an atomically replaced snapshot. Timing
  and lifecycle options remain process-lifetime configuration.
- Save chip actions immediately through the existing local API; no second persistence seam.
- Make `lore connect` the primary command. It copies the portable skill to
  `~/.agents/skills/lore` and `~/.claude/skills/lore`, then merges Claude Desktop MCP only
  when detected (or with `--all`). The Electron button invokes that packaged CLI without a shell.
- Preserve legacy installers as advanced compatibility surfaces.

## Verification

- Agent and CLI unit tests cover live settings and connection idempotence.
- App lint, typecheck, and package build cover renderer/main integration.
- An installed Windows smoke check remains the acceptance test for taskbar cache behavior.
