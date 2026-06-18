import { useState } from 'react';

import { Button } from '../../design-system';

export function Done({
  listening,
  onFinish,
  onBack,
}: {
  listening: boolean;
  onFinish: () => Promise<void>;
  onBack: () => void;
}): JSX.Element {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const finish = async (): Promise<void> => {
    setSaving(true);
    setError(null);
    try {
      await onFinish();
    } catch {
      setError("Couldn't save your setup. Is Lore running?");
      setSaving(false);
    }
  };

  return (
    <div className="onb-step">
      <div className="onb-eyebrow">// all set</div>
      <h2 className="onb-title">Lore is ready</h2>
      <p className="onb-lede">
        Your model is connected and your boundaries are set.{' '}
        {listening
          ? 'Lore will start keeping your memory warm in the background.'
          : 'Lore is paused for now — turn it on whenever you like.'}
      </p>
      <p className="onb-subtle">
        Next, head to Connect to wire up your AI tools so they can recall what
        Lore keeps.
      </p>

      <div className="onb-actions">
        <Button
          variant="ghost"
          icon="arrow-left"
          onClick={onBack}
          disabled={saving}
        >
          Back
        </Button>
        <Button
          variant="primary"
          iconRight="arrow-right"
          onClick={finish}
          disabled={saving}
        >
          {saving ? 'Saving…' : 'Enter Lore'}
        </Button>
      </div>
      {error !== null && <div className="onb-note onb-note--err">{error}</div>}
    </div>
  );
}
