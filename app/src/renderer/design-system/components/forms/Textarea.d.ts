import * as React from "react";

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  hint?: string;
  error?: string;
  required?: boolean;
  /** @default 4 */
  rows?: number;
}

/** Multi-line text field. */
export function Textarea(props: TextareaProps): JSX.Element;
