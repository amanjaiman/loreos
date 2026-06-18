import * as React from "react";

export interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Lucide icon name, e.g. "settings". */
  icon: string;
  /** Accessible label (also used as tooltip title). */
  label: string;
  /** @default "ghost" */
  variant?: "ghost" | "solid";
  /** @default "md" */
  size?: "sm" | "md" | "lg";
}

/** Square, icon-only button. */
export function IconButton(props: IconButtonProps): JSX.Element;
