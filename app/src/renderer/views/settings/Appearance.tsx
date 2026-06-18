import { Card, ThemeSelector } from '../../design-system';

/** Appearance settings: the theme switcher. Coastal (default) ⇄ Nocturne; the choice persists. */
export function Appearance(): JSX.Element {
  return (
    <Card eyebrow="// appearance" title="Theme">
      <p className="set-subtle">
        Coastal is calm and light; Nocturne is dark and crisp. Your choice is
        remembered.
      </p>
      <div className="set-theme">
        <ThemeSelector />
      </div>
    </Card>
  );
}
