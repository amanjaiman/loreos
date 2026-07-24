import { Card } from '../../design-system';

/** Appearance settings: the persistent header control is the single source of truth. */
export function Appearance(): JSX.Element {
  return (
    <Card eyebrow="// appearance" title="Theme">
      <p className="set-subtle">
        Switch between Coastal and Nocturne from the header. Lore remembers
        your choice across restarts.
      </p>
    </Card>
  );
}
