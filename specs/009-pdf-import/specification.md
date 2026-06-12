# 009 — Document Import · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 002
> (`IMemoryService`), 005 (reserves `POST /import`). **Deferrable** — see note.

> **Scope note:** Document import is a high-value but non-blocking feature. If
> launch timing is tight, this spec can be deferred without affecting 001–008/011.
> It is included now so the seam (the reserved `/import` route in 005) has an owner.

## Overview

Document import lets a user **seed their memory from files** — "import this PDF of my
travel itinerary / my résumé / my research notes" — instead of waiting for ambient
capture to learn it. It ports v1's import pipeline (PDF text extraction → chunking →
storage) and re-points the storage tail at `IMemoryService`, so imported content
flows through the same mem0 extraction as captured content. Long imports run as
background jobs with progress.

## User stories

- **As a new user**, I can import a few documents on day one so Lore already knows
  useful things about me before it has watched me work.
- **As any user**, I can drop in a PDF (and other supported text documents) and have
  the relevant facts become searchable memories.
- **As a user with a big document**, the import runs in the background with progress,
  not a frozen UI.

## Scope

### In scope

- **Extraction**: PDF text extraction (`PdfExtractor`) and plain-text/markdown
  documents; the same sensitivity filtering principles as capture apply before
  storage.
- **Chunking**: `TextChunker` splits documents into sized, overlapped chunks suited
  to mem0 extraction.
- **Storage tail**: chunks flow to `IMemoryService.Remember()` with
  source/document metadata, so imports are attributable and forgettable as a unit.
- **Background jobs**: an import job store (`ImportJobStore`) with status/progress;
  `POST /import` (filling 005's reserved route) starts a job, a status route reports
  progress.
- **Filtering**: imported text passes the same sensitivity checks (no storing SSNs/
  cards/secrets from a document).

### Out of scope

- The app's import UI (010 — this spec provides the endpoints/jobs it drives).
- Non-document sources (web pages, email) — future.
- mem0 internals (002).

## Acceptance criteria

1. Importing a sample PDF produces searchable memories whose content matches the
   document and whose metadata records the source document.
2. A large document imports as a background job; `GET /import/{id}` reports progress
   and final status.
3. Sensitive patterns present in a document (SSN/card) are filtered out and not
   stored.
4. Imported memories carry document/source metadata so they can be listed and
   forgotten as a group.
5. Malformed/unreadable files fail the job cleanly with an actionable error — no
   crash, no partial silent loss.

## Non-functional requirements

- **Same seam**: storage only via `IMemoryService`; no direct mem0 access
  (constitution §3.2).
- **Same trust bar on content**: document text is filtered before storage, like
  capture (constitution §4).
- **Responsive**: imports never block the agent loop or the API; they run as jobs.

## Risks

- *PDF extraction quality varies.* Mitigation: port v1's handling; surface
  low-text-extraction warnings.
- *Large imports flood memory with low-value chunks.* Mitigation: sized/overlapped
  chunking + mem0's own extraction/dedup; document size limits.
