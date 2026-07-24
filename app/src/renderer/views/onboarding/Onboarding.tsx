import { useState } from 'react';

import { api } from '../../api';
import { LoreMark } from '../../chrome/LoreMark';
import { ProgressBar, ThemeSelector } from '../../design-system';
import { Icon } from '../../lib/Icon';
import { type ProviderInput } from '../../lib/provider';
import { ConnectModel } from './ConnectModel';
import { Done } from './Done';
import { MemoryModel } from './MemoryModel';
import { Privacy } from './Privacy';
import { TuneCapture } from './TuneCapture';
import { Welcome } from './Welcome';
import './onboarding.css';

const STEP_LABELS = [
  'Welcome',
  'Privacy',
  'Your model',
  'Memory',
  'Capture',
  'Done',
];

const DEFAULT_APPS = ['1Password', 'Bitwarden', 'KeePassXC', 'Authy'];
const DEFAULT_KEYWORDS = [
  'password',
  'secret',
  'api key',
  'credit card',
  'ssn',
];

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
    setStep((current) => Math.min(STEP_LABELS.length - 1, current + 1));
  const back = (): void => setStep((current) => Math.max(0, current - 1));

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
        <aside className="onb__rail">
          <div className="onb__brand">
            <span className="onb__mark">
              <LoreMark />
            </span>
            <span className="onb__wordmark">Lore</span>
          </div>
          <div className="onb__rail-copy">
            <span>// ambient memory</span>
            <p>The context your tools have been missing.</p>
          </div>
          <ol className="onb__steps">
            {STEP_LABELS.map((label, index) => (
              <li
                key={label}
                className={
                  index === step
                    ? 'onb__step onb__step--active'
                    : index < step
                      ? 'onb__step onb__step--complete'
                      : 'onb__step'
                }
              >
                <span>
                  {index < step ? <Icon name="check" size={12} /> : index + 1}
                </span>
                <span>{label}</span>
              </li>
            ))}
          </ol>
          <p className="onb__local">
            Local by default. No account. No telemetry.
          </p>
        </aside>

        <main className="onb__main">
          <div className="onb__head">
            <div className="onb__progress">
              <span className="onb__steplabel">
                Step {step + 1} of {STEP_LABELS.length}
              </span>
              <ProgressBar value={(step / (STEP_LABELS.length - 1)) * 100} />
            </div>
            <ThemeSelector />
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
            {step === 3 && <MemoryModel onNext={next} onBack={back} />}
            {step === 4 && (
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
            {step === 5 && (
              <Done listening={listening} onFinish={finish} onBack={back} />
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
