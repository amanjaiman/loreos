---
name: write-changelog
description: Write concise, human-readable Lore release notes from commits, diffs, issues, or pull requests. Use when preparing a release, updating release-notes.md, drafting a changelog, or writing a release PR title that users may see in the in-app update dialog.
---

# Write Changelog

Turn implementation history into notes a Lore user can scan before choosing whether to
restart for an update. Write for the person using the product, not the people who built it.

## Workflow

1. Identify the comparison range. For a release, compare the previous release tag with the
   proposed release commit and inspect the merged pull requests when available.
2. Keep only user-visible changes: new capabilities, meaningful behavior changes, important
   fixes, and required user actions. Omit refactors, tests, dependency housekeeping, CI work,
   internal filenames, and release mechanics unless they materially affect users.
3. Group related implementation changes into one outcome. Describe what is better and why it
   matters; do not narrate how the code works.
4. Write or replace the repository-root `release-notes.md`. The release workflow publishes
   that file verbatim, and Lore displays it in the update dialog.
5. Re-read every bullet as a user deciding whether to update. Remove anything vague,
   repetitive, promotional, or useful only to maintainers.

## Format

Use this shape unless the release needs an explicit warning:

```markdown
## What's new

- **Short outcome.** One plain-language sentence explaining the visible benefit.
- **Another outcome.** One plain-language sentence explaining the visible benefit.
```

- Write 1-5 bullets and normally stay below 100 words total.
- Put the most valuable change first.
- Use sentence case and active voice.
- Keep each bullet understandable without an issue, commit, or pull-request link.
- Add `## Before you update` above `## What's new` only for breaking changes, migrations, or
  actions the user must take.
- Do not repeat the version number; the release page and update dialog already supply it.
- Do not add contributor lists, full-compare links, commit prefixes, or generic claims such as
  "various improvements" and "bug fixes."

## Release PR title

Make the release pull-request title user-facing too. Summarize the primary outcome without a
`chore(release):` prefix, because GitHub and downstream tooling may expose that title.
