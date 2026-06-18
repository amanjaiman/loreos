import * as React from "react";

export interface SpinnerProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Diameter in px. @default 20 */
  size?: number;
}

/** Indeterminate loading spinner. */
export function Spinner(props: SpinnerProps): JSX.Element;
