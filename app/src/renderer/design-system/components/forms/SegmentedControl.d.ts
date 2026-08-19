import * as React from "react";

export interface SegmentedOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface SegmentedControlProps {
  /** Stops as strings or {value,label}. Keep the set small — three or four. */
  options: Array<string | SegmentedOption>;
  /** Controlled selection; omit for uncontrolled. */
  value?: string;
  /** Initial selection when uncontrolled; defaults to the first option. */
  defaultValue?: string;
  onChange?: (value: string) => void;
  /** Field label, rendered above the control and read as the group's name. */
  label?: string;
  /** Text below the control, wired to the group with aria-describedby. */
  hint?: string;
  /** Radio group name; generated when omitted. */
  name?: string;
  disabled?: boolean;
  className?: string;
}

/** Mutually exclusive stops shown side by side (radio-group semantics). */
export function SegmentedControl(props: SegmentedControlProps): JSX.Element;
