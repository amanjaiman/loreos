import { type CSSProperties } from 'react';

import { Icon } from '../lib/Icon';
import { useRouter, type Route } from '../lib/router';
import { LiveElement, type AmbientStatus } from './LiveElement';
import { LoreMark } from './LoreMark';
import { ThemeToggle } from './ThemeToggle';

const NAV: Array<{ route: Route; label: string; icon: string }> = [
  { route: 'today', label: 'Home', icon: 'house' },
  { route: 'memory', label: 'Memory', icon: 'layers' },
  { route: 'activity', label: 'Timeline', icon: 'clock' },
  { route: 'settings', label: 'Settings', icon: 'sliders-horizontal' },
];

/**
 * The rail (v2-004): the app's one piece of persistent chrome, sitting on the ground
 * plane to the left of the content card.
 *
 * It is deliberately more than a nav list. Four destinations would not justify 236px on
 * their own — what earns the width is Lore's ambient presence: what it is watching, the
 * pause control, and the trace of what it just kept. The rail reports *state*; the
 * content pane reports *numbers*. Nothing appears in both.
 */
export function Rail({
  status,
  staged,
  recent,
  watching,
  onToggleCapture,
  busy,
}: {
  status: AmbientStatus;
  staged: number;
  recent: string[];
  watching: string | null;
  onToggleCapture: () => void;
  busy: boolean;
}): JSX.Element {
  const { route, navigate } = useRouter();
  // Drives the sliding pill's offset; -1 would be unreachable (every route is in NAV).
  const activeIndex = Math.max(
    0,
    NAV.findIndex((item) => item.route === route),
  );

  return (
    <div className="rail">
      {/* Part of the drag surface: the wordmark row continues the top strip across
          the rail's column, so the whole 44px band moves the window. */}
      <div className="rail__brand">
        <span className="rail__mark" aria-hidden="true">
          <LoreMark />
        </span>
        <span className="rail__word">Lore</span>
      </div>

      <button
        type="button"
        className="rail__search"
        onClick={() => navigate('memory')}
      >
        <Icon name="search" size={15} />
        <span>Search</span>
        <span className="rail__kbd">Ctrl K</span>
      </button>

      <nav
        className="rail__nav"
        aria-label="Primary"
        style={{ '--nav-i': activeIndex } as CSSProperties}
      >
        <span className="nav-ind" aria-hidden="true" />
        {NAV.map(({ route: r, label, icon }) => (
          <button
            key={r}
            type="button"
            className={r === route ? 'navitem navitem--active' : 'navitem'}
            aria-current={r === route ? 'page' : undefined}
            onClick={() => navigate(r)}
          >
            <Icon name={icon} size={17} />
            <span>{label}</span>
            {r === 'memory' && staged > 0 && (
              <span className="navitem__badge">{staged}</span>
            )}
          </button>
        ))}
      </nav>

      <div className="rail__mid" />

      {/* Now first, then what it just kept below — the trace reads as the live element
          continuing downward in time, which only works in that order. */}
      <LiveElement
        status={status}
        watching={watching}
        onToggle={onToggleCapture}
        busy={busy}
      />

      {recent.length > 0 && (
        <div className="trace" aria-label="Recently kept">
          {recent.map((statement, index) => (
            <button
              key={`${index}-${statement.slice(0, 24)}`}
              type="button"
              className="trace__item"
              onClick={() => navigate('activity')}
              title={statement}
            >
              <span className="trace__dot" aria-hidden="true" />
              <span className="trace__text">{statement}</span>
            </button>
          ))}
        </div>
      )}

      <div className="rail__themes">
        <ThemeToggle />
      </div>
    </div>
  );
}
