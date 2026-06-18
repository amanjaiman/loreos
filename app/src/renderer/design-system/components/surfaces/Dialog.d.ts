import * as React from "react";

export interface DialogProps {
  /** Controls visibility; nothing renders when false. */
  open: boolean;
  /** Called on overlay click, close button, or Escape. */
  onClose?: () => void;
  title?: React.ReactNode;
  /** Footer actions (e.g. buttons), right-aligned. */
  footer?: React.ReactNode;
  /** Show the × button. @default true */
  showClose?: boolean;
  className?: string;
  children?: React.ReactNode;
}

/** Centered modal dialog with overlay, Escape-to-close, and click-outside. */
export function Dialog(props: DialogProps): JSX.Element | null;
