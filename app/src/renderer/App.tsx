import { AppShell } from './chrome/AppShell';
import { RouterProvider } from './lib/router';

/**
 * App root: mounts the router and the persistent chrome (sidebar + ambient header).
 * Onboarding gating (shown on first run, before the shell) lands in T004.
 */
export function App(): JSX.Element {
  return (
    <RouterProvider initial="home">
      <AppShell />
    </RouterProvider>
  );
}
