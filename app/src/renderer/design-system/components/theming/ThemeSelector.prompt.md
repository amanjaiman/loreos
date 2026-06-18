Segmented control that switches the active Lore theme and persists it — drop it once (e.g. in a header) to make both bundled themes available across that surface. By default it themes the whole page (`<html>`).

```jsx
// Anywhere in your app — applies + remembers the choice:
<ThemeSelector />

// Theme only a subtree, or react to changes:
<ThemeSelector target={panelEl} onChange={(t) => console.log(t.id)} />
```

Ships two themes via `LORE_THEMES`: **Coastal** (Palette 2 · Minimal · light) and **Nocturne** (Palette 3 · Techy · dark). Override with the `themes` prop, or apply a theme imperatively with `applyLoreTheme(theme, target)`. On mount it applies the saved theme (or `defaultId`), so the selected look survives reloads.
