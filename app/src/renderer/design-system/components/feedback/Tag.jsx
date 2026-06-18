const React = window.React;

const X = () =>
  React.createElement(
    "svg",
    { viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: "2.5", strokeLinecap: "round" },
    React.createElement("path", { d: "M18 6 6 18M6 6l12 12" }),
  );

/** Lore Tag — removable chip for filters / selections. */
export function Tag({ onRemove, className = "", children, ...rest }) {
  return React.createElement(
    "span",
    { className: ["lore-tag", className].filter(Boolean).join(" "), ...rest },
    children,
    onRemove &&
      React.createElement(
        "button",
        { className: "lore-tag__close", onClick: onRemove, "aria-label": "Remove", type: "button" },
        React.createElement(X),
      ),
  );
}
