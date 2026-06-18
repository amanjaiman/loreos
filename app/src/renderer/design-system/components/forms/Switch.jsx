const React = window.React;

/** Lore Switch — on/off toggle with optional label. */
export function Switch({ label, className = "", ...rest }) {
  return React.createElement(
    "label",
    { className: ["lore-switch", className].filter(Boolean).join(" ") },
    React.createElement("input", { type: "checkbox", role: "switch", ...rest }),
    React.createElement(
      "span",
      { className: "lore-switch__track" },
      React.createElement("span", { className: "lore-switch__thumb" }),
    ),
    label != null && React.createElement("span", null, label),
  );
}
