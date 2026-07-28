import { ThemeSelector } from '../design-system';
import { Icon } from '../lib/Icon';
import { useRouter, type Route } from '../lib/router';
import { LoreMark } from './LoreMark';

/** Ambient capture state, surfaced in the header so it's legible at a glance. */
export type AmbientStatus = 'listening' | 'paused' | 'offline';

const NAV: Array<{ route: Route; label: string }> = [
  { route: 'today', label: 'Home' },
  { route: 'memory', label: 'Memory' },
  { route: 'activity', label: 'Timeline' },
  { route: 'settings', label: 'Settings' },
];

const STATUS: Record<
  AmbientStatus,
  { label: string; detail: string; icon: string }
> = {
  listening: {
    label: 'Listening',
    detail: 'Capture is active',
    icon: 'audio-lines',
  },
  paused: { label: 'Paused', detail: 'Capture is paused', icon: 'pause' },
  offline: {
    label: 'Offline',
    detail: "Lore isn't running",
    icon: 'cloud-off',
  },
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
        <span className="app-header__mark">
          <LoreMark />
        </span>
        <span className="app-header__wordmark">Lore</span>
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
      <div className={`app-header__status app-header__status--${status}`}>
        <span className="app-header__status-icon">
          <Icon name={s.icon} size={14} />
        </span>
        <span className="app-header__status-copy">
          <span className="app-header__status-label">{s.label}</span>
          <span className="app-header__status-detail">{s.detail}</span>
        </span>
      </div>
      <div className="app-header__theme">
        <ThemeSelector />
      </div>
    </header>
  );
}
