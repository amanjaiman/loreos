# Lore v2 — Spec Set

The v2 restart reshapes Lore from an **activity log** into a **memory layer**:
durable, typed, first-person facts about the user, captured skeptically and
surfaced to any AI agent on every message. Bound by
[`constitution.md`](../../constitution.md) (unamended). Specs 001–013 describe the
retired v1 shape and are archived per v2-002.

| Spec | What | Status |
|---|---|---|
| [v2-001 — Memory Model & Capture](001-memory-and-capture/specification.md) | The product core: typed memories with lifecycle, episode-based skeptical capture, the every-turn recall contract, app-facing API resources. | specification ✓ · [spike GO](001-memory-and-capture/spike-findings.md) (raw-store mode) · [plan](001-memory-and-capture/plan.md) ✓ · [tasks](001-memory-and-capture/tasks.md) all tasks T001–T010 done |
| [v2-002 — Architecture Reset](002-architecture-reset/specification.md) | What survives v1 / what's retired, spec-tree archive, repo mechanics, docs rewrite. | implemented (shipped with the v2-001 chain + follow-up PRs) |
| [v2-003 — App Redesign](003-app-redesign/specification.md) | Sidebar-free single-column app on the Lore Design System (Coastal/Nocturne), five screens, vendored assets, zero egress. | implemented (shipped with the v2-001 chain + follow-up PRs) |
| [v2-004 — Shell Redesign](004-shell-redesign/specification.md) | Frameless window on a ground plane, left rail carrying ambient presence, Home as a two-column app pane, Coastal Dark replacing Nocturne. | implemented (T001–T009 on `feat/v2-004-shell-redesign`) |
| [v2-005 — App Read API](005-app-read-api/specification.md) | Additive read endpoints the v2-004 shell needs: memory counts, the live capture target, memory evidence. | implemented (agent + renderer, same branch) |

**Implementation order:** 001's tasks in order (T002 memoryd raw mode first), 002
interleaved as the deletions become safe (old capture path deletes only after
001's flag defaults to v2), 003 in parallel once 001's API resources (T009) are
pinned. Each spec follows SDD: `specification.md` (what/why)
→ `plan.md` (how) → `tasks.md` (PR-sized steps), every task landing through the
`no-mistakes` gate.
