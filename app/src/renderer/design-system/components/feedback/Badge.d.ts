import * as React from "react";

export interface BadgeProps {
  /** @default "neutral" */
  variant?: "neutral" | "primary" | "accent" | "success" | "warning" | "danger" | "solid";
  /** Show a leading status dot. */
  dot?: boolean;
  className?: string;
  children?: React.ReactNode;
}

/** Small status / category label. */
export function Badge(props: BadgeProps): JSX.Element;
