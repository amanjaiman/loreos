import { Button } from '../../design-system';

/**
 * The trust-first explainer. It renders before any input is requested (acceptance
 * criterion 3): plain language about what Lore reads, the three filter layers, that
 * everything is local, and the single egress (the user's own model).
 */
export function Privacy({
  onNext,
  onBack,
}: {
  onNext: () => void;
  onBack: () => void;
}): JSX.Element {
  return (
    <div className="onb-step">
      <div className="onb-eyebrow">// how lore works</div>
      <h2 className="onb-title">What Lore sees, and what it never does</h2>
      <ul className="onb-list">
        <li>
          <strong>What it reads.</strong> Only the text of your active window,
          while you work — never your keystrokes, never the screen itself.
        </li>
        <li>
          <strong>Three filters, before anything is kept.</strong> Your
          blocklist of apps and keywords, a sensitivity check that drops
          secrets, and a relevance gate — so noise and private things are
          forgotten, not stored.
        </li>
        <li>
          <strong>It stays here.</strong> Memories live in a local store on this
          machine. There is no account, no sync, and no telemetry.
        </li>
        <li>
          <strong>One door out.</strong> The only thing that ever leaves is the
          call to the model you choose next — your key, your endpoint.
        </li>
      </ul>
      <div className="onb-actions">
        <Button variant="ghost" icon="arrow-left" onClick={onBack}>
          Back
        </Button>
        <Button variant="primary" iconRight="arrow-right" onClick={onNext}>
          This sounds right
        </Button>
      </div>
    </div>
  );
}
