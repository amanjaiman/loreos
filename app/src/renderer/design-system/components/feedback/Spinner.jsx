const React = window.React;

/** Lore Spinner — indeterminate loading indicator. */
export function Spinner({ size = 20, className = "", ...rest }) {
  return React.createElement("span", {
    className: ["lore-spinner", className].filter(Boolean).join(" "),
    style: { width: size, height: size },
    role: "status",
    "aria-label": "Loading",
    ...rest,
  });
}
