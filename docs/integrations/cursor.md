# Cursor

Cursor reads MCP servers from a JSON config. Use **stdio** to have Cursor launch
Lore, or **Streamable HTTP** to point it at the running Lore app.

## 1. Open the config file

- **Global** (all projects): `%USERPROFILE%\.cursor\mcp.json`
- **Project-scoped**: `.cursor\mcp.json` in the project root

Or use **Settings → MCP → Add new global MCP server**, which opens the same file.

## 2. Add Lore

**stdio** (Cursor launches Lore):

```json
{
  "mcpServers": {
    "lore": {
      "command": "C:\\Program Files\\Lore\\LoreAgent.exe",
      "args": ["--mcp"]
    }
  }
}
```

**Streamable HTTP** (Lore app already running):

```json
{
  "mcpServers": {
    "lore": {
      "url": "http://127.0.0.1:7842/mcp"
    }
  }
}
```

Use the full, double-escaped path to `LoreAgent.exe` for the stdio form.

## 3. Verify

Open **Settings → MCP**. **lore** should show a **green** indicator and list its
seven tools. In a chat (Agent mode), ask something that needs your context — Cursor
will call a Lore tool and answer from your memory. You can toggle individual tools
on the same settings page.

## Troubleshooting

- **Red indicator / no tools.** Re-check the JSON (valid, doubled backslashes) and
  that the `LoreAgent.exe` path is correct, or that the app is running for the HTTP
  form.
- **Tools listed but calls fail.** Confirm Lore is onboarded with a model provider.
