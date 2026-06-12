import { createRoot } from 'react-dom/client';

import { App } from './App';
import './index.css';

const container = document.getElementById('root');
if (container === null) {
  throw new Error('renderer: #root element missing from index.html');
}
createRoot(container).render(<App />);
