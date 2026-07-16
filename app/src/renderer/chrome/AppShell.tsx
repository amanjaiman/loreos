import { useConfig, useSystemStatus } from '../lib/hooks';
import { useRouter } from '../lib/router';
import { Activity } from '../views/Activity';
import { Memory } from '../views/Memory';
import { Settings } from '../views/settings/Settings';
import { Today } from '../views/Today';
import { Header, type AmbientStatus } from './Header';

const VIEWS = {
  today: Today,
  memory: Memory,
  activity: Activity,
  settings: Settings,
} as const;

/**
 * The v2-003 shell: one sticky header (wordmark · tabs · ambient status) over a single
 * centered column. No sidebar, no multi-pane dashboards — the app is a quiet place the
 * user visits, not a workspace they live in. The renderer holds no business logic.
 */
export function AppShell(): JSX.Element {
  const { route } = useRouter();
  const View = VIEWS[route];

  const { offline } = useSystemStatus();
  const { config } = useConfig();
  const status: AmbientStatus = offline
    ? 'offline'
    : config?.capture?.enabled === false
      ? 'paused'
      : 'listening';

  return (
    <div className="app-shell">
      <Header status={status} />
      <main className="app-content">
        <View />
      </main>
    </div>
  );
}
