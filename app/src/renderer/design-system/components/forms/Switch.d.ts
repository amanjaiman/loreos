import * as React from "react";

export interface SwitchProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
}

/** On/off toggle (checkbox semantics with role="switch"). */
export function Switch(props: SwitchProps): JSX.Element;
