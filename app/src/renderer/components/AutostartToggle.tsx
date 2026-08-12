import { useEffect, useState } from 'react';

import { Switch } from '../design-system';

/**
 * Start Lore with Windows (spec v2-006 R2).
 *
 * Unlike every other control in the app, this one does not go through `api.ts`: it is not
 * agent config, it is an OS login item, and it has to work whether or not the agent is
 * running. It reads and writes through the preload bridge, and re-reads the OS after every
 * write, so what the switch shows is what Windows will actually do — a user who removes
 * Lore from Task Manager's Startup tab sees this go off by itself.
 *
 * There is no Save button because there is nothing to save: the write happens on toggle,
 * to the registry, immediately.
 */
export function AutostartToggle({
  surface,
}: {
  /** Which stylesheet's classes to wear — the two hosts style their toggles differently. */
  surface: 'settings' | 'onboarding';
}): JSX.Element | null {
  const [supported, setSupported] = useState<boolean | null>(null);
  const [enabled, setEnabled] = useState(false);
  const [busy, setBusy] = useState(false);

  const bridge = window.lore;
  const prefix = surface === 'settings' ? 'set' : 'onb';

  useEffect(() => {
    if (typeof bridge?.getAutostart !== 'function') {
      setSupported(false);
      return;
    }
    void bridge.getAutostart().then((state) => {
      setSupported(state.supported);
      setEnabled(state.enabled);
    });
  }, [bridge]);

  // Still asking the main process: render nothing rather than flashing a wrong position.
  if (supported === null) {
    return null;
  }

  const change = async (next: boolean): Promise<void> => {
    setBusy(true);
    try {
      // Trust the OS's answer, not the click: if the write failed, the switch snaps back.
      setEnabled(await bridge.setAutostart(next));
    } catch {
      setEnabled(false);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className={`${prefix}-toggle`}>
      <Switch
        label="Start Lore when I sign in"
        checked={supported && enabled}
        disabled={!supported || busy}
        onChange={(e) => void change(e.target.checked)}
      />
      <p className={`${prefix}-subtle`}>
        {!supported
          ? 'Available in the installed app — a dev build has nothing stable to register.'
          : enabled
            ? 'Lore starts quietly in your system tray, so your memory keeps building without you thinking about it.'
            : "Lore only runs when you open it. Anything that happens while it's closed isn't captured."}
      </p>
    </div>
  );
}
