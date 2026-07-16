import { Badge } from '../design-system';
import { useRouter, type Route } from '../lib/router';

/** Ambient capture state, surfaced in the header so it's legible at a glance. */
export type AmbientStatus = 'listening' | 'paused' | 'offline';

const NAV: Array<{ route: Route; label: string }> = [
  { route: 'today', label: 'Today' },
  { route: 'memory', label: 'Memory' },
  { route: 'activity', label: 'Activity' },
  { route: 'settings', label: 'Settings' },
];

const STATUS: Record<
  AmbientStatus,
  { label: string; variant: 'success' | 'neutral' | 'warning' }
> = {
  listening: { label: 'Lore is listening', variant: 'success' },
  paused: { label: 'Lore is paused', variant: 'neutral' },
  offline: { label: "Lore isn't running", variant: 'warning' },
};

/**
 * The one piece of persistent chrome (v2-003): a sticky bar on the design system's
 * blurred ground carrying the wordmark, the tab navigation, and the ambient status.
 * There is no sidebar — every screen is a single centered column beneath this bar.
 */
export function Header({ status }: { status: AmbientStatus }): JSX.Element {
  const { route, navigate } = useRouter();
  const s = STATUS[status];
  return (
    <header className="app-header">
      <div className="app-header__brand" aria-hidden="true">
        <span className="app-header__wordmark">
          Lore<span>.</span>
        </span>
      </div>
      <nav className="app-header__nav" aria-label="Primary">
        {NAV.map(({ route: r, label }) => (
          <button
            key={r}
            type="button"
            className={r === route ? 'app-tab app-tab--active' : 'app-tab'}
            aria-current={r === route ? 'page' : undefined}
            onClick={() => navigate(r)}
          >
            {label}
          </button>
        ))}
      </nav>
      <Badge variant={s.variant} dot>
        {s.label}
      </Badge>
    </header>
  );
}
