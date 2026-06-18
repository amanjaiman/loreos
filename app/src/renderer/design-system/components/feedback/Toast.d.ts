import * as React from "react";

export interface ToastProps {
  /** @default "info" */
  variant?: "info" | "success" | "warning" | "danger";
  /** Bold first line. */
  title?: string;
  /** Override the default Lucide icon for the variant. */
  icon?: string;
  className?: string;
  children?: React.ReactNode;
}

/** Transient notification surface. */
export function Toast(props: ToastProps): JSX.Element;
