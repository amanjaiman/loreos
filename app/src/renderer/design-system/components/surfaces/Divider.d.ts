import * as React from "react";

export interface DividerProps {
  /** Centered label between two rules. */
  label?: string;
  /** Render a vertical separator instead. */
  vertical?: boolean;
  className?: string;
}

/** Horizontal or vertical separator, optionally labelled. */
export function Divider(props: DividerProps): JSX.Element;
