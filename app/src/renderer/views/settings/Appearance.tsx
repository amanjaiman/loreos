import { Card } from '../../design-system';

/** Appearance settings: explains the theme choice. The switcher itself lives in the header. */
export function Appearance(): JSX.Element {
  return (
    <Card eyebrow="// appearance" title="Theme">
      <p className="set-subtle">
        Coastal is calm and light; Coastal Dark is the same room with the lamp
        on — the same type and the same soft edges, only the ground goes dark.
        Switch anytime from the control at the bottom of the rail; your choice
        is remembered.
      </p>
    </Card>
  );
}
