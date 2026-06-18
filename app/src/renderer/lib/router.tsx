import {
  createContext,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from 'react';

// A deliberately tiny client-side router. The desktop app is a fixed set of pages with
// no URLs to honor, so a route is just an enum in memory — no history, no dependency.
// Onboarding is gated outside this router (it owns the whole window when shown).

export type Route =
  | 'home'
  | 'library'
  | 'activity'
  | 'connect'
  | 'add'
  | 'settings';

interface RouterValue {
  route: Route;
  navigate: (route: Route) => void;
}

const RouterContext = createContext<RouterValue | null>(null);

export function RouterProvider({
  children,
  initial = 'home',
}: {
  children: ReactNode;
  initial?: Route;
}): JSX.Element {
  const [route, setRoute] = useState<Route>(initial);
  const value = useMemo<RouterValue>(
    () => ({ route, navigate: setRoute }),
    [route],
  );
  return (
    <RouterContext.Provider value={value}>{children}</RouterContext.Provider>
  );
}

export function useRouter(): RouterValue {
  const ctx = useContext(RouterContext);
  if (ctx === null) {
    throw new Error('useRouter must be used within a RouterProvider');
  }
  return ctx;
}
