import { useEffect } from 'react';

// Renders a Lucide icon the same way the design system does: an <i data-lucide> that
// the global lucide (registered in ds-globals) swaps for an inline SVG after render.
// Icons inherit color via currentColor, so they theme automatically. Use this for
// chrome/app icons; the design-system components render their own icons.
export function Icon({
  name,
  size,
  className,
}: {
  name: string;
  size?: number;
  className?: string;
}): JSX.Element {
  useEffect(() => {
    window.lucide.createIcons();
  });
  const style = size === undefined ? undefined : { width: size, height: size };
  return (
    <i
      data-lucide={name}
      style={style}
      className={className}
      aria-hidden="true"
    />
  );
}
