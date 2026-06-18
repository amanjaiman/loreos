import { Card } from '../design-system';

/**
 * Placeholder page body used by the shell until each view is built out in its own task
 * (T004–T010). Keeps navigation real and on-brand while the screens land one by one.
 */
export function PageStub({
  eyebrow,
  title,
  note,
}: {
  eyebrow: string;
  title: string;
  note: string;
}): JSX.Element {
  return (
    <div className="app-page">
      <Card eyebrow={eyebrow} title={title}>
        <p style={{ color: 'var(--text-muted)', margin: 0 }}>{note}</p>
      </Card>
    </div>
  );
}
