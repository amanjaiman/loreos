# Lore — Design System

> **Lore · Ambient Intelligence.** An ambient layer that listens in the background and threads your conversations, documents, and decisions into one quiet memory — surfaced exactly when it matters. The brand voice is **calm + precise**: engineered, unhurried, never shouty.

This design system ships **two ready-to-use themes**, each a complete look. A theme is just three attributes on `<html>` — the `<ThemeSelector>` component sets them for you:

| Theme                 | `data-palette`   | `data-style` | `data-mode` |
| --------------------- | ---------------- | ------------ | ----------- |
| **Coastal** (default) | _(unset → base)_ | `minimal`    | `light`     |
| **Coastal Dark**      | `2`              | `minimal`    | `dark`      |

With **no attributes**, a page renders full **Coastal**. Add `<ThemeSelector />` (or set the attributes directly) to switch to **Nocturne**. Open **`showcase.html`** for the live switcher and a full component preview.

```html
<!-- Coastal (default) -->
<html>
    <link rel="stylesheet" href="styles.css" />
</html>

<!-- Nocturne -->
<html data-mode="dark">
    <link rel="stylesheet" href="styles.css" />
</html>
```

The axes are still orthogonal under the hood (palette × style × mode), so you _can_ mix them — but the two themes above are the supported, designed combinations.

---

## The two themes

| Theme        | Palette roles                                                                                                                                     | Style               |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------- |
| **Coastal**  | Cerulean primary `#0081AF` · Vibrant Coral accent `#F76C5E` · Sunlit Clay secondary `#EABA6B` · Wheat tone `#EAD2AC` · Carbon Black ink `#1B2021` | **Minimal** · Light |
| **Nocturne** | Yale Blue primary `#344966` · Golden Bronze accent `#C9A74D` · Dusty Mauve secondary `#8E6778` · Porcelain `#F0F4EF` · Ink Black `#0D1821`        | **Techy** · Dark    |

Each theme's palette defines its brand roles plus a 10-step neutral ramp (`--n-0` lightest → `--n-9` darkest). **Modes only re-map semantics** (`--bg`, `--surface`, `--text`, …) onto that ramp — the palette never touches semantics, the mode never touches the ramp.

## The two styles

- **Minimal** (Coastal) — Manrope + JetBrains Mono. Pill / generously-rounded corners (`--radius-control: full`), airier controls, soft diffuse shadows, sans eyebrows with gentle tracking. Reads quiet and premium. _This is the `:root` default._
- **Techy** (Nocturne) — REMOVED in v2-004. The style axis changed typeface and shadow character as well as the ground, so the dark theme read as a different product. Only the Minimal style ships.

---

## Sources

This is an **original brand built from scratch** for the prompt "Lore: Ambient Intelligence." There was **no attached codebase, Figma, or asset pack** — all foundations, copy, and the logo were authored here. Inputs that defined the brief:

- Color palettes for two themes (Coastal, Nocturne), supplied by the user.
- Two style directions (Techy, Modern/Minimal), supplied by the user, paired to the themes.
- Mood: _calm / ambient / quiet_ + _precise / technical / engineered_. Motion: _smooth & subtle_. Logo: _wordmark + simple geometric glyph mark_.

**Fonts are Google-hosted substitutes** (Manrope, JetBrains Mono) chosen to fit the brief — no brand font files were provided. Swap the `@font-face` sources in `tokens/fonts.css` if real brand fonts arrive. → _See CAVEATS at the bottom._

---

## CONTENT FUNDAMENTALS — how Lore writes

The voice is **calm, certain, and quietly clever**. Lore is an assistant that has already done the work; it never hypes, never nags.

- **Person:** Speak to the user as **"you"**; refer to the product as **"Lore"** in the third person ("Lore remembers", "Lore summarized your standup"). Avoid first-person "I".
- **Tone:** Declarative and unhurried. Short sentences. Confidence without exclamation. _"Memory, kept warm." · "Lore remembers so you don't have to." · "Nothing demands your attention until it earns it."_
- **Casing:** Sentence case everywhere in product UI and body copy. **Eyebrows / overlines are the one uppercase moment** — set in mono with wide tracking (e.g. `// MEMORY.INDEX`, `AMBIENT INTELLIGENCE`). Titles use sentence case, never Title Case.
- **The `//` motif:** Mono eyebrows often open with `//` (a code-comment nod) — e.g. `// memory, kept warm`. Use sparingly, only on labels, never in sentences.
- **Verbs:** Ambient, sensory, low-effort — _listen, surface, thread, recall, keep warm, remember, summon_. Avoid aggressive SaaS verbs (_supercharge, unlock, crush, leverage_).
- **Numbers & data:** Mono, terse, with a unit and an arrow when showing a result (`latency → 42ms`, `1,284 threads`). Don't manufacture stats for decoration.
- **Emoji:** **None.** The brand expresses warmth through color and copy, not emoji.
- **Punctuation:** Em dashes for asides; periods end even short fragments. A single accent period in the wordmark — `Lore.` — is a brand signature.

**Good:** "Lore summarized your standup and linked the related thread." → calm, specific, third-person product.
**Avoid:** "🚀 We supercharged your workflow!! Unlock memory now!" → hype, emoji, first-person plural.

---

## VISUAL FOUNDATIONS

**Color & vibe.** Two grounds: **Coastal** is paper-warm cream with a Carbon-Black ink and a confident Cerulean + Coral pairing; **Nocturne** is deep Ink-Black with cool Porcelain text and a Yale-Blue + Golden-Bronze pairing. Light mode is paper-warm, not stark white; dark mode is deep ink, not pure black. Imagery, when used, should be **warm and softly lit** in Coastal and **cool and architectural** in Nocturne — grain is welcome, heavy saturation is not. There are no gradients in the brand except the tiny two-tone theme dots in the switcher; surfaces are flat and honest.

