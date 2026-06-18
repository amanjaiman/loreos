import * as React from "react";

export interface CheckboxProps extends React.InputHTMLAttributes<HTMLInputElement> {
  /** Text shown next to the box. */
  label?: string;
}

/** Boolean checkbox with label. */
export function Checkbox(props: CheckboxProps): JSX.Element;
