import { useState } from 'react';

import { Tabs } from '../../design-system';
import { useConfig } from '../../lib/hooks';
import { About } from './About';
import { Appearance } from './Appearance';
import { CapturePrivacy } from './CapturePrivacy';
import { Data } from './Data';
import { ModelMemory } from './ModelMemory';
import './settings.css';

type SettingsTab = 'model' | 'capture' | 'appearance' | 'data' | 'about';

const TABS = [
  { value: 'model', label: 'Model & memory' },
  { value: 'capture', label: 'Capture & privacy' },
  { value: 'appearance', label: 'Appearance' },
  { value: 'data', label: 'Data' },
  { value: 'about', label: 'About' },
];

/**
 * Settings — Model & memory, Capture & privacy, Appearance, Data, About. Every section
 * reads and writes through api.ts; secrets are submitted but never echoed (the key field
 * starts blank). Config is read once here and passed down; sections refresh it on save.
 */
export function Settings(): JSX.Element {
  const [tab, setTab] = useState<SettingsTab>('model');
  const { config, refresh } = useConfig();

  return (
    <div className="app-page">
      <Tabs
        tabs={TABS}
        value={tab}
        onChange={(v) => setTab(v as SettingsTab)}
      />
      {tab === 'model' && (
        <ModelMemory config={config} onSaved={() => void refresh()} />
      )}
      {tab === 'capture' && (
        <CapturePrivacy config={config} onSaved={() => void refresh()} />
      )}
      {tab === 'appearance' && <Appearance />}
      {tab === 'data' && <Data onReset={() => void refresh()} />}
      {tab === 'about' && <About />}
    </div>
  );
}
