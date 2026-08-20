// Capture tuning (v2-008 R1): the three preset stops, and the plain-English consequence
// line under each one.
//
// **Every line is derived from `capture.resolved`, never from the preset name.** That block
// is the read-only set of values actually in force, which `GET /config` returns beside the
// writable keys (R3) — preset table, plus any raw value the user typed into config.json,
// which wins for its own field. Writing these sentences against the preset name instead
// would produce a caption that lies to exactly the user who cared enough to open the file.
//
// The stop each control lands on comes from `resolved` too, so a misspelled
// `"attentivenes": "close"` shows `balanced` — the stop actually running — rather than
// nothing selected.

import type { LoreConfigShape } from '../api';
import type { SegmentedOption } from '../design-system';

/** The default stop on all three controls, and the fallback for anything unrecognised. */
export const BALANCED = 'balanced';

export const ATTENTIVENESS_STOPS: SegmentedOption[] = [
  { value: 'light', label: 'Light' },
  { value: 'balanced', label: 'Balanced' },
  { value: 'close', label: 'Close' },
];

export const CERTAINTY_STOPS: SegmentedOption[] = [
  { value: 'strict', label: 'Strict' },
  { value: 'balanced', label: 'Balanced' },
  { value: 'eager', label: 'Eager' },
];

export const DETAIL_STOPS: SegmentedOption[] = [
  { value: 'minimal', label: 'Minimal' },
  { value: 'balanced', label: 'Balanced' },
  { value: 'rich', label: 'Rich' },
];

/** `capture.resolved`, parsed. Every field is what is actually in force. */
export interface ResolvedCapture {
  attentiveness: string;
  certainty: string;
  detail: string;
  /** Seconds. */
  poll: number;
  dwell: number;
  recapture: number;
  titleRecapture: number;
  statementMaxChars: number;
  sampleMaxChars: number;
  maxObservations: number;
  /** Seconds — the clock bound on an episode, alongside the observation cap. */
  maxAge: number;
  highSignalConfidence: number;
  stagedTtlDays: number;
  retentionDays: number;
  diagnostics: boolean;
}

/**
 * Parse `capture.resolved` off a config document. Null when the block is absent — an agent
 * that isn't running, or one built without the capture pipeline. Callers render nothing
 * rather than guessing: a made-up consequence line is worse than none.
 */
export function resolvedCapture(
  config: LoreConfigShape | null,
): ResolvedCapture | null {
  const resolved = config?.capture?.['resolved'];
  if (typeof resolved !== 'object' || resolved === null) {
    return null;
  }
  const r = resolved as Record<string, unknown>;
  const episodes = object(r['episodes']);
  const lifecycle = object(r['lifecycle']);
  return {
    attentiveness: text(r['attentiveness']),
    certainty: text(r['certainty']),
    detail: text(r['detail']),
    poll: seconds(r['pollInterval']),
    dwell: seconds(r['dwellThreshold']),
    recapture: seconds(r['recaptureInterval']),
    titleRecapture: seconds(r['titleRecaptureInterval']),
    statementMaxChars: number(r['statementMaxChars']),
    sampleMaxChars: number(episodes['sampleMaxChars']),
    maxObservations: number(episodes['maxObservations']),
    maxAge: seconds(episodes['maxAge']),
    highSignalConfidence: number(lifecycle['highSignalConfidence']),
    stagedTtlDays: number(lifecycle['stagedTtlDays']),
    retentionDays: number(r['retentionDays']),
    diagnostics: r['diagnostics'] === true,
  };
}

/**
 * Which stop a control shows: the resolved (already repaired) name when the agent reports
 * one, the raw config value when it doesn't, and balanced when neither is a known stop.
 */
export function pickStop(
  resolvedName: string | undefined,
  rawName: unknown,
  stops: SegmentedOption[],
): string {
  const known = (v: unknown): v is string =>
    typeof v === 'string' &&
    stops.some((s) => s.value === v.trim().toLowerCase());
  if (known(resolvedName)) {
    return resolvedName.trim().toLowerCase();
  }
  if (known(rawName)) {
    return rawName.trim().toLowerCase();
  }
  return BALANCED;
}

// ---- The consequence lines ------------------------------------------------------

/** What retention does to the evidence behind memories. Reads `retentionDays` (R4.1). */
export function retentionLine(r: ResolvedCapture | null): string | null {
  if (r === null || !finite(r.retentionDays)) {
    return null;
  }
  return r.retentionDays > 0
    ? `Lore keeps the episodes behind your memories for ${r.retentionDays} ${plural(r.retentionDays, 'day')}, then deletes them. The memories themselves stay.`
    : 'Lore keeps the episodes behind your memories indefinitely. Set capture.retentionDays in config.json to prune them.';
}

/** What the diagnostics switch is doing right now. Bounds are fixed in the agent (R4.2). */
export function diagnosticsLine(on: boolean): string {
  return on
    ? 'Lore keeps the last 24 hours of what it read — up to 500 readings — on this machine. Turn it off once you have your answer.'
    : "Lore doesn't keep a copy of what it reads. Turn this on only while working out why an app isn't being captured.";
}

// ---- Parsing and phrasing -------------------------------------------------------

/**
 * A .NET TimeSpan as `resolved` writes it — `"hh:mm:ss"`, or `"d.hh:mm:ss.fffffff"` once a
 * value passes a day. NaN for anything else, which reads as "don't claim this".
 */
export function timeSpanSeconds(value: unknown): number {
  if (typeof value !== 'string') {
    return Number.NaN;
  }
  const parts = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2}(?:\.\d+)?)$/.exec(
    value.trim(),
  );
  if (parts === null) {
    return Number.NaN;
  }
  return (
    Number(parts[1] ?? 0) * 86400 +
    Number(parts[2]) * 3600 +
    Number(parts[3]) * 60 +
    Number(parts[4])
  );
}

/** "25 seconds", "1 minute", "1 minute 30 seconds" — never a bare number. */
export function duration(totalSeconds: number): string {
  const s = Math.round(totalSeconds);
  if (s < 60) {
    return `${s} ${plural(s, 'second')}`;
  }
  const minutes = Math.floor(s / 60);
  const rest = s % 60;
  const head = `${minutes} ${plural(minutes, 'minute')}`;
  return rest === 0 ? head : `${head} ${rest} ${plural(rest, 'second')}`;
}

function plural(n: number, word: string): string {
  return n === 1 ? word : `${word}s`;
}

function finite(...values: number[]): boolean {
  return values.every((v) => Number.isFinite(v));
}

function seconds(value: unknown): number {
  return timeSpanSeconds(value);
}

function number(value: unknown): number {
  return typeof value === 'number' ? value : Number.NaN;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function object(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null
    ? (value as Record<string, unknown>)
    : {};
}
