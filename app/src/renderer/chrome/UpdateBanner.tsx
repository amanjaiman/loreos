import { useEffect, useState } from 'react';

import { Button } from '../design-system';
import { Icon } from '../lib/Icon';

/**
 * A calm, dismissible prompt shown once an update has downloaded in the background and is
 * ready to apply. Restarting is the user's call: the main process only surfaces this while
 * the window is visible — if the app is in the background it installs silently instead
 * (autoUpdate.ts) — so this never interrupts anyone mid-task. "Later" hides it until the
 * next launch, when the update is simply already in place.
 */
export function UpdateBanner(): JSX.Element | null {
  const [ready, setReady] = useState(false);
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => window.lore.onUpdateReady(() => setReady(true)), []);

  if (!ready || dismissed) {
    return null;
  }

  return (
    <div className="update-banner" role="status">
      <span className="update-banner__glyph">
        <Icon name="sparkles" size={18} />
      </span>
      <div className="update-banner__text">
        <p className="update-banner__title">A new version of Lore is ready</p>
        <p className="update-banner__body">Restart to finish updating.</p>
      </div>
      <div className="update-banner__actions">
        <Button variant="ghost" size="sm" onClick={() => setDismissed(true)}>
          Later
        </Button>
        <Button
          variant="primary"
          size="sm"
          icon="refresh-cw"
          onClick={() => window.lore.installUpdate()}
        >
          Restart now
        </Button>
      </div>
    </div>
  );
}
