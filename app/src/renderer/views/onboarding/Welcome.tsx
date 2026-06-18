import { Button } from '../../design-system';

export function Welcome({ onNext }: { onNext: () => void }): JSX.Element {
  return (
    <div className="onb-step">
      <div className="onb-eyebrow">// memory, kept warm</div>
      <h2 className="onb-title">Welcome to Lore</h2>
      <p className="onb-lede">
        Lore listens quietly in the background and threads what matters into one
        calm memory — surfaced exactly when you need it. No account, no cloud.
        Nothing leaves this machine except the model calls you set up.
      </p>
      <div className="onb-actions onb-actions--end">
        <Button variant="primary" iconRight="arrow-right" onClick={onNext}>
          Get started
        </Button>
      </div>
    </div>
  );
}
