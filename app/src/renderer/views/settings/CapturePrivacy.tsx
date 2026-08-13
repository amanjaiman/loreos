import { useEffect, useRef, useState } from 'react';

import { api, LoreOfflineError, type LoreConfigShape } from '../../api';
import { AutostartToggle } from '../../components/AutostartToggle';
import { ChipListEditor } from '../../components/ChipListEditor';
import { Card, Switch } from '../../design-system';

function stringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((v): v is string => typeof v === 'string')
    : [];
}

/**
 * Capture & Privacy settings: the capture on/off toggle and the blocklist (apps + keywords).
 * Reads current config to prefill; every toggle/chip action writes immediately through api.ts.
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
  const saveQueue = useRef<Promise<void>>(Promise.resolve());
  const pendingSaves = useRef(0);

  useEffect(() => {
    const capture = config?.capture;
    if (capture !== undefined) {
      setEnabled(capture.enabled !== false);
      setApps(stringArray(capture.blocklistApps));
      setKeywords(stringArray(capture.blocklistKeywords));
    }
  }, [config]);

  const save = (
    nextEnabled: boolean,
    nextApps: string[],
    nextKeywords: string[],
  ): void => {
    pendingSaves.current += 1;
    setSaving(true);
    setNote(null);
    setError(null);
    saveQueue.current = saveQueue.current.then(async () => {
      try {
        await api.patchConfig({
          capture: {
            enabled: nextEnabled,
            blocklistApps: nextApps,
            blocklistKeywords: nextKeywords,
          },
        });
        setError(null);
        setNote('Saved.');
      } catch (e) {
        setNote(null);
        setError(
          e instanceof LoreOfflineError
            ? "Lore isn't running."
            : "Couldn't save.",
        );
      } finally {
        pendingSaves.current -= 1;
        if (pendingSaves.current === 0) {
          setSaving(false);
          onSaved();
        }
      }
    });
  };

  const changeEnabled = (next: boolean): void => {
    setEnabled(next);
    save(next, apps, keywords);
  };

  const changeApps = (next: string[]): void => {
    setApps(next);
    save(enabled, next, keywords);
  };

  const changeKeywords = (next: string[]): void => {
    setKeywords(next);
    save(enabled, apps, next);
  };

  return (
    <div className="set-stack">
      <Card eyebrow="// capture" title="Capture">
        <div className="set-toggle">
          <Switch
            label="Lore is listening"
            checked={enabled}
            onChange={(e) => changeEnabled(e.target.checked)}
            disabled={saving}
          />
          <p className="set-subtle">
            {enabled
              ? 'Lore watches your active window and threads what matters.'
              : 'Capture is paused. Nothing new is read until you turn it back on.'}
          </p>
        </div>
      </Card>

      {/* Not part of the capture block above: this writes an OS login item, not agent
          config, so it saves on toggle and has nothing to do with the Save button. */}
      <Card eyebrow="// startup" title="Startup">
        <AutostartToggle surface="settings" />
      </Card>

      <Card eyebrow="// privacy" title="Blocklist">
        <p className="set-subtle">
          Lore never captures these apps, or anything containing these words.
        </p>
        <ChipListEditor
          label="Never capture these apps"
          placeholder="e.g. 1Password"
          items={apps}
          onChange={changeApps}
        />
        <ChipListEditor
          label="Drop anything containing"
          placeholder="e.g. password"
          items={keywords}
          onChange={changeKeywords}
        />
      </Card>

      {note !== null && <p className="set-note set-note--ok">{note}</p>}
      {error !== null && <p className="set-note set-note--err">{error}</p>}
      {saving && <p className="set-note">Saving…</p>}
    </div>
  );
}
