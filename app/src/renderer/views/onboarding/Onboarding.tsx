import { useState } from 'react';

import { api } from '../../api';
import { ProgressBar, ThemeSelector } from '../../design-system';
import { type ProviderInput } from '../../lib/provider';
import { ConnectModel } from './ConnectModel';
import { Done } from './Done';
import { Privacy } from './Privacy';
import { TuneCapture } from './TuneCapture';
import { Welcome } from './Welcome';
import './onboarding.css';

const STEP_LABELS = ['Welcome', 'Privacy', 'Your model', 'Capture', 'Done'];

// Sensible privacy defaults the user can edit. Apps match by executable name; keywords
// are case-insensitive substrings (see the capture blocklist).
const DEFAULT_APPS = ['1Password', 'Bitwarden', 'KeePassXC', 'Authy'];
const DEFAULT_KEYWORDS = [
  'password',
  'secret',
  'api key',
  'credit card',
  'ssn',
];

/**
 * First-run onboarding: Welcome → privacy explainer → connect a model (with a live test)
 * → tune the capture blocklist → done. The privacy explainer precedes any input; on
 * finish it seeds the blocklist and marks onboarding.completed (acceptance criterion 3).
 * No account or login appears anywhere (acceptance criterion 4).
 */
export function Onboarding({
  onComplete,
}: {
  onComplete: () => void;
}): JSX.Element {
  const [step, setStep] = useState(0);
  const [provider, setProvider] = useState<ProviderInput>({
    type: 'anthropic',
    model: '',
    baseUrl: '',
    apiKey: '',
  });
  const [tested, setTested] = useState(false);
  const [apps, setApps] = useState<string[]>(DEFAULT_APPS);
  const [keywords, setKeywords] = useState<string[]>(DEFAULT_KEYWORDS);
  const [listening, setListening] = useState(true);

  const next = (): void =>
    setStep((s) => Math.min(STEP_LABELS.length - 1, s + 1));
  const back = (): void => setStep((s) => Math.max(0, s - 1));

  const finish = async (): Promise<void> => {
    await api.patchConfig({
      capture: {
        enabled: listening,
        blocklistApps: apps,
        blocklistKeywords: keywords,
      },
      onboarding: { completed: true },
    });
    onComplete();
  };

  return (
    <div className="onb">
      <div className="onb__panel">
        <div className="onb__head">
          <span className="onb__wordmark">
            Lore<span>.</span>
          </span>
          <ThemeSelector />
        </div>
        <div className="onb__progress">
          <span className="onb__steplabel">
            Step {step + 1} of {STEP_LABELS.length} · {STEP_LABELS[step]}
          </span>
          <ProgressBar value={(step / (STEP_LABELS.length - 1)) * 100} />
        </div>

        <div className="onb__body">
          {step === 0 && <Welcome onNext={next} />}
          {step === 1 && <Privacy onNext={next} onBack={back} />}
          {step === 2 && (
            <ConnectModel
              value={provider}
              onChange={setProvider}
              tested={tested}
              onTested={setTested}
              onNext={next}
              onBack={back}
            />
          )}
          {step === 3 && (
            <TuneCapture
              apps={apps}
              keywords={keywords}
              setApps={setApps}
              setKeywords={setKeywords}
              listening={listening}
              setListening={setListening}
              onNext={next}
              onBack={back}
            />
          )}
          {step === 4 && (
            <Done listening={listening} onFinish={finish} onBack={back} />
          )}
        </div>
      </div>
    </div>
  );
}
