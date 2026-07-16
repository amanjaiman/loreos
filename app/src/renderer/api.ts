// api.ts — the renderer's ONE seam to the local API (spec 005), the only behavior
// layer (constitution §3.1). Every view reads and writes through here; nothing else in
// renderer/ constructs a URL or calls fetch (an eslint rule enforces this — acceptance
// criterion 2). Wire fields are snake_case, mirroring the 005 OpenAPI contract; types
// here are the contract surfaces pin to.
//
// There is no auth and no remote surface — the API binds loopback only. A failed
// connection means "Lore isn't running" (LoreOfflineError), which views render as a
// calm offline state rather than an error.

const API_BASE = 'http://127.0.0.1:7842';

// ---- Errors ---------------------------------------------------------------------

/** The local API couldn't be reached — the agent isn't running. */
export class LoreOfflineError extends Error {
  // `cause` is ES2022; the app targets ES2021, so declare it explicitly.
  readonly cause?: unknown;
  constructor(cause?: unknown) {
    super("Lore isn't running");
    this.name = 'LoreOfflineError';
    this.cause = cause;
  }
}

/** A non-2xx response. `error` carries the API's actionable message when present. */
export class ApiError extends Error {
  readonly status: number;
  readonly error: string;
  constructor(status: number, error: string) {
    super(`api ${status}: ${error}`);
    this.name = 'ApiError';
    this.status = status;
    this.error = error;
  }
}

// ---- Wire types -----------------------------------------------------------------

/** A stored memory. `score` is present on search hits; timestamps/metadata when known. */
export interface Memory {
  id: string;
  memory: string;
  score?: number;
  metadata?: Record<string, unknown>;
  created_at?: string;
  updated_at?: string;
}

export interface PagedMemories {
  items: Memory[];
  total: number;
  limit: number;
  offset: number;
}

export interface MemoryResults {
  results: Memory[];
}

/** One distilled memory from an add, with what happened to it. */
export interface AddedMemory {
  id: string;
  memory: string;
  event: string; // ADD | UPDATE | NONE
}

export interface AddedMemories {
  results: AddedMemory[];
}

export interface SearchRequest {
  query: string;
  limit?: number;
  user_id?: string;
  filters?: Record<string, unknown>; // reserved by the contract
}

export interface AddRequest {
  text: string;
  user_id?: string;
  metadata?: Record<string, unknown>;
}

/** One raw capture: the filtered text Lore read and how it read it. */
export interface RawCapture {
  at: string;
  executable: string;
  window_title: string;
  extraction_source: string;
  content_type: string;
  text: string;
}

export interface RecentCaptures {
  items: RawCapture[];
}

/** One activity-log row: what Lore did with a window and why. */
export interface ActivityEntry {
  at: string;
  executable: string;
  window_title: string;
  decision: string; // Captured | Skipped | Filtered
  reason: string;
  observation: string;
  category: string;
}

export interface ActivityFeed {
  items: ActivityEntry[];
}

// ---- v2-001: typed memories, episodes, decisions, staging ------------------------

/** The five memory kinds (closed set) and lifecycle statuses. */
export type MemoryKind =
  | 'identity'
  | 'preference'
  | 'state'
  | 'experience'
  | 'project';
export type MemoryStatus = 'staged' | 'active' | 'archived';

export const MEMORY_KINDS: MemoryKind[] = [
  'identity',
  'preference',
  'state',
  'experience',
  'project',
];

/** The v2-001 typed metadata parsed off a memory; null when the row is not v2-shaped. */
export interface MemoryMeta {
  kind: string;
  status: string;
  confidence: number;
  expires_at: number;
  established_at: number;
  updated_reason: string;
  reinforced: number;
  episodes: string[];
  pinned: boolean;
  user_edited: boolean;
  supersedes: string;
}

