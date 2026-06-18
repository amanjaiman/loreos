import * as React from "react";

export interface TooltipProps {
  /** Text shown on hover / focus. */
  label: string;
  className?: string;
  children?: React.ReactNode;
}

/** CSS hover/focus tooltip positioned above its child. */
export function Tooltip(props: TooltipProps): JSX.Element;
