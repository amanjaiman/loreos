# Generic Streamable HTTP clients

Any client that speaks MCP over **Streamable HTTP** can connect to Lore at a single
loopback URL. Use this when your tool isn't covered by the other guides but supports
an HTTP MCP endpoint.

## Endpoint

```
http://127.0.0.1:7842/mcp
```

- **Transport:** Streamable HTTP (the MCP HTTP transport; it also negotiates SSE for
  streamed responses).
- **Auth:** none. There's nothing to authenticate because there's nothing remote —
  the endpoint binds **loopback only** and refuses any non-loopback interface
  (constitution §3.4).
- **Prerequisite:** the Lore app (or agent) must be **running**, so the local API is
  up. Check with `http://127.0.0.1:7842/health` → `{"status":"ok"}`.

## Typical client config

Most HTTP-capable clients accept a `url` entry:

```json
{
  "mcpServers": {
    "lore": {
      "url": "http://127.0.0.1:7842/mcp"
    }
  }
}
```

## Verify by hand

An MCP session begins with an `initialize` call. For example:

```sh
curl -sS http://127.0.0.1:7842/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"0"}}}'
```

A successful response identifies the `lore` server and its protocol version; a
follow-up `tools/list` returns the seven tools. (Most users let their MCP client
drive this handshake rather than calling it directly.)

## Coming from OpenMemory MCP?

Lore keeps its own (v1) tool names for backward compatibility rather than adopting
OpenMemory's. If you're used to OpenMemory MCP, here's the rough correspondence:

| OpenMemory MCP | Closest Lore tool |
|---|---|
| `add_memories` | `add_context` |
| `search_memory` | `get_context` |
| `list_memories` | `get_recent` / `get_profile` |
| `delete_all_memories` | *(no bulk delete; `forget(topic)` removes one at a time)* |

Lore also adds `summarize_profile` and `update_context`, which have no direct
OpenMemory equivalent. The names differ, but the intent — a personal memory layer
your assistant can read and write — is the same.
