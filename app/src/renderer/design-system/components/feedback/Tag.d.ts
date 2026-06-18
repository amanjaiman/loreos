import * as React from "react";

export interface TagProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** When provided, renders a remove (×) button that calls this. */
  onRemove?: (e: React.MouseEvent) => void;
  children?: React.ReactNode;
}

/** Chip for filters and multi-select tokens. */
export function Tag(props: TagProps): JSX.Element;
