# The memory model

Lore is a **memory layer**, not an activity log. It keeps a small, curated set of
durable, first-person facts about you — and surfaces them to any connected AI tool
at the moment they're relevant, the way ChatGPT or Claude memory works. Mention
tooth pain this week and ask an assistant about takeout next week: the assistant
gets *"I'm recovering from a wisdom tooth extraction"* as context and steers you
toward soft foods. Ask for trip ideas and it already knows you've been to France.

Full intent and acceptance criteria: [`specs/v2/001-memory-and-capture/`](../specs/v2/001-memory-and-capture/specification.md).

## Memories are typed facts

Every memory is one first-person statement plus metadata:

| Kind | Meaning | Lifespan |
|---|---|---|
| `identity` | stable traits (job, family, home city) | long-lived |
| `preference` | tastes and working styles | long-lived, revisable |
| `state` | temporary conditions (recovering from surgery, job hunting) | **expires** on a horizon; stops surfacing automatically |
| `experience` | past events (visited France, shipped a launch) | permanent, gently recency-weighted |
| `project` | ongoing endeavors | active until revised |

Plus: a **status** (`staged` → `active` → `archived`), a confidence score,
provenance (which episodes support it), and user-authority flags (`pinned`,
`user_edited`) — anything you edit or pin is never auto-changed by capture again;
contradictions surface as a confirmation instead.

## Capture is skeptical

The old model asked "what is the user doing?" every few seconds and stored the
answer. The v2 pipeline watches for **episodes** — contiguous stretches of related
activity, segmented with cheap local math and zero model calls — and asks your
model one question per closed episode: *"what durable fact about the user does
this support?"*, where **"nothing" is the expected answer**. Routine reading,
news, ordinary coding: no memory.

A candidate fact is not immediately a memory. It **stages** first and needs a
second supporting episode to promote — one misread page never becomes a "fact"
about you. A committed action (a booking confirmation) promotes directly.
Promotions are capped (default **10/day**; at the cap candidates defer, never
drop), which bounds both noise and inference spend.

When a new fact contradicts an old one ("mouth has healed" vs. "recovering from
extraction"), a one-word arbitration call decides: the old memory is **archived,
not deleted**, with a link from its replacement.

## Recall is ambient

Clients (MCP's `recall` tool, `lore recall`, `POST /recall`) check memory on
**every message**. The path is one filtered vector search plus scoring math — no
LLM — so it's fast enough to run per turn, and it returns *empty* rather than
padding with weak matches. Expired `state` never surfaces; staged and archived
rows are invisible; old experiences fade gently but never vanish.

## Everything is auditable

Every episode and every decision (staged / promoted / deferred / revised /
nothing-durable-here) is written to a local decision trail (`GET /decisions`,
`GET /episodes`), so *"why does — or doesn't — Lore know X?"* always has an
answer. Memories you disagree with are yours to edit, pin, dismiss, or delete;
your word always outranks the pipeline's.
