---
name: lore
description: Use the user's personal Lore memory so answers reflect what they've done, prefer, and decided. Use when about to give a personalized recommendation or opinion, when the user states a durable preference/decision/fact about themselves, when they ask what you know about them, or when they ask you to forget something. Backed by the local `lore` CLI.
user-invocable: true
---

# Lore — the user's personal memory

[Lore](https://github.com/amanjaiman/loreos) is the user's local, private memory
layer. It captures what they work on and lets them record facts about themselves,
and it serves that memory to you through the `lore` CLI. Use it so your help is
grounded in who *this* user is — their tools, preferences, and past decisions —
instead of generic defaults.

Everything is local (`127.0.0.1`); nothing leaves the machine.

## When to use Lore

- **Before giving a personalized recommendation or opinion** — what editor, library,
  framework, or approach to use; how to set something up. Search first so you build
  on what they already use and prefer.
- **When the user states a durable fact about themselves** — a preference ("I prefer
  X"), a decision ("we went with Y"), a stable detail ("my main project is Z"),
  their setup or constraints. Record it so it's there next time.
- **When the user asks what you know about them**, or to catch up on what they've
  been doing.
- **When the user asks you to forget something.**

## When NOT to use Lore

- **Transient or throwaway details** — a one-off value in this task, scratch state,
  anything not useful later.
- **Secrets** — API keys, passwords, tokens. Never record these. (Lore filters
  secrets, but don't send them in the first place.)
- **Things the user wouldn't want persisted.** If unsure whether a fact is durable
  and wanted, ask before recording.
- Don't call Lore on *every* message — reach for it at the moments above, not as a
  reflex.

## How to use Lore

Drive the `lore` CLI and read its `--json` output. The commands you'll use most:

### Search memory before personalizing

```sh
lore search "<topic>" --json --limit 5
```

Run this before recommending or personalizing. Example: the user asks "what test
framework should I use?" → `lore search "testing framework preferences" --json` →
read the results and tailor your answer to what they already use.

### Record a durable fact

```sh
lore add "<a plain statement about the user>"
```

Phrase it as a clear standalone fact, e.g. `lore add "The user prefers pytest over
unittest"`. You can file it under a category: `lore add "..." --category preferences`.
Lore de-duplicates, so don't worry about near-repeats.

### Forget on request

```sh
lore forget "<topic>"
```

Removes the memory most relevant to the topic. If more remain, run it again.

### Catch up / review

```sh
lore recent --json --limit 10     # the user's most recent memories
lore status --json                # is Lore running and configured?
```

## Reading `--json` output

Every command supports `--json` and prints one envelope:

```json
{ "schema_version": "1.0", "ok": true, "data": { "...": "..." } }
```

- Check `ok`. On success, read `data`; on failure, `data` is absent and `error`
  holds `{ "code", "message" }`.
- `search` → `data.results` (array of memories, most relevant first). Each memory has
  `id`, `memory` (the text), and often `score` and `metadata.category`.
- `recent` / `list` → `data.items` (array of memories) plus `total`, `limit`, `offset`.
- `add` → `data.results` (the memories Lore distilled, each with an `event`).
- `forget` → `data.forgotten` (bool), and `remaining_matches`.

Use the `memory` field of each result as the fact; ignore `id` unless you need to
reference a specific memory.

## When Lore isn't available

`lore` uses exit codes so you can branch:

| Exit | Meaning | What to do |
|---|---|---|
| `0` | Success | Use the output. |
| `2` | Bad usage | Fix the command (check the flag/argument). |
| `3` | Lore isn't running | Proceed **without** memory; optionally tell the user Lore isn't running so they can start it. |
| `4` | Not found | The id/topic didn't match anything. |

If a search fails or returns nothing, just continue with your normal, non-personalized
answer — Lore is an enhancement, never a blocker.
