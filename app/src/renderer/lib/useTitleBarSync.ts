import { useEffect } from 'react';

// The native window-control overlay (v2-004 T001) has its colours fixed when the window
// is built and does not follow CSS. This hook reports the *resolved* theme colours to the
// main process on mount and on every theme change, so the controls stay legible in both
// themes (AC 4).
//
// Colours are read off the live cascade rather than a hard-coded per-theme map: a future
// theme then works without touching this file, which is the failure mode called out in
// plan.md ("overlay colour drift").

/** `rgb(a)` as returned by getComputedStyle → `#rrggbb`. Alpha is dropped; the overlay is opaque. */
function toHex(computed: string): string | null {
  const parts = computed.match(/\d+(\.\d+)?/g);
  if (parts === null || parts.length < 3) {
    return null;
  }
  const hex = parts
    .slice(0, 3)
    .map((value) => {
      const n = Math.max(0, Math.min(255, Math.round(Number(value))));
      return n.toString(16).padStart(2, '0');
    })
    .join('');
  return `#${hex}`;
}

/**
 * Resolve two custom properties to concrete hex. Reading `getPropertyValue('--bg')`
 * would hand back the unresolved `var(--n-0)` token in some engines, so we let the
 * cascade do the work on a throwaway element and read the computed colours back.
 */
function resolveOverlayColors(): { color: string; symbolColor: string } | null {
  const probe = document.createElement('span');
  probe.setAttribute('aria-hidden', 'true');
  probe.style.cssText =
    'position:absolute;width:0;height:0;opacity:0;pointer-events:none;' +
    'background-color:var(--bg);color:var(--text-muted)';
  document.body.appendChild(probe);
  const computed = getComputedStyle(probe);
  const color = toHex(computed.backgroundColor);
  const symbolColor = toHex(computed.color);
  probe.remove();
  return color !== null && symbolColor !== null ? { color, symbolColor } : null;
}

export function useTitleBarSync(): void {
  useEffect(() => {
    // Guard: the bridge is absent in a plain-browser render (tests, storybook-style use).
    const bridge = window.lore;
    if (bridge === undefined || typeof bridge.setTitleBar !== 'function') {
      return undefined;
    }

    const push = (): void => {
      const colors = resolveOverlayColors();
      if (colors !== null) {
        bridge.setTitleBar(colors.color, colors.symbolColor);
      }
    };

    push();

    // ThemeSelector switches themes by writing data-palette/data-style/data-mode on
    // <html>, so that is the signal to re-read.
    const observer = new MutationObserver(push);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-palette', 'data-style', 'data-mode'],
    });
    return () => observer.disconnect();
  }, []);
}
