# Multi-device memory (self-hosted)

> **Stub.** Lore is local-first by default (constitution §1.1): your memory lives on
> your disk and nothing is uploaded. This page sketches the *optional* way to share
> one memory store across several of your machines. The full walkthrough lands with
> spec 011; this is the seam it builds on.

## The idea

By default Lore runs a bundled **memoryd** sidecar per machine (`memory.engine:
"embedded"`). If you want several devices to read and write the *same* memory, you
point each device at **one mem0 server you host yourself** — there is no
Lore-operated server in the path, ever.

Switching is **config only** — no rebuild, no code change:

```jsonc
// config.json
"memory": {
  "engine": "remote",
  "remote_url": "http://your-host:7843"
}
```

With `engine: "remote"`, the agent does **not** spawn a local sidecar; the
`MemorydClient` (the one seam to memory) targets `remote_url` instead, and the
supervisor health-gates that endpoint before first use.

## What you host

A mem0 / memoryd instance reachable from your devices — for example memoryd on a
home server or NAS. Securing that endpoint (it is no longer loopback-only) is your
responsibility: bind it to a trusted network or front it with a tunnel/VPN and
authentication. Lore makes no outbound calls beyond the `remote_url` you configure
and your model provider (see [`privacy.md`](privacy.md)).

## Status

- ✅ `engine: "remote"` switch (spec 002): the client targets `remote_url`; the
  supervisor spawns nothing and gates on the remote `/health`.
- ⏳ Hardening, auth guidance, and a hosting recipe — spec 011.
