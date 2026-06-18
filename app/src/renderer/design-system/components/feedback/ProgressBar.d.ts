import * as React from "react";

export interface ProgressBarProps {
  /** Current value. @default 0 */
  value?: number;
  /** Maximum value. @default 100 */
  max?: number;
  /** Caption above the track. */
  label?: string;
  /** Show the percentage on the right. */
  showValue?: boolean;
  className?: string;
}

/** Determinate progress bar. */
export function ProgressBar(props: ProgressBarProps): JSX.Element;
