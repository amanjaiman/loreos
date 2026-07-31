# v2-004 — Shell Redesign · Plan

> SDD artifact: **how.** Designs and decisions behind
> [`specification.md`](specification.md). Task breakdown in [`tasks.md`](tasks.md).

## Windows specifics (the three that bite)

**1. `titleBarStyle: 'hidden'`, never `frame: false`.** `frame: false` removes the
non-client area entirely, which also removes **Snap Layouts** — the flyout Windows 11
shows on hover over Maximize. Users notice its absence. `titleBarStyle: 'hidden'` plus
`titleBarOverlay` keeps real, snap-aware native controls in a strip we control the
colour of.

**2. A hidden menu, not a null menu.** `Menu.setApplicationMenu(null)` silently breaks
`Ctrl+C/V/X/A/Z` inside text inputs on Windows — those accelerators are registered *by*
the menu. The fix is a menu template whose top-level items are `visible: false`;
accelerators still register, the bar does not render. Roles used: `editMenu` (the
clipboard set) plus an explicitly hidden `viewMenu` for devtools in development.

**3. The overlay must be re-coloured on theme change.** `titleBarOverlay` colours are
set at window construction and do not follow CSS. The renderer tells the main process
the resolved colours after each theme switch, over a new one-way IPC channel
(`lore:set-titlebar`). Main clamps the payload to two hex strings before calling
`setTitleBarOverlay` — the renderer is untrusted input like any other.

## Shell geometry

```
grid-template-columns: 236px 1fr;      /* rail | content     */
grid-template-rows:    44px  1fr;      /* strip | card       */

.rail   → column 1, rows 1-2   (wordmark row aligns to the 44px strip)
.strip  → column 2, row 1      (drag surface; native controls sit here)
.card   → column 2, row 2      (margin: 0 12px 12px 0; radius 20px)
```

Drag regions: `.strip` and the rail's wordmark row get `-webkit-app-region: drag`;
every interactive descendant gets `no-drag`. The card never drags — it scrolls.

**Why the strip is ground, not card.** With the card inset below a full-width strip,
the native controls always sit on the ground plane and can never collide with content.
Bleeding the card to the top edge would mean reserving ~148 px of right padding in the
card header forever.

## The `backdrop-filter` clip bug

In Chromium an element with `backdrop-filter` **escapes its ancestor's
`overflow: hidden` + `border-radius` clip** and repaints square corners over it. This
flattened the card's top edge during design. Two rules follow:

- The card's sticky header carries its **own** top radii (`--r-tl` / `--r-tr`), so it
  paints the right shape whether or not the ancestor clip applies.
- Corner radii are per-corner CSS variables on the ground, so header and card can never
  drift apart.

## Glass, under a rule

Translucency applies **only to surfaces that float over something else**: the card over
the ground, the sticky card header over scrolling content, the rail's search and live
panels over the ground gradient, and the active nav indicator. Rows *inside* the card
stay opaque — nothing passes behind them, so frosting them is decoration.

The ground carries two low-alpha radial blobs rather than one, because a blur over a
flat fill reads as a grey wash.

## Coastal Dark

Implemented as a **mode flip on the Coastal palette**, which is the axis split
`colors.css` already documents ("mode only re-maps semantics onto the active ramp").
The existing `:root[data-mode="dark"]` block maps onto whichever ramp is active; for
Coastal that ramp bottoms out at the cool Carbon `#1b2021`. So a more specific rule
extends the warm family downward before the semantic mapping reads it:

```
:root:not([data-palette="3"])[data-mode="dark"]   /* Coastal + dark */
  --n-6: #554c40  --n-7: #322b23  --n-8: #221d17  --n-9: #17130e
```

Specificity beats the base dark block, so ordering within the file is not load-bearing.
Cerulean and Coral are lifted for contrast on the dark ground; Wheat is untouched —
it is what carries the Coastal feeling into the dark.

`LORE_THEMES` in the vendored `ThemeSelector` becomes Coastal + Coastal Dark. Per
v2-003's divergence rule this is a library-level edit and is contributed back, not
patched around in app code.

## Chart colour validation

Run against the OKLCH checks (lightness band, chroma floor, CVD separation,
normal-vision floor, contrast vs surface):

| Palette | Mode | Result |
|---|---|---|
| kinds: `#0081af,#f76c5e,#eaba6b,#3a9d78,#7d7565` | light | **FAIL** — Clay outside band; warm grey below chroma floor; grey↔green ΔE 12.4 normal-vision (floor 15) |
| `#0081af,#f76c5e` | light | **PASS** (contrast WARN on Coral → direct labels required) |
| `#35a8d0,#df6250` | dark | **FAIL** — `#35a8d0` L 0.685, above the dark band 0.48–0.67 |
| `#2499c2,#df6250` | dark | **PASS** |

Conclusions baked into the design: memory *kind* is never encoded by colour alone in a
chart; the composition bars are one hue (magnitude, sequential) with direct labels; the
selectivity ratio is two hues plus a neutral remainder (the emphasis form). Hence
`--viz-1` / `--viz-2` / `--viz-rest`, per mode, rather than reusing `--primary`.

## Motion

Gated on state change, never decorative. Durations reuse the existing tokens
(`--dur-fast` 110ms, `--dur` 200ms, `--dur-slow` 360ms) on `--ease-out`; two additions
run once each and are deliberately slower (440 ms page entrance, 700–900 ms meter/bar
draw). A restrained spring `cubic-bezier(.22, 1.18, .36, 1)` carries the nav indicator
and hover states.

Moments: route cascade with **travel direction** (down the rail enters from below),
sliding nav indicator, condensing card header, row collapse on keep/dismiss, meters
drawing in, theme cross-fade, and the **memory-landing** animation — the one Lore-
specific moment, because capture is the product and it was previously silent.

Theme cross-fade requires the ground to split `background-color` (transitionable) from
`background-image` (gradient stops are not).

## Files

| Path | Change |
|---|---|
| `app/src/index.ts` | frameless window, hidden menu, `lore:set-titlebar` handler |
| `app/src/preload.ts` | expose `setTitleBar` |
| `app/src/renderer/chrome/Rail.tsx` | new — nav + presence |
| `app/src/renderer/chrome/LiveElement.tsx` | new — pulse, watched window, pause |
| `app/src/renderer/chrome/AppShell.tsx` | rewritten to the ground/strip/rail/card grid |
| `app/src/renderer/chrome/Header.tsx` | deleted |
| `app/src/renderer/chrome/chrome.css` | rewritten |
| `app/src/renderer/lib/useTitleBarSync.ts` | new — pushes theme colours to main |
| `app/src/renderer/design-system/tokens/colors.css` | Coastal Dark + `--viz-*` |
| `app/src/renderer/design-system/tokens/typography.css` | scale +1 |
| `app/src/renderer/design-system/components/theming/ThemeSelector.jsx` | theme list |
| `app/src/renderer/views/Today.tsx` + `today.css` | two-column Home |

## Risks

- **Rail crowding on short windows.** Nav + trace + live + theme control compete below
  ~700 px. The trace is the first thing to drop; it is already the lowest-value element.
- **Context column at narrow widths.** It collapses to a row, but the trigger should key
  off the *card's* width (container query), not the viewport — the rail eats 236 px
  before content sees any. Shipped with a viewport fallback; noted in tasks.
- **Overlay colour drift.** If a future theme lands without updating the sync hook, the
  controls silently stay the previous theme's colour. Mitigated by deriving the colours
  from computed CSS custom properties rather than a hard-coded map.
