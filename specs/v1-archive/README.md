# v1 spec archive

These specs (001–013) describe Lore's **retired v1 shape** — the activity-log
pipeline (dwell → smart gate → per-window analysis → observation store) and the
sidebar app. They are kept for provenance only; **the active plan of record is
[`specs/v2/`](../v2/README.md)** (see [v2-002](../v2/002-architecture-reset/specification.md)
for the reset that archived them).

Where a v1 spec's subject survived the reset, this maps it to its v2 status:

| v1 spec | Status after the v2 reset |
|---|---|
| 001-foundation | Survives (repo scaffolding, host bootstrap). |
| 002-memory-service | Survives, thinned: memoryd runs mem0 as a **raw store** (v2-001 T002); its Option-C reconciler is deleted. |
| 003-capture-pipeline | **Retired decision layer** (smart gate, per-window analysis). The trust-critical filter chain, extractors, monitor, and activity store survive inside the v2 pipeline. |
| 004-provider-layer | Survives unchanged (BYO key/URL, keystore, test-connection). |
| 005-local-api | Survives; memory/capture endpoints reshaped by v2-001 (recall, staging, episodes, decisions, economy). |
| 006-mcp-server | Survives; tool surface reworded + `recall` added (7 → 8 tools). |
| 007-cli | Survives; `lore recall` added. |
| 008-agent-skill | Survives; copy updated for the memory-layer story. |
| 009-pdf-import | **Retired** (deleted; a future spec may reintroduce imports as episode-shaped evidence). |
| 010-desktop-app | **Retired renderer** — replaced wholesale by v2-003 (no sidebar, Today/Memory/Activity/Settings). |
| 011-packaging / 012-distribution | Survive unchanged. |
| 013-first-run-reliability | Survives (embedder policy, first-run fixes). |
