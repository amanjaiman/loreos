const React = window.React;

let _id = 0;

/** Lore Input — labelled text field with optional leading icon, hint, error. */
export function Input({
  label,
  hint,
  error,
  icon,
  required = false,
  id,
  className = "",
  ...rest
}) {
  React.useEffect(() => {
    if (window.lucide) window.lucide.createIcons();
  });
  const fid = React.useMemo(() => id || `lore-input-${++_id}`, [id]);
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
    React.createElement(
      "div",
      { className: "lore-input-wrap" },
      icon &&
        React.createElement("span", { className: "lore-input__icon" },
          React.createElement("i", { "data-lucide": icon })),
      React.createElement("input", {
        id: fid,
        className: [
          "lore-input",
          icon && "lore-input--has-icon",
          error && "lore-input--error",
          className,
        ]
          .filter(Boolean)
          .join(" "),
        "aria-invalid": error ? "true" : undefined,
        ...rest,
      }),
    ),
    error
      ? React.createElement("span", { className: "lore-field__error" }, error)
      : hint && React.createElement("span", { className: "lore-field__hint" }, hint),
  );
}
