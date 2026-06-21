# 012 — Package-Manager Distribution & Updates · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 011 (the
> installer + signing wiring) and the launch step that publishes GitHub Releases.

## Overview

Make Lore **updatable through the platform's native package manager** so an installed
user runs **one command** to upgrade — `winget upgrade Lore` on Windows today, `brew
upgrade lore` on macOS later — instead of rebuilding the installer by hand.

Critically, this is the **privacy-preserving** way to do updates: Lore itself never
checks for or downloads updates, so the headline promise — *zero non-loopback calls
when you're local* — stays exactly true. The package manager the user already trusts
does the checking, only when the user asks. There is **no in-app update check, banner,
or "check for updates" button** — that was considered and deliberately dropped (see
[Out of scope](#out-of-scope)).

Delivering this requires three things that don't exist yet: a **single source of truth
for the product version**, **automated GitHub Release publication** of the signed
installer on a version tag, and a **maintained winget manifest** submitted to the
community `winget-pkgs` repository.

## User stories

- **As an installed user**, I run `winget upgrade Lore` and get the latest version,
  with no manual installer rebuild and no developer tools.
- **As a new user**, I can `winget install Lore` instead of downloading and clicking
  through an unsigned `.exe`.
- **As a privacy-minded user**, updating costs **zero background phone-home** from
  Lore: the app never reaches out to check versions; my package manager does, when I
  choose to run it.
- **As a maintainer**, tagging a release (`vX.Y.Z`) automatically builds the signed
  installer, publishes a GitHub Release with the artifacts attached, and opens (or
  updates) the winget manifest PR — so cutting a release is one tag, not a checklist.
- **As a future macOS user**, the same model applies through Homebrew, so the
  cross-platform update story is "use your platform's package manager," not a bespoke
  updater per OS.

## Scope

### In scope

- **Single-source product version.** One authoritative version drives the agent
  assembly version (`/system/status` `version`), the app (`app/package.json` →
  installer artifact name), and the release tag. Today these disagree
  (`app/package.json` = `0.1.0`, agent defaults to `1.0.0`); they must agree, because
  winget keys upgrade detection on a single coherent version.
- **Automated GitHub Release on tag.** On a `vX.Y.Z` tag, CI builds the signed
  installer (reusing 011's `installer.yml` pipeline) and publishes a GitHub **Release**
  with `LoreSetup.exe`, the `.nupkg`, and `RELEASES` attached. This also satisfies the
  launch-checklist "publish a GitHub Release" item.
- **winget manifest.** The three-file winget manifest (version / installer /
  defaultLocale) for the released version, validated locally (Windows Sandbox /
  `winget validate`), and submitted as a PR to **microsoft/winget-pkgs**. Upgrade
  detection must work: the manifest's `AppsAndFeaturesEntries` (ProductCode /
  DisplayVersion / scope) must match what the Squirrel installer writes to the registry
  so `winget upgrade` recognizes an older install.
- **Per-release manifest automation.** A release step (e.g. `wingetcreate`/`komac`)
  that regenerates and submits the updated manifest for each new version, so the manual
  authoring is a one-time cost.
- **Docs.** README documents `winget install` / `winget upgrade`; `docs/privacy.md`
  gets an explicit line that updates flow through the package manager and Lore makes
  **no** update call (reinforcing the existing "No update check" guarantee, *not*
  adding an egress row); `docs/roadmap.md` notes Homebrew/macOS as the next channel;
  the launch checklist points at the automated Release flow.

### Out of scope

- **Any in-app update mechanism** — no version-check call, no notification banner, no
  "Check for updates" button, no Squirrel/electron `autoUpdater`. Explicitly dropped to
  keep the zero-egress promise; the package manager owns updates entirely.
- **macOS Homebrew implementation** — roadmap. This spec establishes the
  package-manager pattern so Homebrew is a parallel follow-on, not a redesign.
- **A hosted update server or auto-download.** Updates are user-pulled via the package
  manager; Lore operates no update infrastructure.
- **Changing the installer technology** (stays Squirrel/electron-forge from 011).

## Acceptance criteria

1. **Version is single-sourced:** the git tag `vX.Y.Z`, the installer artifact name,
   and `GET /system/status` `version` all report the same `X.Y.Z` for a build.
2. **Tag → Release:** pushing a `vX.Y.Z` tag produces a GitHub Release with the signed
   `LoreSetup.exe`, `.nupkg`, and `RELEASES` attached, built reproducibly in CI from
   pinned inputs.
3. **winget works end-to-end:** a valid manifest for that version passes
   `winget validate`; in a clean Windows Sandbox, `winget install Lore` installs it,
   and `winget upgrade Lore` moves a prior install to the new version (upgrade
   detection via `AppsAndFeaturesEntries` confirmed).
4. **Docs match reality:** README shows the winget install/upgrade commands;
   `privacy.md` states updates run through the package manager with no in-app check;
   `roadmap.md` notes Homebrew for macOS.
5. **Egress unchanged:** no new outbound call is added to the app or agent; the
   `privacy.md` egress table is unchanged and its "**No update check**" line remains
   true. (This is the load-bearing criterion — a regression here defeats the point.)

## Non-functional requirements

- **Zero-egress preserved** — the default install still makes no non-loopback call
  except the user-configured model endpoint (constitution §1, §4.3).
- **Reproducible release** from pinned inputs (same standard as 011's installer build).
- **Signed artifacts** — winget validation and SmartScreen both favor a signed
  installer; this rides on 011's signing wiring. Launching unsigned is possible but a
  conscious, documented choice.
- **Automatable, idempotent** manifest submission — re-running a release must not
  produce a broken or duplicate manifest.

## Risks

- **winget upgrade detection vs. Squirrel's HKCU registration.** Squirrel registers its
  uninstall entry under `HKCU`; if the manifest's `AppsAndFeaturesEntries` (ProductCode,
  DisplayVersion, `scope: user`) don't match, `winget upgrade` won't see the installed
  version. *Mitigation:* derive those fields from an actual install and verify upgrade
  in Windows Sandbox before submitting.
- **Unsigned installer friction.** An unsigned `.exe` trips SmartScreen and can slow
  winget-pkgs validation. *Mitigation:* land the code-signing cert (011 step 5) before
  first submission, or submit unsigned as a documented interim.
- **Version drift between app and agent.** *Mitigation:* the single-source-of-truth
  task (T001) is a hard prerequisite for everything else.
- **winget-pkgs is community-moderated.** PR review/merge latency is outside our CI's
  control. *Mitigation:* automate generation so resubmission is cheap; don't gate our
  release on the external merge.

## Human-gated (cannot be fully automated)

Consistent with 011, some steps need a human, a credential, or an external account and
are **not** done by the implementing agent:

- The repo being **public** and a **signing cert** in hand (both 011 launch steps).
- The **first** `winget-pkgs` PR and Microsoft's review/merge.
- The actual **release tag** push that cuts version `vX.Y.Z`.

The agent's job is to make all of the above a **single tag push** away: the version
plumbing, the release workflow, the manifest + its generator, and the docs.
