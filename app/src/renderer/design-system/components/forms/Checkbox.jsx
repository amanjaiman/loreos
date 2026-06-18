const React = window.React;

const Check = () =>
  React.createElement(
    "svg",
    { viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: "3", strokeLinecap: "round", strokeLinejoin: "round" },
    React.createElement("path", { d: "M20 6 9 17l-5-5" }),
  );

/** Lore Checkbox — boxed control with label. */
export function Checkbox({ label, className = "", ...rest }) {
  return React.createElement(
    "label",
    { className: ["lore-check", className].filter(Boolean).join(" ") },
    React.createElement("input", { type: "checkbox", ...rest }),
    React.createElement(
      "span",
      { className: "lore-check__box lore-check__box--cb" },
      React.createElement(Check),
    ),
    label != null && React.createElement("span", null, label),
  );
}
