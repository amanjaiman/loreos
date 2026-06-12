# 009 — Document Import · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Port v1's `Import/` pipeline, cleaned, and re-point its tail from v1 storage to
`IMemoryService`. Run imports as background jobs tracked in a small store, exposed
through the `/import` route 005 reserved. Reuse the capture spec's sensitivity
filtering on document text.

## Structure

```
agent/Import/
├── PdfExtractor.cs         # PDF → text (ported)
├── DocumentImportService.cs# orchestrates extract → filter → chunk → Remember
├── TextChunker.cs          # sized/overlapped chunking (ported)
├── ImportJobStore.cs       # job status/progress (local)
└── ImportModels.cs         # job + request/result types
agent/Api/Endpoints/
└── ImportEndpoints.cs      # POST /import (start), GET /import/{id} (status)  — fills 005's reserved route
```

## Flow

```
POST /import (file/path) → create job → background:
    text   = PdfExtractor / text reader
    safe   = SensitivityFilter.Apply(text)         # reuse 003's chain
    chunks = TextChunker.Split(safe)
    for chunk: IMemoryService.Remember(chunk, metadata{source=doc, doc_id})
    update job progress; on error → job failed + message
GET /import/{id} → status/progress
```

## Metadata & grouping

Each imported memory carries `source` + `document_id` metadata so imports are
attributable, listable, and forgettable as a unit (a future "forget this document"
is then trivial). Categories may also be inferred as in capture.

## Filtering

Document text passes the **same** `SensitivityFilter` chain (003) before chunking/
storage — a document is just another text source, and the trust bar doesn't drop
because the text came from a file.

## Decisions

- **Reuse capture's filter + the memory seam** — imports and captures share the
  trust-critical path and the single storage door; no parallel logic.
- **Background jobs** — large PDFs must not block the loop or API.
- **Document-scoped metadata** — makes imports manageable and reversible.
- **Deferrable** — nothing in 001–008/011 depends on this; it can ship after launch.

## Dependencies & order

Upstream: **002** (`IMemoryService`), **003** (`SensitivityFilter`), **005**
(reserved `/import` route). Internal order: extractor + chunker → job store →
import service (extract→filter→chunk→Remember) → endpoints filling the reserved
route → error handling + metadata. See [`tasks.md`](tasks.md).
