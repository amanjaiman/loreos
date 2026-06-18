import { useState } from 'react';

import { LoreOfflineError, type ProviderTestResult } from '../../api';
import { Button, Input, Select, Spinner } from '../../design-system';
import {
  MODEL_PLACEHOLDER,
  PROVIDER_OPTIONS,
  keyOptional,
  needsBaseUrl,
  saveAndTestProvider,
  type ProviderInput,
  type ProviderType,
} from '../../lib/provider';

/**
 * Connect-your-model step: pick a provider, enter a key or local URL, and Test the
 * connection (POST /providers/test via the save-then-test helper). Advancing requires a
 * successful test (acceptance criterion 3). The key is submitted to be stored, then
 * cleared from the field — never retained.
 */
export function ConnectModel({
  value,
  onChange,
  tested,
  onTested,
  onNext,
  onBack,
}: {
  value: ProviderInput;
  onChange: (value: ProviderInput) => void;
  tested: boolean;
  onTested: (ok: boolean) => void;
  onNext: () => void;
  onBack: () => void;
}): JSX.Element {
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<ProviderTestResult | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  // Any edit invalidates a prior successful test.
  const edit = (patch: Partial<ProviderInput>): void => {
    onChange({ ...value, ...patch });
    onTested(false);
    setResult(null);
    setFailure(null);
  };

  const ready =
    value.model.trim().length > 0 &&
    (!needsBaseUrl(value.type) || (value.baseUrl ?? '').trim().length > 0) &&
    (keyOptional(value.type) || (value.apiKey ?? '').trim().length > 0);

  const test = async (): Promise<void> => {
    setTesting(true);
    setFailure(null);
    setResult(null);
    try {
      const r = await saveAndTestProvider(value);
      setResult(r);
      onTested(r.ok);
      if (r.ok) {
        // The key is stored in the keystore now — don't keep it in renderer state.
        onChange({ ...value, apiKey: '' });
      } else {
        setFailure(
          r.error ?? 'The model could not be reached. Check your settings.',
        );
      }
    } catch (e) {
      onTested(false);
      setFailure(
        e instanceof LoreOfflineError
          ? "Lore isn't running — start it and try again."
          : 'Something went wrong testing the connection.',
      );
    } finally {
      setTesting(false);
    }
  };

  return (
    <div className="onb-step">
      <div className="onb-eyebrow">// bring your own model</div>
      <h2 className="onb-title">Connect your model</h2>
      <p className="onb-lede">
        Lore uses the model you choose — your key, or a URL to a model you run.
        It is the only thing Lore ever calls out to.
      </p>

      <Select
        label="Provider"
        value={value.type}
        onChange={(e) => edit({ type: e.target.value as ProviderType })}
        options={PROVIDER_OPTIONS}
      />
      <Input
        label="Model"
        placeholder={MODEL_PLACEHOLDER[value.type]}
        value={value.model}
        onChange={(e) => edit({ model: e.target.value })}
      />
      {needsBaseUrl(value.type) && (
        <Input
          label="Base URL"
          placeholder="http://localhost:11434/v1"
          value={value.baseUrl ?? ''}
          onChange={(e) => edit({ baseUrl: e.target.value })}
        />
      )}
      <Input
        label={
          keyOptional(value.type)
            ? 'API key — optional for a local endpoint'
            : 'API key'
        }
        type="password"
        placeholder="sk-…"
        value={value.apiKey ?? ''}
        onChange={(e) => edit({ apiKey: e.target.value })}
      />

      {testing && (
        <div className="onb-note">
          <Spinner size={16} /> Reaching the model…
        </div>
      )}
      {result?.ok && (
        <div className="onb-note onb-note--ok">
          Connected — {result.model}{' '}
          {result.latency_ms !== undefined && (
            <span className="onb-mono">latency → {result.latency_ms}ms</span>
          )}
        </div>
      )}
      {failure !== null && (
        <div className="onb-note onb-note--err">{failure}</div>
      )}

      <div className="onb-actions">
        <Button variant="ghost" icon="arrow-left" onClick={onBack}>
          Back
        </Button>
        <div className="onb-actions__right">
          <Button
            variant="secondary"
            onClick={test}
            disabled={!ready || testing}
          >
            {testing ? 'Testing…' : 'Test connection'}
          </Button>
          <Button
            variant="primary"
            iconRight="arrow-right"
            onClick={onNext}
            disabled={!tested}
          >
            Continue
          </Button>
        </div>
      </div>
    </div>
  );
}
