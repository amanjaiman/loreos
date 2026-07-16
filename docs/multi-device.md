# Multi-device memory (self-hosted)

> Lore is local-first by default (constitution §1.1): your memory lives on your disk
> and nothing is uploaded. This page is the *optional* way to share one memory store
> across several of your machines — by pointing each at **one mem0/memoryd server you
> host yourself.** There is no Lore-operated server in the path, ever.

## The model

By default each machine runs its own bundled **memoryd** sidecar
(`memory.engine: "embedded"`) against a local on-disk store. To share memory, you run
**one** memoryd somewhere your devices can reach (a home server, a NAS, a small VM)
and set every device to `memory.engine: "remote"` pointing at it. All devices read and
write the same store, so a memory captured on your laptop surfaces on your desktop.

```
   desktop ─┐
            ├──→  one memoryd you host  ──→  on-disk Qdrant (the shared store)
   laptop  ─┘        (remote_url)
```

Memories are keyed by a fixed `user_id` (`"default"`), so no per-device identity
setup is needed — point devices at the same server and the store is shared.

## 1. Stand up the server

On the host, install and run memoryd bound to a reachable address with a
host-controlled data directory:

```sh
# Python 3.11+ on the host
pip install ./memoryd            # or: pip install lore-memoryd  (from your build)

# Bind to the network (not just loopback) and pin where the shared store lives.
# LORE_MEMORYD_DATA_DIR is host-controlled on purpose: connecting devices each send
# their own local data_dir, and this override ensures none of them can repoint your
# store (spec 011 T005).
export LORE_MEMORYD_HOST=0.0.0.0
export LORE_MEMORYD_PORT=7843
export LORE_MEMORYD_DATA_DIR=/srv/lore        # persists Qdrant + history.db here
python -m lore_memoryd
```

You can also run the packaged `lore-memoryd.exe` (from an install's
`resources/native/memoryd/`) with the same environment variables instead of a Python
install.

**The host needs to reach a model.** Embeddings run *on the server*, using the
provider each device pushes (see step 2). So the host must be able to reach
that provider — the cloud API over the internet, or, for a local model, an Ollama the
host runs or can reach (`OLLAMA_HOST` / the provider `base_url`).

### Secure the endpoint (required)

Once bound to `0.0.0.0`, memoryd is **no longer loopback-only**, and it has no
built-in auth. Do **not** expose it to the open internet. Put it on a trusted network
and front it with one of:

- a **VPN / private network** (Tailscale, WireGuard) so only your devices can reach it, or
- an **SSH tunnel** from each device (`ssh -L 7843:localhost:7843 host`), keeping
  `remote_url` at `http://127.0.0.1:7843`, or
- a **reverse proxy with TLS + auth** in front of port 7843.

This matters because the agent sends your **provider config, including the model API
key**, to the server so it can embed (see [`privacy.md`](privacy.md)). Treat
the link as carrying a secret.

## 2. Point each device at it

On every device, edit `config.json` (in `%LocalAppData%\Lore\`):

```jsonc
"memory": {
  "engine": "remote",
  "remote_url": "http://your-host:7843"   // or http://127.0.0.1:7843 over an SSH tunnel
}
```

With `engine: "remote"` the agent spawns **no** local sidecar; the `MemorydClient`
(the one seam to memory) targets `remote_url`, the supervisor health-gates that
endpoint before first use, and the agent applies your provider to it via `POST /config`.
Restart the agent (or the app) to pick up the change.

Use the **same provider** on every device so the server isn't reconfigured back and
forth between models.

## 3. Verify cross-device recall

1. On device A, capture or add a memory (use the app, or `lore add "<fact>"`).
2. On device B, search for it: `lore search "<fact>"` — it should return the memory
   stored from device A.
3. Confirm the server is the single store: stop memoryd on the host and both devices
   show the calm "Lore isn't running"/offline state for memory; restart it and recall
   resumes.

## Reverting

Set `memory.engine` back to `"embedded"` (or remove the `memory` block) on a device to
return it to its own local store. The remote store is unaffected.

## Status

- ✅ `engine: "remote"` switch (spec 002): the client targets `remote_url`; the
  supervisor spawns nothing and gates on the remote `/health`.
- ✅ Host-controlled `LORE_MEMORYD_DATA_DIR` (spec 011): the operator, not the
  connecting devices, owns where the shared store lives.
- ⚠️ memoryd has no built-in authentication; securing the endpoint (VPN/tunnel/proxy)
  is yours to do. Optional memoryd auth is a roadmap item.
