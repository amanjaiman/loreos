const React = window.React;

/**
 * Lore Button — primary action control.
 * `icon` / `iconRight` accept a Lucide icon name (string); the host page must
 * load lucide and the component refreshes icons after render.
 */
export function Button({
  variant = "primary",
  size = "md",
  icon,
  iconRight,
  fullWidth = false,
  as = "button",
  className = "",
  children,
  ...rest
}) {
  React.useEffect(() => {
    if (window.lucide) window.lucide.createIcons();
  });
  const Tag = as;
  const cls = [
    "lore-btn",
    `lore-btn--${variant}`,
    size !== "md" && `lore-btn--${size}`,
    fullWidth && "lore-btn--full",
    className,
  ]
    .filter(Boolean)
    .join(" ");
  return React.createElement(
    Tag,
    { className: cls, ...rest },
    icon && React.createElement("span", { className: "lore-btn__icon" },
      React.createElement("i", { "data-lucide": icon })),
    children != null && React.createElement("span", null, children),
    iconRight && React.createElement("span", { className: "lore-btn__icon" },
      React.createElement("i", { "data-lucide": iconRight })),
  );
}
