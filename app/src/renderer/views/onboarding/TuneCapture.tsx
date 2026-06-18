import { ChipListEditor } from '../../components/ChipListEditor';
import { Button, Switch } from '../../design-system';

export function TuneCapture({
  apps,
  keywords,
  setApps,
  setKeywords,
  listening,
  setListening,
  onNext,
  onBack,
}: {
  apps: string[];
  keywords: string[];
  setApps: (apps: string[]) => void;
  setKeywords: (keywords: string[]) => void;
  listening: boolean;
  setListening: (listening: boolean) => void;
  onNext: () => void;
  onBack: () => void;
}): JSX.Element {
  return (
    <div className="onb-step">
      <div className="onb-eyebrow">// tune what lore keeps</div>
      <h2 className="onb-title">Set your boundaries</h2>
      <p className="onb-lede">
        Lore never captures these apps, or anything containing these words.
        Start with sensible defaults and adjust any time in Settings.
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

      <div className="onb-toggle">
        <Switch
          label="Start listening now"
          checked={listening}
          onChange={(e) => setListening(e.target.checked)}
        />
        <p className="onb-subtle">
          {listening
            ? 'Lore will begin watching once setup is done. You can pause it any time.'
            : 'Lore will stay paused until you turn it on.'}
        </p>
      </div>

      <div className="onb-actions">
        <Button variant="ghost" icon="arrow-left" onClick={onBack}>
          Back
        </Button>
        <Button variant="primary" iconRight="arrow-right" onClick={onNext}>
          Continue
        </Button>
      </div>
    </div>
  );
}
