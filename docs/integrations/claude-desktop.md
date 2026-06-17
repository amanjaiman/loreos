# Claude Desktop

Claude Desktop launches Lore as a local subprocess over **stdio**. This is the
original Lore v1 setup — if you used Lore with Claude Desktop before, your existing
config still works unchanged.

## 1. Open the config file

Edit (creating it if it doesn't exist):

```
%APPDATA%\Claude\claude_desktop_config.json
```

You can also reach it from Claude Desktop: **Settings → Developer → Edit Config**.

## 2. Add Lore

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

If you already have a `mcpServers` block, add the `"lore"` entry alongside your
others. Use the **full path** to `LoreAgent.exe`, with **escaped backslashes**
(`\\`) as JSON requires.

## 3. Restart Claude Desktop

Quit completely (check the system tray) and reopen it so the new config is read.

## 4. Verify

- Click the **tools icon** (the slider/hammer) in the message box. You should see
  **lore** listed with its seven tools.
- Ask Claude something that needs your context, e.g. *"Using my Lore memory, what
  have I been working on?"* — it should call `get_recent` or `get_profile` and
  answer from your memory.

## Troubleshooting

- **"lore" doesn't appear.** The JSON is almost always the culprit: confirm the path
  exists, backslashes are doubled, and the file is valid JSON (no trailing commas).
  Claude Desktop's **Settings → Developer** shows MCP server status and a log link.
- **It appears but every call errors.** Make sure Lore is installed and you finished
  onboarding (a model provider is configured). The `--mcp` process needs that config
  to reach your memory.
- **Need logs.** The stdio server writes diagnostics to **stderr** (stdout is
  reserved for the protocol). Claude Desktop captures these in its MCP logs.
