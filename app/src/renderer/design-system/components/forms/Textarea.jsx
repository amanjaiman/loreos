const React = window.React;

let _tid = 0;

/** Lore Textarea — multi-line text field with label, hint, error. */
export function Textarea({
  label,
  hint,
  error,
  required = false,
  rows = 4,
  id,
  className = "",
  ...rest
}) {
  const fid = React.useMemo(() => id || `lore-ta-${++_tid}`, [id]);
  return React.createElement(
    "div",
    { className: "lore-field" },
    label &&
      React.createElement(
        "label",
        { className: "lore-field__label", htmlFor: fid },
        label,
        required && React.createElement("span", { className: "lore-field__req" }, "*"),
      ),
    React.createElement("textarea", {
      id: fid,
      rows,
      className: ["lore-input", error && "lore-input--error", className].filter(Boolean).join(" "),
      "aria-invalid": error ? "true" : undefined,
      ...rest,
    }),
    error
      ? React.createElement("span", { className: "lore-field__error" }, error)
      : hint && React.createElement("span", { className: "lore-field__hint" }, hint),
  );
}
