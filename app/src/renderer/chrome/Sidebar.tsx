import { Icon } from '../lib/Icon';
import { useRouter, type Route } from '../lib/router';

const NAV: { route: Route; label: string; icon: string }[] = [
  { route: 'home', label: 'Home', icon: 'house' },
  { route: 'library', label: 'Library', icon: 'library-big' },
  { route: 'activity', label: 'Activity', icon: 'activity' },
  { route: 'connect', label: 'Connect', icon: 'cable' },
  { route: 'add', label: 'Add', icon: 'plus' },
  { route: 'settings', label: 'Settings', icon: 'settings' },
];

/** The persistent left nav. Brand at the top, the page set below. */
export function Sidebar(): JSX.Element {
  const { route, navigate } = useRouter();
  return (
    <nav className="app-sidebar" aria-label="Primary">
      <div className="app-sidebar__brand">
        <span className="app-sidebar__wordmark">
          Lore<span>.</span>
        </span>
      </div>
      <ul className="app-sidebar__nav">
        {NAV.map((item) => (
          <li key={item.route}>
            <button
              type="button"
              className={
                'app-nav__item' +
                (route === item.route ? ' app-nav__item--active' : '')
              }
              aria-current={route === item.route ? 'page' : undefined}
              onClick={() => navigate(item.route)}
            >
              <Icon name={item.icon} size={18} />
              <span>{item.label}</span>
            </button>
          </li>
        ))}
      </ul>
    </nav>
  );
}
