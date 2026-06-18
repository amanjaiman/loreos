const React = window.React;

/** Lore ProgressBar — determinate progress with optional label + value. */
export function ProgressBar({ value = 0, max = 100, label, showValue = false, className = "" }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  return React.createElement(
    "div",
    { className: ["lore-progress", className].filter(Boolean).join(" ") },
    (label || showValue) &&
      React.createElement(
        "div",
        { className: "lore-progress__head" },
        React.createElement("span", null, label),
        showValue && React.createElement("span", null, `${Math.round(pct)}%`),
      ),
    React.createElement(
      "div",
      { className: "lore-progress__track", role: "progressbar", "aria-valuenow": value, "aria-valuemax": max },
      React.createElement("div", { className: "lore-progress__fill", style: { width: `${pct}%` } }),
    ),
  );
}
