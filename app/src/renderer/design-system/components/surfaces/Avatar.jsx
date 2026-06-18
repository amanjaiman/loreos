const React = window.React;

function initials(name) {
  if (!name) return "";
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((p) => p[0])
    .join("")
    .toUpperCase();
}

/** Lore Avatar — user image or initials with optional status dot. */
export function Avatar({ src, name, size = "md", status, className = "", ...rest }) {
  return React.createElement(
    "span",
    { className: ["lore-avatar", `lore-avatar--${size}`, className].filter(Boolean).join(" "), ...rest },
    src
      ? React.createElement("img", { src, alt: name || "" })
      : React.createElement("span", null, initials(name)),
    status && React.createElement("span", { className: `lore-avatar__status lore-avatar__status--${status}` }),
  );
}
