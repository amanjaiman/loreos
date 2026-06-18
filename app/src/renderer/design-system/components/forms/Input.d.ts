import * as React from "react";

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  /** Field label rendered above the control. */
  label?: string;
  /** Helper text shown below when there is no error. */
  hint?: string;
  /** Error message; turns the field red and hides the hint. */
  error?: string;
  /** Leading Lucide icon name. */
  icon?: string;
  required?: boolean;
}

/** Labelled single-line text field. */
export function Input(props: InputProps): JSX.Element;
