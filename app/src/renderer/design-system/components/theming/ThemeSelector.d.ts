import * as React from "react";

export interface LoreTheme {
  /** Stable id, also the persisted value. */
  id: string;
  /** Label shown in the control. */
  label: string;
  /** data-palette value: "1" | "2" | "3". */
  palette: string;
  /** data-style value: "techy" | "minimal". */
  style: string;
  /** data-mode value: "light" | "dark". */
  mode?: string;
  /** Two hex colors for the two-tone swatch dot. */
  dots?: [string, string];
}

/** The two themes bundled with Lore: Coastal (light) and Nocturne (dark). */
export const LORE_THEMES: LoreTheme[];

/** Imperatively apply a theme to an element (defaults to document.documentElement). */
export function applyLoreTheme(theme: LoreTheme, target?: HTMLElement): void;

/**
 * Segmented control that switches and persists the active Lore theme.
 */
export interface ThemeSelectorProps {
  /** Themes to offer. @default LORE_THEMES (Coastal, Nocturne) */
  themes?: LoreTheme[];
  /** Element to theme. @default document.documentElement */
  target?: HTMLElement;
  /** localStorage key for the saved choice. @default "lore-theme-preset" */
  storageKey?: string;
  /** Initial theme id when nothing is saved. @default first theme */
  defaultId?: string;
  /** Called with the theme on every change. */
  onChange?: (theme: LoreTheme) => void;
  className?: string;
}

export function ThemeSelector(props: ThemeSelectorProps): JSX.Element;
