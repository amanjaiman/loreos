import { Icon } from '../lib/Icon';

/** Ambient capture state, surfaced in the rail so it's legible from every screen. */
export type AmbientStatus = 'listening' | 'paused' | 'offline';

const COPY: Record<AmbientStatus, { label: string; detail: string }> = {
  listening: { label: 'Now', detail: 'Watching the active window' },
  paused: { label: 'Paused', detail: 'Capture is off' },
  offline: { label: 'Offline', detail: "Lore isn't running" },
};

/**
 * The rail's ambient presence (v2-004): a slow pulse, what Lore is watching right now,
 * and the capture control.
 *
 * Pause lives here rather than in Settings deliberately. For a capture app this is the
 * most consequential switch in the product, and putting it beside the "I'm watching"
 * indicator makes the privacy promise actionable in one click from anywhere — the
 * constitutional posture (§1), not a convenience.
 *
 * By the same argument the *offline* state carries a control too (v2-006): if the user can
 * stop Lore from the tray, the app has to offer the way back, in the same place and with
 * the same weight as pause/resume. Stopped is otherwise a state you can enter from the UI
 * but only leave from the tray, which is a trap.
 */
export function LiveElement({
  status,
  watching,
  onToggle,
  busy = false,
  onStart,
  starting = false,
}: {
  status: AmbientStatus;
  watching: string | null;
  onToggle: () => void;
  busy?: boolean;
  /** Start a stopped agent. Omitted when this build can't (dev), which hides the control. */
  onStart?: () => void;
  starting?: boolean;
}): JSX.Element {
  const copy = COPY[status];
  const detail =
    status === 'listening' && watching !== null && watching.length > 0
      ? watching
      : status === 'offline' && starting
        ? 'Starting…'
        : copy.detail;

  return (
    <div className={`live live--${status}`}>
      <span className="live__dot" aria-hidden="true" />
      <span className="live__body">
        <span className="live__label">{copy.label}</span>
        <span className="live__detail" title={detail}>
          {detail}
        </span>
      </span>
      {status === 'offline' ? (
        onStart !== undefined && (
          <button
            type="button"
            className="live__pause"
            onClick={onStart}
            disabled={starting}
            aria-label="Start Lore"
            title="Start Lore"
          >
            <Icon name="power" size={13} />
          </button>
        )
      ) : (
        <button
          type="button"
          className="live__pause"
          onClick={onToggle}
          disabled={busy}
          aria-label={status === 'paused' ? 'Resume capture' : 'Pause capture'}
        >
          <Icon name={status === 'paused' ? 'play' : 'pause'} size={13} />
        </button>
      )}
    </div>
  );
}
