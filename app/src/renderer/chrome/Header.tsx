import { Badge } from '../design-system';
import { type Route } from '../lib/router';

/** Ambient capture state, surfaced in the header so it's legible at a glance. */
export type AmbientStatus = 'listening' | 'paused' | 'offline';

const TITLES: Record<Route, string> = {
  home: 'Home',
  library: 'Library',
  activity: 'Activity',
  connect: 'Connect',
  add: 'Add',
  settings: 'Settings',
};

const STATUS: Record<
  AmbientStatus,
  { label: string; variant: 'success' | 'neutral' | 'warning' }
> = {
  listening: { label: 'Lore is listening', variant: 'success' },
  paused: { label: 'Lore is paused', variant: 'neutral' },
  offline: { label: "Lore isn't running", variant: 'warning' },
};

/**
 * The sticky header with the design system's blurred ground. Carries the current page
 * name and the ambient "listening / paused" indicator. The status is supplied by the
 * shell; T005 wires it to GET /system/status, and T011 surfaces the offline state.
 */
export function Header({
  route,
  status,
}: {
  route: Route;
  status: AmbientStatus;
}): JSX.Element {
  const s = STATUS[status];
  return (
    <header className="app-header">
      <h1 className="app-header__title">{TITLES[route]}</h1>
      <Badge variant={s.variant} dot>
        {s.label}
      </Badge>
    </header>
  );
}
