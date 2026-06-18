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
  // TODO(T005): replace with live status from GET /system/status.
  const status: AmbientStatus = 'listening';

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
