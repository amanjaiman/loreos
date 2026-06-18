import * as React from "react";

/**
 * Content surface — the default container for grouped content.
 */
export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Small overline above the title (accent-colored). */
  eyebrow?: React.ReactNode;
  title?: React.ReactNode;
  subtitle?: React.ReactNode;
  /** Footer region, divided by a hairline. */
  footer?: React.ReactNode;
  /** Adds hover lift + pointer cursor. */
  interactive?: boolean;
  children?: React.ReactNode;
}

export function Card(props: CardProps): JSX.Element;
