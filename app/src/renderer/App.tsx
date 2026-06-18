import { Badge, Button, Card, Tag, ThemeSelector } from './design-system';

/**
 * T001 smoke surface: proves the vendored Lore Design System renders with its tokens,
 * that a bare app is Coastal, and that the ThemeSelector switches to Nocturne and
 * persists. The real app shell (sidebar + ambient header) lands in T002.
 */
export function App(): JSX.Element {
  return (
    <main className="ds-proof">
      <header className="ds-proof__head">
        <div className="ds-proof__wordmark">
          Lore<span>.</span>
        </div>
        <ThemeSelector />
      </header>

      <Card
        eyebrow="// memory, kept warm"
        title="The design system is live"
        subtitle="Coastal by default. Switch to Nocturne above — the choice persists."
      >
        <p style={{ color: 'var(--text-muted)', margin: '0 0 16px' }}>
          Lore listens in the background and threads what matters into one quiet
          memory, surfaced exactly when you need it. Every screen is built from
          these primitives.
        </p>
        <div className="ds-proof__row">
          <Button variant="primary" icon="sparkles">
            Primary
          </Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="ghost">Ghost</Button>
          <Badge variant="success" dot>
            Listening
          </Badge>
          <Tag>ambient</Tag>
        </div>
      </Card>
    </main>
  );
}
