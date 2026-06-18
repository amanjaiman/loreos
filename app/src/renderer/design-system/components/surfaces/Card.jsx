const React = window.React;

/** Lore Card — content surface with optional eyebrow, title, subtitle, footer. */
export function Card({
  eyebrow,
  title,
  subtitle,
  footer,
  interactive = false,
  className = "",
  children,
  ...rest
}) {
  return React.createElement(
    "div",
    { className: ["lore-card", interactive && "lore-card--interactive", className].filter(Boolean).join(" "), ...rest },
    eyebrow && React.createElement("div", { className: "lore-card__eyebrow" }, eyebrow),
    title && React.createElement("div", { className: "lore-card__title" }, title),
    subtitle && React.createElement("div", { className: "lore-card__subtitle" }, subtitle),
    children && React.createElement("div", { className: "lore-card__body" }, children),
    footer && React.createElement("div", { className: "lore-card__footer" }, footer),
  );
}
