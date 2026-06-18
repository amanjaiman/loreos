const React = window.React;

let _sid = 0;
const Chevron = () =>
  React.createElement(
    "svg",
    { viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: "2", strokeLinecap: "round", strokeLinejoin: "round" },
    React.createElement("path", { d: "m6 9 6 6 6-6" }),
  );

/** Lore Select — labelled native select with a custom chevron. */
export function Select({
  label,
  hint,
  error,
  options = [],
  placeholder,
  required = false,
  id,
  className = "",
  ...rest
}) {
  const fid = React.useMemo(() => id || `lore-sel-${++_sid}`, [id]);
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
      { className: "lore-select" },
      React.createElement(
        "select",
        {
          id: fid,
          className: ["lore-input", error && "lore-input--error", className].filter(Boolean).join(" "),
          ...rest,
        },
        placeholder && React.createElement("option", { value: "", disabled: true }, placeholder),
        options.map((o) => {
          const opt = typeof o === "string" ? { value: o, label: o } : o;
          return React.createElement("option", { key: opt.value, value: opt.value }, opt.label);
        }),
      ),
      React.createElement("span", { className: "lore-select__chevron" }, React.createElement(Chevron)),
    ),
    error
      ? React.createElement("span", { className: "lore-field__error" }, error)
      : hint && React.createElement("span", { className: "lore-field__hint" }, hint),
  );
}
