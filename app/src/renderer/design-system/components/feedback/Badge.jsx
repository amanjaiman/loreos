const React = window.React;

/** Lore Badge — compact status / category label. */
export function Badge({ variant = "neutral", dot = false, className = "", children }) {
  const cls = [
    "lore-badge",
    variant !== "neutral" && `lore-badge--${variant}`,
    className,
  ]
    .filter(Boolean)
    .join(" ");
  return React.createElement(
    "span",
    { className: cls },
    dot && React.createElement("span", { className: "lore-badge__dot" }),
    children,
  );
}
