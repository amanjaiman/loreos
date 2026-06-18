import { Card } from '../../design-system';
import { useSystemStatus } from '../../lib/hooks';

/** About: version and a short, on-brand statement of what Lore is (and isn't). */
export function About(): JSX.Element {
  const { status } = useSystemStatus();

  return (
    <Card eyebrow="// about" title="Lore">
      <p className="set-subtle">
        Memory, kept warm. Lore listens quietly and threads what matters into
        one local memory, surfaced when you need it. No account, no cloud, no
        telemetry — nothing leaves this machine except the model calls you
        configured.
      </p>
      <dl className="set-about">
        <div>
          <dt>// app version</dt>
          <dd>{status?.version ?? '—'}</dd>
        </div>
        <div>
          <dt>// api version</dt>
          <dd>{status?.api_version ?? '—'}</dd>
        </div>
      </dl>
    </Card>
  );
}
