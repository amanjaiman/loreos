import * as React from "react";

export interface SelectOption {
  value: string;
  label: string;
}

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  hint?: string;
  error?: string;
  /** Options as strings or {value,label} objects. */
  options: Array<string | SelectOption>;
  /** Disabled first option shown when nothing is selected. */
  placeholder?: string;
  required?: boolean;
}

/** Labelled dropdown built on the native select. */
export function Select(props: SelectProps): JSX.Element;
