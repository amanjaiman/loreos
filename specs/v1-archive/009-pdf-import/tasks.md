# 009 — Document Import · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — Port extractor + chunker  `[deps: spec 002]`
Port `PdfExtractor` (PDF → text) and `TextChunker` (sized/overlapped chunks),
cleaned per the constitution.
**Done when:** a sample PDF extracts to text and chunks to the expected sizes;
both are unit-tested, including a low-text/scanned-PDF warning path.

### T002 — Import job store  `[deps: spec 002]`
Implement `ImportJobStore` + `ImportModels` (job id, status, progress, error) backed
by the local store.
**Done when:** jobs can be created, updated, and queried; covered by tests.

### T003 — Import service (extract → filter → chunk → Remember)  `[deps: T001, T002, spec 003]`
Implement `DocumentImportService`: extract → **`SensitivityFilter.Apply`** (reuse
003) → chunk → `IMemoryService.Remember()` with `source`/`document_id` metadata;
update job progress.
**Done when:** importing a sample doc yields searchable, source-tagged memories;
SSN/card content in a document is filtered out (acceptance criteria 1, 3, 4).

### T004 — Import endpoints (fill 005's reserved route)  `[deps: T003, spec 005]`
Implement `POST /import` (start job) and `GET /import/{id}` (status/progress),
replacing 005's `501` placeholder.
**Done when:** a large document imports as a background job with progress reported;
the API contract/OpenAPI is updated (acceptance criterion 2).

### T005 — Error handling + limits  `[deps: T004]`
Handle malformed/unreadable files (clean job failure + actionable message) and
enforce document size limits.
**Done when:** bad files fail the job cleanly with no crash or silent partial loss
(acceptance criterion 5).

---

## Definition of done for spec 009

Documents import through the same filter + memory seam as capture, as background jobs
with progress, tagged for group management; errors are clean. Users can seed their
memory from files. (The app's import UI is 010.)
