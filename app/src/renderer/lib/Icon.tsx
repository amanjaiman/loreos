import { createElement, type ReactNode } from 'react';
import { icons } from 'lucide';

// Renders a Lucide icon as inline SVG, straight from the icon data.
//
// This used to render an `<i data-lucide>` and let `window.lucide.createIcons()` swap it
// for an SVG in an effect — the pattern the vendored design system uses. That is fine for
// a handful of icons and pathological for a list: `createIcons()` runs
// `document.querySelectorAll('[data-lucide]')` over the WHOLE document and mutates the
// DOM out from under React, and the effect had no dependency array, so every mounted icon
// re-ran it on every render. Timeline renders 200 decision rows with an icon each, so one
// render pass meant 200 full-document scans. That was the lag.
//
// Rendering the SVG directly costs a single array walk per icon, keeps the DOM React's,
// and removes the mutation entirely. The design system's own components still use the
// global `createIcons` path — they render few enough icons for it not to matter.

/** A lucide icon node: `[tag, attributes, children?]`, nestable. */
type IconNode = [
  string,
  Record<string, string | number>,
  IconNode[] | undefined,
];

const ICON_SET = icons as unknown as Record<string, IconNode>;

/** `chevron-right` → `ChevronRight`, the key lucide exports icons under. */
function toPascalCase(name: string): string {
  return name
    .split('-')
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
}

function renderChildren(children: IconNode[] | undefined): ReactNode {
  if (children === undefined) {
    return null;
  }
  return children.map(([tag, attrs, nested], index) =>
    createElement(tag, { key: index, ...attrs }, renderChildren(nested)),
  );
}

export function Icon({
  name,
  size,
  className,
}: {
  name: string;
  size?: number;
  className?: string;
}): JSX.Element | null {
  const node = ICON_SET[toPascalCase(name)];
  if (node === undefined) {
    // An unknown name is a typo, not a runtime condition worth failing a view over.
    return null;
  }
  const [, attrs, children] = node;
  return (
    <svg
      {...attrs}
      width={size ?? attrs['width']}
      height={size ?? attrs['height']}
      className={className}
      aria-hidden="true"
      focusable="false"
    >
      {renderChildren(children)}
    </svg>
  );
}
