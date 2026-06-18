import * as React from "react";

/**
 * Primary action control for Lore.
 */
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Visual weight. @default "primary" */
  variant?: "primary" | "secondary" | "outline" | "ghost" | "danger";
  /** Control height. @default "md" */
  size?: "sm" | "md" | "lg";
  /** Leading Lucide icon name, e.g. "sparkles". */
  icon?: string;
  /** Trailing Lucide icon name, e.g. "arrow-right". */
  iconRight?: string;
  /** Stretch to fill the container width. */
  fullWidth?: boolean;
  /** Render as a different element (e.g. "a"). @default "button" */
  as?: "button" | "a";
  children?: React.ReactNode;
}

export function Button(props: ButtonProps): JSX.Element;
