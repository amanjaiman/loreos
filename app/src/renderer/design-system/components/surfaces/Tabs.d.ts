import * as React from "react";

export interface TabItem {
  value: string;
  label: string;
}

export interface TabsProps {
  /** Tabs as strings or {value,label}. */
  tabs: Array<string | TabItem>;
  /** Controlled active value; omit for uncontrolled. */
  value?: string;
  onChange?: (value: string) => void;
  className?: string;
}

/** Horizontal underline tab strip. */
export function Tabs(props: TabsProps): JSX.Element;
