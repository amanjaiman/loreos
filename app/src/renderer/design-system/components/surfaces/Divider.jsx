const React = window.React;

/** Lore Divider — horizontal rule, optionally with a centered label, or vertical. */
export function Divider({ label, vertical = false, className = "" }) {
  if (vertical) {
    return React.createElement("div", {
      className: ["lore-divider", "lore-divider--vertical", className].filter(Boolean).join(" "),
      role: "separator",
      "aria-orientation": "vertical",
    });
  }
  if (label) {
    return React.createElement(
      "div",
      { className: ["lore-divider--label", className].filter(Boolean).join(" "), role: "separator" },
      React.createElement("span", { className: "lore-divider__text" }, label),
    );
  }
  return React.createElement("hr", {
    className: ["lore-divider", className].filter(Boolean).join(" "),
  });
}
