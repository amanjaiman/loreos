# v2-002 — Architecture Reset · Specification

> SDD artifact: **what & why.** Bound by [`constitution.md`](../../../constitution.md).
> Defines what survives from v1 (specs 001–013), what is retired, and the repo
> mechanics of the restart. **Depends on:** v2-001 (defines the new memory/capture
> shape that decides each component's fate).

## Overview

The v2 restart is a product reshape, not a rewrite-for-its-own-sake. v1 got three
expensive things right — the BYO-provider layer, the trust infrastructure (filter
chain, keystore, gitleaks, privacy docs), and the packaging/distribution pipeline —
and got two things wrong: the capture/memory shape (fixed by v2-001) and the app
(replaced by v2-003). This spec makes the keep/retire line explicit so the restart
deletes with intent instead of entropy, and resets the spec tree so v2 is the only
active plan of record.

The constitution survives unamended: stack (§2), seams (§3), privacy rules (§4),
and the delivery gate (§7) all continue to bind v2 work.

## Component disposition

### Survives as-is (re-justified, not rewritten)

- **Provider layer** (`agent/Providers`, `agent/Inference`, spec 004): BYO key
  (Anthropic / OpenAI / Gemini) or BYO URL, keystore-backed secrets, connection
  test. v2-001's distiller and embeddings ride these seams unchanged.
- **Sensitivity filter chain + extractors** (`Blocklist`, `SensitivityFilter`,
  `SensitivePatterns`, UIA/OCR extractors and their seams): trust-critical,
  exhaustively tested, unchanged. Their test tables move forward verbatim.
- **memoryd supervision & seam** (`agent/Hosting`, `agent/Memory`,
  `IMemoryService`/`MemorydClient`, spec 002): the process model stands; the
  payload schema is extended per v2-001.
- **CLI** (spec 007), **MCP host plumbing** (spec 006), and the **agent skill**
  (spec 008): thin clients of the local API; they gain the recall surface and
  re-worded memory-layer descriptions (v2-001 owns that copy) but keep their
  architecture.
- **Packaging/distribution** (specs 011/012): installer, winget, signing, PyInstaller
  bundling — untouched except for what the new app build emits.
- **Repo trust scaffolding**: LICENSE, SECURITY.md, CONTRIBUTING.md, gitleaks
  config, CI, `no-mistakes` gate.

### Rebuilt (v2-001 / v2-003 own the shape)

- **Capture decision layer**: `SmartGate`, dwell-triggered analysis,
  `AnalysisPrompt`, `ObservationParser` → replaced by episode segmentation,
  skeptical distillation, staging/promotion (v2-001).
- **Memory schema & memoryd behavior**: observation-shaped `Remember()` → typed
  facts with kind/horizon/confidence/lifecycle (v2-001).
- **The Electron app**: replaced wholesale (v2-003). No renderer code is ported;
  the main-process agent-supervision code may be transcribed with judgment.
- **REST API surface** (spec 005): endpoints survive where their resource survives;
  memory and capture endpoints re-shape around memories/episodes/staging/recall.

### Retired

- **PDF import** (spec 009, `agent/Import`): an activity-log-era feature; out of
  v2 launch scope. Code is deleted (git history keeps it); a future spec can
  reintroduce imports as evidence sources for the v2 model.
- **v1 memory data as live data**: handled per v2-001's migration criteria
  (distill or archive read-only — never silent deletion).

## Spec-tree reset

- `specs/001-…013-…` move to `specs/v1-archive/` with a one-paragraph README
  stating they describe the retired shape and are kept for provenance.
- `specs/v2/*` becomes the active plan of record; `AGENTS.md` and the README point
  agents at v2 specs only.
- v1 specs whose subject survives (004, 006, 007, 011, 012) get a pointer line in
  the archive README mapping them to their v2 status ("survives; see v2-002"), so
  the archive is navigable, not misleading.

## Repo mechanics

- Work proceeds on feature branches per the delivery gate; **no orphan branch, no
  force-push, no history rewrite** — v2 is expressed as ordinary commits that
  delete retired code and add new code. History is the provenance trail.
- The default branch is shared with other agents; reset work is sliced into
  reviewable PRs (spec archive · retire capture decision layer · retire app · docs
  rewrite), not one mega-commit.
- `README.md`, `docs/privacy.md`, and `docs/integrations/` are rewritten to tell
  the v2 story (memory layer, skeptical capture, ambient recall) — the README's
  quickstart promise ("under ten minutes") is retained and re-verified.
- Nothing is ever copied from the legacy `lore/` repo wholesale (constitution
  §4.1); any v1 code that moves is transcribed with judgment from `loreos` itself.

## User stories

- **As a contributor**, the specs directory tells me exactly one coherent story;
  I cannot accidentally implement a retired v1 behavior.
- **As a user updating from v1**, my provider config and API keys keep working,
  my old memories are not silently destroyed, and the privacy guarantees I
  installed under did not weaken.
- **As the maintainer**, every deletion is attributable to a disposition line in
  this spec — nothing disappears without a stated reason.

## Acceptance criteria

1. `specs/` contains only `v2/` (active) and `v1-archive/` (with mapping README);
   `AGENTS.md`/`README.md` reference v2 specs only.
2. The retired code paths (SmartGate/dwell analysis, PDF import, old renderer) are
   gone from the tree; the surviving components build and their existing test
   suites pass unchanged (filter-chain table verbatim).
3. `docs/privacy.md`'s egress list is re-verified against the v2 tree: user-configured
   model endpoints remain the only outbound calls (the v2-003 app adds none).
4. A v1 user's `config.json` + keystore entries load in v2 without re-entry of keys
   (config migration is additive; unknown v1 keys are ignored with a logged note).
5. Every PR in the reset lands through the `no-mistakes` gate on feature branches.

## Risks

- *The reset stalls the repo in a half-old/half-new state.* Mitigation: the PR
  slicing above is ordered so the tree builds and tests green after every merge;
  archive-first, delete-second, rebuild-third.
- *Deleting the capture decision layer breaks surviving consumers.* Mitigation:
  v2-001's episode pipeline lands behind the same `IMemoryService` seam before the
  old path is removed; the agent never ships without a capture path.
- *Docs drift from the new story.* Mitigation: docs rewrite is an explicit PR in
  the slicing with its own acceptance criterion (AC 3), not a follow-up intention.
