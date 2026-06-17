# Claude Code

Claude Code (the CLI) supports both transports. **stdio** is the simplest; use
**Streamable HTTP** if you'd rather connect to the already-running Lore app.

## Option A — stdio (recommended)

Add Lore with `claude mcp add`. Everything after `--` is the command Claude Code
will launch:

```sh
claude mcp add lore -- "C:\Program Files\Lore\LoreAgent.exe" --mcp
```

By default this adds it for the current project (`local` scope). To make Lore
available in every project, add `--scope user`:

```sh
claude mcp add lore --scope user -- "C:\Program Files\Lore\LoreAgent.exe" --mcp
```

## Option B — Streamable HTTP

With the Lore app running (so the local API is up on `127.0.0.1:7842`):

```sh
claude mcp add --transport http lore http://127.0.0.1:7842/mcp
```

## Verify

```sh
claude mcp list
```

`lore` should show as **connected**. Inside a session, run `/mcp` to see the server
and its seven tools, then ask something that needs your context — Claude Code will
call `get_context` / `get_recent` and answer from your memory.

## Troubleshooting

- **`failed to connect` (stdio).** Check the path to `LoreAgent.exe` and that Lore
  is installed and onboarded (a model provider configured).
- **`failed to connect` (HTTP).** The Lore app must be running; confirm
  `http://127.0.0.1:7842/health` returns `{"status":"ok"}`.
- **Remove or re-add.** `claude mcp remove lore`, then add again.
