// Globals the vendored design system expects on `window`, established BEFORE any
// component module evaluates. The bundle's components do `const React = window.React`
// at module load and call `window.lucide.createIcons()` after render, so this file
// must be the first import at the renderer entry — ahead of anything that pulls in a
// design-system component (App, views). ESM evaluates imports in source order, so a
// leading `import './lib/ds-globals'` runs to completion first.
//
// Both globals are satisfied locally (no CDN): React is the app's own copy; Lucide is
// the `lucide` npm package, registered with its full icon set so the components'
// `data-lucide` names always resolve. Webfonts are self-hosted via `@fontsource`
// (see design-system/tokens/fonts.css) — nothing here reaches the network
// (constitution §1; spec 010 acceptance criterion 2).

import * as React from 'react';
import { createIcons, icons } from 'lucide';

// Self-hosted webfonts — the families typography.css references by name.
import '@fontsource/manrope/300.css';
import '@fontsource/manrope/400.css';
import '@fontsource/manrope/500.css';
import '@fontsource/manrope/600.css';
import '@fontsource/manrope/700.css';
import '@fontsource/manrope/800.css';
import '@fontsource/space-grotesk/300.css';
import '@fontsource/space-grotesk/400.css';
import '@fontsource/space-grotesk/500.css';
import '@fontsource/space-grotesk/600.css';
import '@fontsource/space-grotesk/700.css';
import '@fontsource/jetbrains-mono/400.css';
import '@fontsource/jetbrains-mono/500.css';
import '@fontsource/jetbrains-mono/600.css';
import '@fontsource/jetbrains-mono/700.css';

declare global {
  interface Window {
    React: typeof React;
    lucide: { createIcons: (options?: Record<string, unknown>) => void };
  }
}

window.React = React;
window.lucide = {
  // The components call createIcons() with no arguments; supply the full icon set
  // so any data-lucide name they use is found, while still allowing overrides.
  createIcons: (options = {}) => createIcons({ icons, ...options }),
};
