import * as React from "react";

export interface RadioProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
}

/** Single radio button; share a `name` across a group. */
export function Radio(props: RadioProps): JSX.Element;
