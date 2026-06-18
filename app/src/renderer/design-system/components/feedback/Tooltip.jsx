const React = window.React;

/** Lore Tooltip — hover/focus label above its child. */
export function Tooltip({ label, className = "", children }) {
  return React.createElement(
    "span",
    { className: ["lore-tooltip", className].filter(Boolean).join(" "), tabIndex: 0 },
    children,
    React.createElement("span", { className: "lore-tooltip__pop", role: "tooltip" }, label),
  );
}
