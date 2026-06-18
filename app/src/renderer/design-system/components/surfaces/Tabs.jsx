const React = window.React;

/** Lore Tabs — horizontal tab strip. Controlled via `value` + `onChange`. */
export function Tabs({ tabs = [], value, onChange, className = "" }) {
  const [internal, setInternal] = React.useState(
    value != null ? value : tabs[0] && (tabs[0].value ?? tabs[0]),
  );
  const active = value != null ? value : internal;
  const select = (v) => {
    setInternal(v);
    onChange && onChange(v);
  };
  return React.createElement(
    "div",
    { className: ["lore-tabs", className].filter(Boolean).join(" "), role: "tablist" },
    tabs.map((t) => {
      const tab = typeof t === "string" ? { value: t, label: t } : t;
      const isActive = tab.value === active;
      return React.createElement(
        "button",
        {
          key: tab.value,
          type: "button",
          role: "tab",
          "aria-selected": isActive,
          className: ["lore-tabs__tab", isActive && "lore-tabs__tab--active"].filter(Boolean).join(" "),
          onClick: () => select(tab.value),
        },
        tab.label,
      );
    }),
  );
}