/** Parse a memory's metadata into the typed v2 shape (rows without `v` are v1 relics). */
export function metaOf(memory: Memory): MemoryMeta | null {
  const m = memory.metadata;
  if (m === undefined || m['v'] === undefined) {
    return null;
  }
  return {
    kind: typeof m['kind'] === 'string' ? m['kind'] : '',
    status: typeof m['status'] === 'string' ? m['status'] : '',
    confidence: typeof m['confidence'] === 'number' ? m['confidence'] : 0,
    expires_at: typeof m['expires_at'] === 'number' ? m['expires_at'] : 0,
    established_at:
      typeof m['established_at'] === 'number' ? m['established_at'] : 0,
    updated_reason:
      typeof m['updated_reason'] === 'string' ? m['updated_reason'] : '',
    reinforced: typeof m['reinforced'] === 'number' ? m['reinforced'] : 0,
    episodes: Array.isArray(m['episodes'])
      ? m['episodes'].filter((e): e is string => typeof e === 'string')
      : [],
    pinned: m['pinned'] === true,
    user_edited: m['user_edited'] === true,
    supersedes: typeof m['supersedes'] === 'string' ? m['supersedes'] : '',
  };
}

/** One episode: what the distiller saw (provenance for memories). */
export interface Episode {
  id: string;
  started_at: string;
  ended_at: string;
  executables: string[];
  titles: string[];
  samples: string[];
  observation_count: number;
}

export interface Episodes {
  items: Episode[];
}

/** One decision-trail row — the Activity feed's unit. */
export interface Decision {
  at: string;
  episode_id: string;
  action: string; // closed | no_facts | distill_failed | staged | promoted | …
  reason: string;
  statement: string;
  kind: string;
  memory_id: string;
}

export interface Decisions {
  items: Decision[];
}

/** Today's capture economy since UTC midnight, against the promotion budget. */
export interface Economy {
  decisions_today: Record<string, number>;
  promoted_today: number;
  daily_budget: number;
}

/** A user-authority patch: text/pin/kind (any subset). */
export interface MemoryPatch {
  text?: string;
  pinned?: boolean;
  kind?: MemoryKind;
}

/** Config is a free-form JSON document; known blocks are typed, the rest is open. */
export interface LoreConfigShape {
  provider?: {
    type?: string;
    model?: string;
    base_url?: string;
    api_key_ref?: string;
    [key: string]: unknown;
  };
  capture?: {
    enabled?: boolean;
    blocklistApps?: string[];
    blocklistKeywords?: string[];
    [key: string]: unknown;
  };
  memory?: Record<string, unknown>;
  onboarding?: { completed?: boolean; [key: string]: unknown };
  [key: string]: unknown;
}

/** A deep-partial config patch. The API deep-merges objects and replaces scalars/arrays. */
export type ConfigPatch = Record<string, unknown>;

export interface ProviderTestRequest {
  type?: string;
  model?: string;
  base_url?: string;
  api_key_ref?: string;
}

export interface ProviderTestResult {
  ok: boolean;
  model?: string;
  latency_ms?: number;
  error?: string;
  error_kind?: string;
}

export interface SystemStatus {
  version: string;
  api_version: string;
  components: {
    agent: string; // running
    memoryd: string; // ready | starting
    provider: string; // ready | unconfigured
  };
}

export interface LogTail {
  lines: string[];
}

export interface ResetResult {
  deleted: number;
}

export interface MemoryExport {
  exported_at: string;
  count: number;
  memories: Memory[];
}

// ---- Transport ------------------------------------------------------------------

type Query = Record<string, string | number | undefined>;

function withQuery(path: string, query?: Query): string {
  if (query === undefined) {
    return path;
  }
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params.set(key, String(value));
    }
  }
  const qs = params.toString();
  return qs.length > 0 ? `${path}?${qs}` : path;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  query?: Query;
}

async function send(
  path: string,
  options: RequestOptions = {},
): Promise<Response> {
  const { method = 'GET', body, query } = options;
  const init: RequestInit = { method, headers: {} };
  if (body !== undefined) {
    (init.headers as Record<string, string>)['content-type'] =
      'application/json';
    init.body = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetch(API_BASE + withQuery(path, query), init);
  } catch (cause) {
    // A loopback fetch that throws means the connection failed — Lore isn't running.
    throw new LoreOfflineError(cause);
  }
  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response));
  }
  return response;
}

