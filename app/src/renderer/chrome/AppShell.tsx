import { useConfig, useSystemStatus } from '../lib/hooks';
import { useRouter } from '../lib/router';
import { Activity } from '../views/Activity';
import { Add } from '../views/Add';
import { Connect } from '../views/Connect';
import { Home } from '../views/Home';
import { Library } from '../views/Library';
import { Settings } from '../views/Settings';
import { Header, type AmbientStatus } from './Header';
import { Sidebar } from './Sidebar';

const VIEWS = {
  home: Home,
  library: Library,
  activity: Activity,
  connect: Connect,
  add: Add,
  settings: Settings,
} as const;

/**
 * Persistent chrome: the left sidebar nav and the sticky ambient header wrapping the
 * routed page. The renderer holds no business logic — the status shown here is a
 * placeholder until T005 polls GET /system/status through api.ts (T003).
 */
export function AppShell(): JSX.Element {
  const { route } = useRouter();
  const View = VIEWS[route];

  // Ambient status: offline if Lore can't be reached, else paused/listening from the
  // capture toggle in config. Both come through api.ts (the renderer holds no logic).
  const { offline } = useSystemStatus();
  const { config } = useConfig();
  const status: AmbientStatus = offline
    ? 'offline'
    : config?.capture?.enabled === false
      ? 'paused'
      : 'listening';

  return (
    <div className="app-shell">
      <Sidebar />
      <div className="app-main">
        <Header route={route} status={status} />
        <main className="app-content">
          <View />
        </main>
      </div>
    </div>
  );
}
