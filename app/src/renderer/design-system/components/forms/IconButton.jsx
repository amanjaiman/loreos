const React = window.React;

/** Lore IconButton — a square button containing a single Lucide icon. */
export function IconButton({
  icon,
  label,
  variant = "ghost",
  size = "md",
  className = "",
  ...rest
}) {
  React.useEffect(() => {
    if (window.lucide) window.lucide.createIcons();
  });
  const cls = [
    "lore-iconbtn",
    variant === "solid" && "lore-iconbtn--solid",
    size !== "md" && `lore-iconbtn--${size}`,
    className,
  ]
    .filter(Boolean)
    .join(" ");
  return React.createElement(
    "button",
    { className: cls, "aria-label": label, title: label, ...rest },
    React.createElement("i", { "data-lucide": icon }),
  );
}
