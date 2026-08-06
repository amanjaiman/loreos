import { useState } from 'react';

import { PageHeader } from '../../chrome/PageHeader';
import { useConfig } from '../../lib/hooks';
import { Icon } from '../../lib/Icon';
import { About } from './About';
import { Appearance } from './Appearance';
import { CapturePrivacy } from './CapturePrivacy';
import { Connections } from './Connections';
import { Data } from './Data';
import { ModelMemory } from './ModelMemory';
import './settings.css';

type SettingsTab =
  | 'model'
  | 'capture'
  | 'connections'
  | 'appearance'
  | 'data'
  | 'about';

const TABS = [
  { value: 'model', label: 'Model & memory', icon: 'brain-circuit' },
  { value: 'capture', label: 'Capture & privacy', icon: 'scan-eye' },
  { value: 'connections', label: 'Connections', icon: 'plug-zap' },
  { value: 'appearance', label: 'Appearance', icon: 'palette' },
  { value: 'data', label: 'Data', icon: 'database' },
  { value: 'about', label: 'About', icon: 'info' },
] as const;

/**
 * Settings — Model & memory, Capture & privacy, Appearance, Data, About. Every section
 * reads and writes through api.ts; secrets are submitted but never echoed (the key field
 * starts blank). Config is read once here and passed down; sections refresh it on save.
 */
export function Settings(): JSX.Element {
  const [tab, setTab] = useState<SettingsTab>('model');
  const { config, refresh } = useConfig();

  return (
    <>
      <PageHeader
        eyebrow="// settings"
        title="Make Lore yours."
        description="Configure the local model, set clear boundaries, and choose how Lore meets your tools."
      />
      <div className="app-page">
        <div className="set-layout">
          <nav className="set-nav" aria-label="Settings sections">
            {TABS.map((item) => (
              <button
                key={item.value}
                type="button"
                className={
                  item.value === tab
                    ? 'set-nav__item set-nav__item--active'
                    : 'set-nav__item'
                }
                aria-current={item.value === tab ? 'page' : undefined}
                onClick={() => setTab(item.value)}
              >
                <Icon name={item.icon} size={16} />
                <span>{item.label}</span>
                <Icon name="chevron-right" size={14} />
              </button>
            ))}
          </nav>
          <section className="set-content">
            {tab === 'model' && (
              <ModelMemory config={config} onSaved={() => void refresh()} />
            )}
            {tab === 'capture' && (
              <CapturePrivacy config={config} onSaved={() => void refresh()} />
            )}
            {tab === 'connections' && <Connections />}
            {tab === 'appearance' && <Appearance />}
            {tab === 'data' && <Data onReset={() => void refresh()} />}
            {tab === 'about' && <About />}
          </section>
        </div>
      </div>
    </>
  );
}
