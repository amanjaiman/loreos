const React = window.React;

/** Lore Radio — single radio control with label. Group via the `name` prop. */
export function Radio({ label, className = "", ...rest }) {
  return React.createElement(
    "label",
    { className: ["lore-check", className].filter(Boolean).join(" ") },
    React.createElement("input", { type: "radio", ...rest }),
    React.createElement(
      "span",
      { className: "lore-check__box lore-check__box--radio" },
      React.createElement("span", { className: "lore-check__dot" }),
    ),
    label != null && React.createElement("span", null, label),
  );
}
