import * as React from "react";

export interface AvatarProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Image URL; falls back to initials when omitted. */
  src?: string;
  /** Full name — used for initials and alt text. */
  name?: string;
  /** @default "md" */
  size?: "sm" | "md" | "lg";
  /** Presence indicator. */
  status?: "online" | "busy" | "away";
}

/** User avatar with image or initials and optional presence dot. */
export function Avatar(props: AvatarProps): JSX.Element;