async function errorMessage(response: Response): Promise<string> {
  try {
    const data = (await response.json()) as { error?: unknown };
    if (typeof data.error === 'string') {
      return data.error;
    }
  } catch {
    // non-JSON body; fall through to the status text
  }
  return response.statusText || 'request failed';
}

async function getJson<T>(path: string, query?: Query): Promise<T> {
  const response = await send(path, { query });
  return (await response.json()) as T;
}

async function mutateJson<T>(
  method: string,
  path: string,
  body?: unknown,
): Promise<T> {
  const response = await send(path, { method, body });
  return (await response.json()) as T;
}

// ---- The client -----------------------------------------------------------------

export const api = {
  /** Liveness probe. Throws LoreOfflineError when the agent is down. */
  health: () => getJson<{ status: string }>('/health'),

  // Memories
  listMemories: (params?: {
    limit?: number;
    offset?: number;
    userId?: string;
    kind?: MemoryKind;
    status?: MemoryStatus;
  }) =>
    getJson<PagedMemories>('/memories', {
      limit: params?.limit,
      offset: params?.offset,
      user_id: params?.userId,
      kind: params?.kind,
      status: params?.status,
    }),
  searchMemories: (request: SearchRequest) =>
    mutateJson<MemoryResults>('POST', '/memories/search', request),
  getMemory: (id: string) =>
    getJson<Memory>(`/memories/${encodeURIComponent(id)}`),
  addMemory: (request: AddRequest) =>
    mutateJson<AddedMemories>('POST', '/memories', request),
  updateMemory: (id: string, patch: MemoryPatch) =>
    mutateJson<Memory>('PATCH', `/memories/${encodeURIComponent(id)}`, patch),
  deleteMemory: async (id: string): Promise<void> => {
    await send(`/memories/${encodeURIComponent(id)}`, { method: 'DELETE' });
  },
  confirmMemory: (id: string) =>
    mutateJson<Memory>('POST', `/memories/${encodeURIComponent(id)}/confirm`),

  // v2-001: staging curation, episodes, the decision trail, the day's economy
  promoteStaged: (id: string) =>
    mutateJson<Memory>('POST', `/staging/${encodeURIComponent(id)}/promote`),
  dismissStaged: (id: string) =>
    mutateJson<Memory>('POST', `/staging/${encodeURIComponent(id)}/dismiss`),
  episodes: (limit?: number) => getJson<Episodes>('/episodes', { limit }),
  getEpisode: (id: string) =>
    getJson<Episode>(`/episodes/${encodeURIComponent(id)}`),
  decisions: (limit?: number) => getJson<Decisions>('/decisions', { limit }),
  economy: () => getJson<Economy>('/system/economy'),

  // Local telemetry
  recent: (limit?: number) => getJson<RecentCaptures>('/recent', { limit }),
  activity: (limit?: number) => getJson<ActivityFeed>('/activity', { limit }),

  // Config (secrets are submitted, never returned)
  getConfig: () => getJson<LoreConfigShape>('/config'),
  patchConfig: (patch: ConfigPatch) =>
    mutateJson<LoreConfigShape>('PATCH', '/config', patch),

  // Providers
  testProvider: (request: ProviderTestRequest = {}) =>
    mutateJson<ProviderTestResult>('POST', '/providers/test', request),

  // System
  systemStatus: () => getJson<SystemStatus>('/system/status'),
  systemLog: (lines?: number) => getJson<LogTail>('/system/log', { lines }),
  resetData: () => mutateJson<ResetResult>('DELETE', '/system/data'),

  // Export
  exportJson: () => getJson<MemoryExport>('/export/json'),
  exportMarkdown: async (): Promise<string> => {
    const response = await send('/export/markdown');
    return response.text();
  },

};

export type LoreApi = typeof api;
