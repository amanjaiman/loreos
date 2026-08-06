import { useEffect, useState } from 'react';

/**
 * The window's minimise / maximise / close buttons, drawn by us.
 *
 * These were the OS's own, via `titleBarOverlay`. That overlay is painted as an opaque
 * rectangle by Windows, and nothing on our side — matching its colour to `--bg`, keeping
 * the ground's gradient clear of it, clamping the card's shadow out of the strip — made
 * it stop reading as a box sitting on top of the app. Owning the buttons removes the
 * seam entirely: they are our elements, on our background, in our tokens.
 *
 * Sizes and order stay conventional (46×32 glyph targets, minimise → maximise → close,
 * red close hover) so nothing has to be relearned.
 */
export function WindowControls(): JSX.Element | null {
  const [maximized, setMaximized] = useState(false);
  const bridge = window.lore;

  useEffect(() => {
    if (bridge === undefined || typeof bridge.onWindowState !== 'function') {
      return undefined;
    }
    void bridge.isMaximized().then(setMaximized);
    return bridge.onWindowState(setMaximized);
  }, [bridge]);

  // In a plain-browser render (tests) there is no window to control.
  if (bridge === undefined || typeof bridge.windowCommand !== 'function') {
    return null;
  }

  return (
    <div className="wincontrols">
      <button
        type="button"
        className="wincontrols__btn"
        aria-label="Minimise"
        onClick={() => bridge.windowCommand('minimize')}
      >
        <svg viewBox="0 0 10 10" aria-hidden="true">
          <path d="M0 5h10" />
        </svg>
      </button>

      <button
        type="button"
        className="wincontrols__btn"
        aria-label={maximized ? 'Restore' : 'Maximise'}
        onClick={() => bridge.windowCommand('toggle-maximize')}
      >
        {maximized ? (
          <svg viewBox="0 0 10 10" aria-hidden="true">
            <path d="M2.5 2.5V0.5h7v7h-2" />
            <rect x="0.5" y="2.5" width="7" height="7" />
          </svg>
        ) : (
          <svg viewBox="0 0 10 10" aria-hidden="true">
            <rect x="0.5" y="0.5" width="9" height="9" />
          </svg>
        )}
      </button>

      <button
        type="button"
        className="wincontrols__btn wincontrols__btn--close"
        aria-label="Close"
        onClick={() => bridge.windowCommand('close')}
      >
        <svg viewBox="0 0 10 10" aria-hidden="true">
          <path d="M0 0l10 10M10 0L0 10" />
        </svg>
      </button>
    </div>
  );
}
