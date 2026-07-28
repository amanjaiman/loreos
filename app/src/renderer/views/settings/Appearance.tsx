import { Card } from '../../design-system';

/** Appearance settings: explains the theme choice. The switcher itself lives in the header. */
export function Appearance(): JSX.Element {
  return (
    <Card eyebrow="// appearance" title="Theme">
      <p className="set-subtle">
        Coastal is calm and light; Nocturne is dark and crisp. Switch anytime
        from the theme control in the header — your choice is remembered.
      </p>
    </Card>
  );
}
