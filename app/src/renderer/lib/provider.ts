import { api, type ProviderTestResult } from '../api';

// Provider config helpers shared by onboarding (T004) and Settings (T010). The raw key
// is submitted to 005 to be stored in the OS keystore and is never retained in renderer
// state (constitution §4.2; spec 010 non-functional requirement: no secrets in renderer).

export type ProviderType =
  | 'anthropic'
  | 'openai'
  | 'gemini'
  | 'openai_compatible';

export interface ProviderInput {
  type: ProviderType;
  model: string;
  /** Required for openai_compatible; ignored otherwise. */
  baseUrl?: string;
  /** Raw key, submitted once to be stored. Empty for a keyless local endpoint. */
  apiKey?: string;
}

export const PROVIDER_OPTIONS: { value: ProviderType; label: string }[] = [
  { value: 'anthropic', label: 'Anthropic (Claude)' },
  { value: 'openai', label: 'OpenAI' },
  { value: 'gemini', label: 'Google Gemini' },
  { value: 'openai_compatible', label: 'OpenAI-compatible (local or URL)' },
];

export const MODEL_PLACEHOLDER: Record<ProviderType, string> = {
  anthropic: 'claude-haiku-4-5',
  openai: 'gpt-4o-mini',
  gemini: 'gemini-2.5-flash',
  openai_compatible: 'qwen3:8b',
};

export function needsBaseUrl(type: ProviderType): boolean {
  return type === 'openai_compatible';
}

export function keyOptional(type: ProviderType): boolean {
  return type === 'openai_compatible';
}

/**
 * Save the provider config — relocating any key into the keystore via 005 — then test
 * it. The test request carries the full provider config (the agent reads ProviderOptions
 * once at startup, so a fresh config must be passed explicitly, not left to fall back to
 * the running value). Returns the test result for the UI to render.
 */
export async function saveAndTestProvider(
  input: ProviderInput,
): Promise<ProviderTestResult> {
  const providerPatch: Record<string, unknown> = {
    type: input.type,
    model: input.model.trim(),
  };
  if (needsBaseUrl(input.type)) {
    providerPatch.base_url = (input.baseUrl ?? '').trim();
  }
  const key = (input.apiKey ?? '').trim();
  if (key.length > 0) {
    providerPatch.api_key = key; // 005 moves this into the keystore, returns a handle
  }

  const config = await api.patchConfig({ provider: providerPatch });
  const apiKeyRef = config.provider?.api_key_ref ?? '';

  return api.testProvider({
    type: input.type,
    model: input.model.trim(),
    base_url: needsBaseUrl(input.type)
      ? (input.baseUrl ?? '').trim()
      : undefined,
    api_key_ref: apiKeyRef,
  });
}
