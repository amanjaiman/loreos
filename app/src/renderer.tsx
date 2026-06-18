// Renderer entry. The first import MUST be ds-globals: it sets window.React and
// window.lucide before any design-system component module evaluates (they read those
// globals at load). ESM evaluates imports in source order, so this ordering holds.
import './renderer/lib/ds-globals';
import './renderer/design-system/styles.css';
import './renderer/app.css';
import './renderer/chrome/chrome.css';

import { createRoot } from 'react-dom/client';

import { App } from './renderer/App';

const container = document.getElementById('root');
if (container === null) {
  throw new Error('renderer: #root element missing from index.html');
}
createRoot(container).render(<App />);
