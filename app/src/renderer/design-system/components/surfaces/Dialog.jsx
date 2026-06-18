const React = window.React;

const X = () =>
  React.createElement(
    "svg",
    { viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: "2", strokeLinecap: "round", width: 18, height: 18 },
    React.createElement("path", { d: "M18 6 6 18M6 6l12 12" }),
  );

/** Lore Dialog — modal overlay. Render only when `open` is true. */
export function Dialog({ open, onClose, title, footer, showClose = true, className = "", children }) {
  React.useEffect(() => {
    if (!open) return;
    const onKey = (e) => e.key === "Escape" && onClose && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open) return null;
  return React.createElement(
    "div",
    { className: "lore-dialog__overlay", onMouseDown: (e) => e.target === e.currentTarget && onClose && onClose() },
    React.createElement(
      "div",
      { className: ["lore-dialog", className].filter(Boolean).join(" "), role: "dialog", "aria-modal": "true" },
      React.createElement(
        "div",
        { style: { display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: "16px" } },
        title && React.createElement("div", { className: "lore-dialog__title" }, title),
        showClose &&
          React.createElement(
            "button",
            { className: "lore-iconbtn lore-iconbtn--sm", onClick: onClose, "aria-label": "Close", type: "button" },
            React.createElement(X),
          ),
      ),
      children && React.createElement("div", { className: "lore-dialog__body" }, children),
      footer && React.createElement("div", { className: "lore-dialog__footer" }, footer),
    ),
  );
}