**Type.** A single family carries display + body (Manrope) with JetBrains Mono for eyebrows, code, data, and metadata. Display is tight-tracked (`-0.03em`) and large; body is `15px` at relaxed line-height; the mono eyebrow is the consistent signature across both styles. Scale runs `11 → 72px`.

**Spacing & layout.** 4px base scale. Content sits in a centered column (~1140px max) with generous vertical rhythm (`--space-16` between sections). Layout is calm and single-focus — no dense dashboards by default. The header is sticky with a blurred translucent ground.

**Backgrounds.** Flat token color (`--bg`). No full-bleed photography, no repeating patterns, no decorative gradients. The only texture is the optional `//` mono eyebrow and the blurred sticky bar.

**Corners & cards.** Radius is the loudest style signal: **Techy** is near-square (cards `6px`, controls `4px`); **Minimal** is soft (cards `20px`, controls fully pill). Cards are a flat `--surface` with a 1px `--border` and `--shadow-sm`; interactive cards lift `-2px` with `--shadow-md` and a stronger border on hover.

**Borders & shadows.** Hairline 1px borders using `--border` / `--border-strong`. Two shadow personalities: Techy = tight, low, hard-edged; Minimal = soft, tall, diffuse. Dark mode deepens all shadows. Elevation is restrained — most surfaces sit at `sm`, modals at `pop`.

**Motion.** Smooth & subtle. Standard `--ease: cubic-bezier(.2,.6,.2,1)` over `--dur: 200ms`; entrances use `--ease-out` for a gentle settle. Theme changes cross-fade background/color. **No bounces, no infinite loops** (only the loading spinner rotates). All durations collapse to `0ms` under `prefers-reduced-motion`.

**Interaction states.** Hover = a subtle wash (`--surface-hover`, a 4% text mix) or a darker primary (`--primary-hover`); links/ghost buttons gain color. Press = `--primary-press` (darker) plus a near-imperceptible `scale(0.99)` + 0.5px nudge. Focus = a 3–4px soft ring built live from `--primary` (`--ring`), never a hard outline.

**Transparency & blur.** Used in exactly two places: the sticky header ground (`color-mix(--bg 82%, transparent)` + `blur(12px)`) and the dialog overlay (`color-mix(--inverse-bg 55%, transparent)` + `blur(3px)`). Elsewhere, surfaces are opaque.

---

## ICONOGRAPHY

- **System:** [**Lucide**](https://lucide.dev) — loaded from CDN (`https://unpkg.com/lucide@latest`). Chosen for its even **2px stroke, rounded caps/joins**, and large, consistent set, which match Lore's calm-precise feel. _This is a substitution_ — no brand icon set was provided. → see CAVEATS.
- **Usage:** Icons render via `<i data-lucide="name"></i>` followed by `lucide.createIcons()`. React components (`Button`, `IconButton`, `Toast`) take a Lucide **name string** and refresh icons after render.
- **Color & size:** Always `currentColor` so icons inherit text/role color and theme automatically. UI icons sit at 16–18px; button icons scale to ~1.2em of the label.
- **Intrinsic affordances** (checkbox tick, select chevron, close ×) are tiny inline SVGs baked into the component CSS/JSX — not Lucide — so controls never depend on the icon CDN being present.
- **Emoji / unicode as icons:** never. **The brand mark** is the transparent three-layer context icon (`assets/lore-icon.svg`) paired with the `Lore` wordmark.

---

## INDEX — what's in this system

**Root**

- `styles.css` — the single entry point consumers link (import-only).
- `showcase.html` — **interactive theme switcher** (Coastal ⇄ Nocturne) + full component preview.
- `readme.md` — this guide. · `SKILL.md` — Agent-Skills wrapper.
- `assets/lore-icon.svg` — the transparent brand mark for every theme.

**`tokens/`** (all reachable from `styles.css`)

- `fonts.css` (webfont imports) · `colors.css` (2 themes + light/dark) · `typography.css` · `spacing.css` · `effects.css` · `components.css` (component class styles).

**Components** — `window.LoreDesignSystem_d94c44.<Name>`

- `components/forms/` — **Button, IconButton, Input, Textarea, Select, Checkbox, Radio, Switch**
- `components/feedback/` — **Badge, Tag, Spinner, ProgressBar, Toast, Tooltip**
- `components/surfaces/` — **Card, Avatar, Tabs, Divider, Dialog**
- `components/theming/` — **ThemeSelector** (+ `LORE_THEMES`, `applyLoreTheme`) — switches between the two themes: **Coastal** (Minimal · Light) and **Nocturne** (Techy · Dark). Drop `<ThemeSelector />` anywhere to make both available across that surface; it applies + persists the choice on `<html>`.

Each component is `<Name>.jsx` + `<Name>.d.ts` (props) + `<Name>.prompt.md` (usage). Each directory has a `*.card.html` rendering live variants in the Design System tab.

**`guidelines/`** — foundation specimen cards (the Design System tab): Coastal & Nocturne palettes, brand roles, surfaces & text, status; type display/body/mono/scale/weights; spacing/radius/elevation; brand logo & iconography.

---

## CAVEATS

1. **Fonts are substitutes.** Manrope / JetBrains Mono are Google-hosted stand-ins chosen for the brief. If Lore has real brand fonts, drop them in and update `tokens/fonts.css`.
2. **Icons are substitutes.** Lucide via CDN. Swap for a bespoke set if one exists.
3. **The logo is original**: three cascading layers represent activity becoming synthesized memory and portable context.
4. **No UI kits** were built, per your direction (components, type, color, and standard foundations only).
