---
name: lore-design
description: Use this skill to generate well-branded interfaces and assets for Lore (an "Ambient Intelligence" product), either for production or throwaway prototypes/mocks/etc. Contains essential design guidelines, colors, type, fonts, assets, and UI components for prototyping. Lore ships two themes — Coastal (Minimal, light) and Nocturne (Techy, dark) — switchable via data attributes on <html> or the ThemeSelector component.
user-invocable: true
---

# Lore Design System

Read `readme.md` in this skill first — it is the full design guide (brand voice, the two themes, visual foundations, iconography, and a file index). Then explore the other files as needed.

## Quick start

Link the one stylesheet. A bare `<html>` renders the **Coastal** theme; add attributes for **Nocturne**:

```html
<!-- Coastal (default): Minimal + Light -->
<html><link rel="stylesheet" href="styles.css"></html>

<!-- Nocturne: Techy + Dark -->
<html data-palette="3" data-style="techy" data-mode="dark">
  <link rel="stylesheet" href="styles.css">
```

- **Coastal** (default) — Cerulean/Coral, Manrope, soft-rounded, light.
- **Nocturne** — Yale Blue/Bronze, Space Grotesk, crisp corners, dark.
- Easiest switch: drop `<ThemeSelector />` (from `components/theming/`) — it sets + persists the theme on `<html>`.

Open `showcase.html` to see both themes live.

## Building artifacts

- **Visual mocks, slides, throwaway prototypes:** copy `styles.css` + `tokens/` + `assets/` out and write static HTML using the component **classes** (`.lore-btn`, `.lore-card`, `.lore-input`, …) and tokens (`var(--primary)`, `var(--bg)`, …). Everything re-themes automatically from the `<html>` attributes. This needs no build step. See `showcase.html` for a complete worked example.
- **Production / React code:** the components live in `components/<group>/<Name>.jsx` as named exports (`Button`, `Card`, `Input`, …). Read each `<Name>.prompt.md` for usage and `<Name>.d.ts` for props. They depend only on React and the CSS custom properties.
- **Icons:** [Lucide](https://lucide.dev) via CDN (`<i data-lucide="name"></i>` + `lucide.createIcons()`), always `currentColor`.

## Voice

Calm + precise. Speak to the user as "you", refer to the product as "Lore". Sentence case everywhere except mono uppercase eyebrows (often opening with `//`). No emoji. See the CONTENT FUNDAMENTALS section of `readme.md`.

## If invoked with no other guidance

Ask what the user wants to build or design, ask a few focused questions (surface, theme, audience), then act as an expert Lore designer — outputting **HTML artifacts** for mocks/prototypes or **production code** when working in a codebase.
