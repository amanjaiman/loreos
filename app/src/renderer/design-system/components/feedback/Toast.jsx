const React = window.React;

const ICONS = {
  success: "check-circle",
  warning: "alert-triangle",
  danger: "alert-octagon",
  info: "info",
};

/** Lore Toast — transient notification surface. */
export function Toast({ variant = "info", title, icon, className = "", children }) {
  React.useEffect(() => {
    if (window.lucide) window.lucide.createIcons();
  });
  const name = icon || ICONS[variant] || "info";
  return React.createElement(
    "div",
    { className: ["lore-toast", `lore-toast--${variant}`, className].filter(Boolean).join(" "), role: "status" },
    React.createElement("span", { className: "lore-toast__icon" },
      React.createElement("i", { "data-lucide": name })),
    React.createElement(
      "div",
      { className: "lore-toast__body" },
      title && React.createElement("div", { className: "lore-toast__title" }, title),
      children && React.createElement("div", { className: "lore-toast__msg" }, children),
    ),
  );
}
