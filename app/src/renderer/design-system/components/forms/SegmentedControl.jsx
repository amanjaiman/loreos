const React = window.React;

let _id = 0;

/**
 * Lore SegmentedControl — a small closed set of mutually exclusive stops, shown all at
 * once. Radio-group semantics: the options are native `<input type="radio">` in one named
 * group, so arrow keys move between stops, only the selected stop is in the tab order, and
 * assistive tech announces "3 of 3" without any ARIA bookkeeping of ours. It is deliberately
 * not a row of buttons — a set of buttons has no selected state to announce and no group
 * navigation.
 *
 * Controlled via `value` + `onChange`; pass `defaultValue` (or nothing) to let it own its
 * state, like Tabs. `label` and `hint` render the same field shell Input/Select use — the
 * hint is where a live consequence line belongs, and it is wired to the group with
 * `aria-describedby` so it is read with the control rather than after it.
 */
export function SegmentedControl({
  options = [],
  value,
  defaultValue,
  onChange,
  label,
  hint,
  name,
  disabled = false,
  className = "",
}) {
  const items = options.map((o) => (typeof o === "string" ? { value: o, label: o } : o));
  const [internal, setInternal] = React.useState(
    defaultValue != null ? defaultValue : items[0] && items[0].value,
  );
  const active = value != null ? value : internal;
  // One id per instance: the radio `name` groups the inputs (that is what gives arrow-key
  // navigation), and the label/hint ids hang off it so two controls on one page never collide.
  const gid = React.useMemo(() => name || `lore-segmented-${++_id}`, [name]);

  const select = (v) => {
    setInternal(v);
    onChange && onChange(v);
  };

  return React.createElement(
    "div",
    { className: "lore-field lore-segmented-field" },
    label &&
      React.createElement("span", { className: "lore-field__label", id: `${gid}-label` }, label),
    React.createElement(
      "div",
      {
        className: ["lore-segmented", className].filter(Boolean).join(" "),
        role: "radiogroup",
        "aria-labelledby": label ? `${gid}-label` : undefined,
        "aria-describedby": hint ? `${gid}-hint` : undefined,
      },
      items.map((item) =>
        React.createElement(
          "label",
          { key: item.value, className: "lore-segmented__opt" },
          React.createElement("input", {
            type: "radio",
            name: gid,
            value: item.value,
            checked: item.value === active,
            disabled: disabled || item.disabled === true,
            onChange: () => select(item.value),
          }),
          React.createElement("span", { className: "lore-segmented__label" }, item.label),
        ),
      ),
    ),
    hint && React.createElement("span", { className: "lore-field__hint", id: `${gid}-hint` }, hint),
  );
}
