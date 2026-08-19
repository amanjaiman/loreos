import { useEffect, useRef, useState } from 'react';

import { api, LoreOfflineError, type LoreConfigShape } from '../../api';
import { AutostartToggle } from '../../components/AutostartToggle';
import { ChipListEditor } from '../../components/ChipListEditor';
import { Card, SegmentedControl, Switch } from '../../design-system';
import {
  ATTENTIVENESS_STOPS,
  CERTAINTY_STOPS,
  DETAIL_STOPS,
  attentivenessLine,
  certaintyLine,
  detailLine,
  diagnosticsLine,
  pickStop,
  resolvedCapture,
  retentionLine,
} from '../../lib/capture';

function stringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((v): v is string => typeof v === 'string')
    : [];
}

/** Everything this pane writes, in one object — one save path, one shape. */
interface CaptureForm {
  enabled: boolean;
  attentiveness: string;
  certainty: string;
  detail: string;
  diagnostics: boolean;
  apps: string[];
  keywords: string[];
}

const INITIAL: CaptureForm = {
  enabled: true,
  attentiveness: 'balanced',
  certainty: 'balanced',
  detail: 'balanced',
  diagnostics: false,
  apps: [],
  keywords: [],
};

/** Shown in place of a consequence line when the agent isn't reporting resolved values. */
const NO_RESOLVED = "Lore reports what these settings do while it's running.";

/**
 * Capture & Privacy settings: the capture on/off toggle, the three tuning presets (v2-008
 * R1), the diagnostics switch (R4.2), and the blocklist (apps + keywords). Reads current
 * config to prefill; every toggle/stop/chip action writes immediately through api.ts.
 *
 * The stops and their consequence lines both come from `capture.resolved` — the read-only
 * block of values actually in force — so a raw override in config.json shows the truth and a
 * misspelled preset name shows the stop that is really running. `resolved` is never sent
 * back: the patch below is built from the writable keys only.
 */
export function CapturePrivacy({
  config,
  onSaved,
}: {
  config: LoreConfigShape | null;
  onSaved: () => void;
}): JSX.Element {
  const [form, setForm] = useState<CaptureForm>(INITIAL);
  const [saving, setSaving] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const saveQueue = useRef<Promise<void>>(Promise.resolve());
  const pendingSaves = useRef(0);

  const resolved = resolvedCapture(config);
  const retention = retentionLine(resolved);

  useEffect(() => {
    const capture = config?.capture;
    if (capture === undefined) {
      return;
    }
    // Prefer the resolved values: they are the repaired, effective ones. The raw keys are
    // the fallback for an agent that doesn't report `resolved` at all.
    const effective = resolvedCapture(config);
    setForm({
      enabled: capture.enabled !== false,
      attentiveness: pickStop(
        effective?.attentiveness,
        capture['attentiveness'],
        ATTENTIVENESS_STOPS,
      ),
      certainty: pickStop(
        effective?.certainty,
        capture['certainty'],
        CERTAINTY_STOPS,
      ),
      detail: pickStop(effective?.detail, capture['detail'], DETAIL_STOPS),
      diagnostics: effective?.diagnostics ?? capture['diagnostics'] === true,
      apps: stringArray(capture.blocklistApps),
      keywords: stringArray(capture.blocklistKeywords),
    });
  }, [config]);

  const save = (next: CaptureForm): void => {
    pendingSaves.current += 1;
    setSaving(true);
    setNote(null);
    setError(null);
    saveQueue.current = saveQueue.current.then(async () => {
      try {
        // Writable keys only. `capture.resolved` is derived, read-only state (R3): the
        // agent strips it, and sending it back would pin every preset value as an override.
        await api.patchConfig({
          capture: {
            enabled: next.enabled,
            attentiveness: next.attentiveness,
            certainty: next.certainty,
            detail: next.detail,
            diagnostics: next.diagnostics,
            blocklistApps: next.apps,
            blocklistKeywords: next.keywords,
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

  const change = (patch: Partial<CaptureForm>): void => {
    const next = { ...form, ...patch };
    setForm(next);
    save(next);
  };

  return (
    <div className="set-stack">
      <Card eyebrow="// capture" title="Capture">
        <div className="set-toggle">
          <Switch
            label="Lore is listening"
            checked={form.enabled}
            onChange={(e) => change({ enabled: e.target.checked })}
            disabled={saving}
          />
          <p className="set-subtle">
            {form.enabled
              ? 'Lore watches your active window and threads what matters.'
              : 'Capture is paused. Nothing new is read until you turn it back on.'}
          </p>
        </div>
      </Card>

      {/* The three controls are deliberately not disabled while a save is in flight: they
          are a radio group, and disabling one mid-change drops keyboard focus out of it.
          Writes are serialized by the queue above, so a fast run of changes is safe. */}
      <Card eyebrow="// tuning" title="How Lore watches">
        <div className="set-presets">
          <SegmentedControl
            label="How closely Lore watches"
            options={ATTENTIVENESS_STOPS}
            value={form.attentiveness}
            onChange={(value) => change({ attentiveness: value })}
            hint={attentivenessLine(resolved) ?? NO_RESOLVED}
          />
          <SegmentedControl
            label="How sure Lore has to be"
            options={CERTAINTY_STOPS}
            value={form.certainty}
            onChange={(value) => change({ certainty: value })}
            hint={certaintyLine(resolved) ?? NO_RESOLVED}
          />
          <SegmentedControl
            label="How much detail"
            options={DETAIL_STOPS}
            value={form.detail}
            onChange={(value) => change({ detail: value })}
            hint={detailLine(resolved) ?? NO_RESOLVED}
          />
        </div>
      </Card>

      <Card eyebrow="// troubleshooting" title="Diagnostics">
        <div className="set-toggle">
          <Switch
            label="Record what Lore reads (for troubleshooting)"
            checked={form.diagnostics}
            onChange={(e) => change({ diagnostics: e.target.checked })}
            disabled={saving}
          />
          <p className="set-subtle">{diagnosticsLine(form.diagnostics)}</p>
        </div>
        {retention !== null && <p className="set-subtle">{retention}</p>}
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
          items={form.apps}
          onChange={(apps) => change({ apps })}
        />
        <ChipListEditor
          label="Drop anything containing"
          placeholder="e.g. password"
          items={form.keywords}
          onChange={(keywords) => change({ keywords })}
        />
      </Card>

      {note !== null && <p className="set-note set-note--ok">{note}</p>}
      {error !== null && <p className="set-note set-note--err">{error}</p>}
      {saving && <p className="set-note">Saving…</p>}
    </div>
  );
}
