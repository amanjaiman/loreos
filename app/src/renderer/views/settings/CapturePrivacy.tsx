import { useEffect, useState } from 'react';

import { api, LoreOfflineError, type LoreConfigShape } from '../../api';
import { ChipListEditor } from '../../components/ChipListEditor';
import { Button, Card, Switch } from '../../design-system';

function stringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((v): v is string => typeof v === 'string')
    : [];
}

/**
 * Capture & Privacy settings: the capture on/off toggle and the blocklist (apps + keywords).
 * Reads current config to prefill; writes the capture block through api.ts.
 */
export function CapturePrivacy({
  config,
  onSaved,
}: {
  config: LoreConfigShape | null;
  onSaved: () => void;
}): JSX.Element {
  const [enabled, setEnabled] = useState(true);
  const [apps, setApps] = useState<string[]>([]);
  const [keywords, setKeywords] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const capture = config?.capture;
    if (capture !== undefined) {
      setEnabled(capture.enabled !== false);
      setApps(stringArray(capture.blocklistApps));
      setKeywords(stringArray(capture.blocklistKeywords));
    }
  }, [config]);

  const save = async (): Promise<void> => {
    setSaving(true);
    setNote(null);
    setError(null);
    try {
      await api.patchConfig({
        capture: { enabled, blocklistApps: apps, blocklistKeywords: keywords },
      });
      setNote('Saved.');
      onSaved();
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running."
          : "Couldn't save.",
      );
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="set-stack">
      <Card eyebrow="// capture" title="Capture">
        <div className="set-toggle">
          <Switch
            label="Lore is listening"
            checked={enabled}
            onChange={(e) => setEnabled(e.target.checked)}
          />
          <p className="set-subtle">
            {enabled
              ? 'Lore watches your active window and threads what matters.'
              : 'Capture is paused. Nothing new is read until you turn it back on.'}
          </p>
        </div>
      </Card>

      <Card eyebrow="// privacy" title="Blocklist">
        <p className="set-subtle">
          Lore never captures these apps, or anything containing these words.
        </p>
        <ChipListEditor
          label="Never capture these apps"
          placeholder="e.g. 1Password"
          items={apps}
          onChange={setApps}
        />
        <ChipListEditor
          label="Drop anything containing"
          placeholder="e.g. password"
          items={keywords}
          onChange={setKeywords}
        />
      </Card>

      {note !== null && <p className="set-note set-note--ok">{note}</p>}
      {error !== null && <p className="set-note set-note--err">{error}</p>}
      <div className="set-actions">
        <Button variant="primary" onClick={save} disabled={saving}>
          {saving ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </div>
  );
}
