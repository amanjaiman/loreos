import { Button } from '../../design-system';

/**
 * The v2-001 memory-model explainer: facts not activity, skeptical by default,
 * everything visible and editable in Memory. Three sentences, per the spec — this is
 * what makes the quiet first days legible instead of worrying.
 */
export function MemoryModel({
  onNext,
  onBack,
}: {
  onNext: () => void;
  onBack: () => void;
}): JSX.Element {
  return (
    <div className="onb-step">
      <div className="onb-eyebrow">// what lore remembers</div>
      <h2 className="onb-title">Facts about you — not a log of your day</h2>
      <ul className="onb-list">
        <li>
          <strong>Durable facts, not activity.</strong> Lore keeps things like
          "recovering from a wisdom tooth extraction" or "visited France" — not
          a diary of every window you opened.
        </li>
        <li>
          <strong>Skeptical by default.</strong> Most of what you do reveals
          nothing durable, and Lore records nothing. A new fact waits in staging
          until a second look confirms it.
        </li>
        <li>
          <strong>Yours to correct.</strong> Everything Lore knows is visible in
          Memory — edit it, pin it, or delete it, and your word always wins.
        </li>
      </ul>
      <div className="onb-actions">
        <Button variant="ghost" icon="arrow-left" onClick={onBack}>
          Back
        </Button>
        <Button variant="primary" iconRight="arrow-right" onClick={onNext}>
          Got it
        </Button>
      </div>
    </div>
  );
}
