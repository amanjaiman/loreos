import { useEffect, useState } from 'react';

import {
  api,
  LoreOfflineError,
  type LoreConfigShape,
  type ProviderTestResult,
} from '../../api';
import { Button, Card, Input, Select, Spinner } from '../../design-system';
import {
  MODEL_PLACEHOLDER,
  PROVIDER_OPTIONS,
  keyOptional,
  needsBaseUrl,
  saveAndTestProvider,
  type ProviderInput,
  type ProviderType,
} from '../../lib/provider';

const PROVIDER_TYPES: ProviderType[] = [
  'anthropic',
  'openai',
  'gemini',
  'openai_compatible',
];

function asProviderType(value: unknown): ProviderType {
  return typeof value === 'string' &&
    (PROVIDER_TYPES as string[]).includes(value)
    ? (value as ProviderType)
    : 'anthropic';
}

/**
 * Model & Memory settings: the provider (with a live test) and the memory engine. Reads
 * current config to prefill; the API key is never returned, so its field starts empty and
 * is only sent when the user types a new one. All writes go through api.ts.
 */
export function ModelMemory({
  config,
  onSaved,
}: {
  config: LoreConfigShape | null;
  onSaved: () => void;
}): JSX.Element {
  return (
    <div className="set-stack">
      <ProviderSection config={config} onSaved={onSaved} />
      <MemorySection config={config} onSaved={onSaved} />
    </div>
  );
}

function ProviderSection({
  config,
  onSaved,
}: {
  config: LoreConfigShape | null;
  onSaved: () => void;
}): JSX.Element {
  const [input, setInput] = useState<ProviderInput>({
    type: 'anthropic',
    model: '',
    baseUrl: '',
    apiKey: '',
  });
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<ProviderTestResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (config?.provider !== undefined) {
      setInput({
        type: asProviderType(config.provider.type),
        model:
          typeof config.provider.model === 'string'
            ? config.provider.model
            : '',
        baseUrl:
          typeof config.provider.base_url === 'string'
            ? config.provider.base_url
            : '',
        apiKey: '',
      });
    }
  }, [config]);

  const edit = (patch: Partial<ProviderInput>): void => {
    setInput((i) => ({ ...i, ...patch }));
    setResult(null);
    setError(null);
  };

  const hasStoredKey =
    typeof config?.provider?.api_key_ref === 'string' &&
    config.provider.api_key_ref.length > 0;

  const save = async (): Promise<void> => {
    setTesting(true);
    setResult(null);
    setError(null);
    try {
      const r = await saveAndTestProvider(input);
      setResult(r);
      if (r.ok) {
        setInput((i) => ({ ...i, apiKey: '' }));
        onSaved();
      } else {
        setError(r.error ?? 'The model could not be reached.');
      }
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running."
          : 'Something went wrong saving the provider.',
      );
    } finally {
      setTesting(false);
    }
  };

  const ready =
    input.model.trim().length > 0 &&
    (!needsBaseUrl(input.type) || (input.baseUrl ?? '').trim().length > 0);

  return (
    <Card eyebrow="// your model" title="Model">
      <Select
        label="Provider"
        value={input.type}
        onChange={(e) => edit({ type: e.target.value as ProviderType })}
        options={PROVIDER_OPTIONS}
      />
      <Input
        label="Model"
        placeholder={MODEL_PLACEHOLDER[input.type]}
        value={input.model}
        onChange={(e) => edit({ model: e.target.value })}
      />
      {needsBaseUrl(input.type) && (
        <Input
          label="Base URL"
          placeholder="http://localhost:11434/v1"
          value={input.baseUrl ?? ''}
          onChange={(e) => edit({ baseUrl: e.target.value })}
        />
      )}
      <Input
        label="API key"
        type="password"
        placeholder={
          hasStoredKey ? '•••••••• — leave blank to keep current key' : 'sk-…'
        }
        hint={
          keyOptional(input.type) ? 'Optional for a local endpoint.' : undefined
        }
        value={input.apiKey ?? ''}
        onChange={(e) => edit({ apiKey: e.target.value })}
      />

      {testing && (
        <p className="set-note">
          <Spinner size={16} /> Saving and testing…
        </p>
      )}
      {result?.ok === true && (
        <p className="set-note set-note--ok">
          Saved — {result.model}{' '}
          {result.latency_ms !== undefined && (
            <span className="set-mono">latency → {result.latency_ms}ms</span>
          )}
        </p>
      )}
      {error !== null && <p className="set-note set-note--err">{error}</p>}

      <div className="set-actions">
        <Button variant="primary" onClick={save} disabled={!ready || testing}>
          {testing ? 'Saving…' : 'Save & test'}
        </Button>
      </div>
    </Card>
  );
}

function MemorySection({
  config,
  onSaved,
}: {
  config: LoreConfigShape | null;
  onSaved: () => void;
}): JSX.Element {
  const [engine, setEngine] = useState('embedded');
  const [remoteUrl, setRemoteUrl] = useState('');
  const [saving, setSaving] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const memory = config?.memory as Record<string, unknown> | undefined;
    if (memory !== undefined) {
      setEngine(
        typeof memory['engine'] === 'string'
          ? (memory['engine'] as string)
          : 'embedded',
      );
      setRemoteUrl(
        typeof memory['remote_url'] === 'string'
          ? (memory['remote_url'] as string)
          : '',
      );
    }
  }, [config]);

  const save = async (): Promise<void> => {
    setSaving(true);
    setNote(null);
    setError(null);
    try {
      await api.patchConfig({
        memory: {
          engine,
          remote_url: engine === 'remote' ? remoteUrl.trim() : '',
        },
      });
      setNote(
        'Saved. Restart Lore for the memory engine change to take effect.',
      );
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

  const ready = engine !== 'remote' || remoteUrl.trim().length > 0;

  return (
    <Card eyebrow="// memory engine" title="Memory">
      <Select
        label="Engine"
        value={engine}
        onChange={(e) => setEngine(e.target.value)}
        options={[
          { value: 'embedded', label: 'Embedded — runs locally with Lore' },
          { value: 'remote', label: 'Remote — a mem0 server you host' },
        ]}
      />
      {engine === 'remote' && (
        <Input
          label="Remote URL"
          placeholder="http://127.0.0.1:8000"
          value={remoteUrl}
          onChange={(e) => setRemoteUrl(e.target.value)}
        />
      )}
      {note !== null && <p className="set-note set-note--ok">{note}</p>}
      {error !== null && <p className="set-note set-note--err">{error}</p>}
      <div className="set-actions">
        <Button variant="primary" onClick={save} disabled={!ready || saving}>
          {saving ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </Card>
  );
}
