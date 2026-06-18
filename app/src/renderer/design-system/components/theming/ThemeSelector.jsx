const React = window.React;

/**
 * The two Lore themes shipped with the design system. Each is a self-contained
 * look (palette + style + mode). Pass a custom `themes` array to override.
 */
export const LORE_THEMES = [
  { id: "coastal", label: "Coastal", palette: "2", style: "minimal", mode: "light", dots: ["#0081af", "#f76c5e"] },
  { id: "nocturne", label: "Nocturne", palette: "3", style: "techy", mode: "dark", dots: ["#344966", "#c9a74d"] },
];

/** Imperatively apply a theme preset to an element (defaults to <html>). */
export function applyLoreTheme(theme, target) {
  const root = target || (typeof document !== "undefined" ? document.documentElement : null);
  if (!root || !theme) return;
  root.setAttribute("data-palette", theme.palette);
  root.setAttribute("data-style", theme.style);
  if (theme.mode) root.setAttribute("data-mode", theme.mode);
}

/**
 * ThemeSelector — segmented control that switches the active Lore theme and
 * persists the choice. On mount it applies the saved (or default) theme, so
 * dropping it anywhere makes both themes available across that surface.
 */
export function ThemeSelector({
  themes = LORE_THEMES,
  target,
  storageKey = "lore-theme-preset",
  defaultId,
  onChange,
  className = "",
}) {
  const [activeId, setActiveId] = React.useState(() => {
    let saved = null;
    try { saved = localStorage.getItem(storageKey); } catch (e) {}
    const valid = saved && themes.some((t) => t.id === saved);
    return valid ? saved : defaultId || themes[0].id;
  });

  // Apply the active theme whenever it changes (and on mount).
  React.useEffect(() => {
    const theme = themes.find((t) => t.id === activeId) || themes[0];
    applyLoreTheme(theme, target);
  }, [activeId, themes, target]);

  const select = (theme) => {
    setActiveId(theme.id);
    try { localStorage.setItem(storageKey, theme.id); } catch (e) {}
    onChange && onChange(theme);
  };

  return React.createElement(
    "div",
    { className: ["lore-themeselect", className].filter(Boolean).join(" "), role: "group", "aria-label": "Theme" },
    themes.map((t) =>
      React.createElement(
        "button",
        {
          key: t.id,
          type: "button",
          className: "lore-themeselect__opt",
          "aria-pressed": t.id === activeId,
          onClick: () => select(t),
        },
        t.dots &&
          React.createElement("span", {
            className: "lore-themeselect__dot",
            style: { background: `linear-gradient(135deg, ${t.dots[0]} 50%, ${t.dots[1]} 50%)` },
          }),
        t.label,
      ),
    ),
  );
}
