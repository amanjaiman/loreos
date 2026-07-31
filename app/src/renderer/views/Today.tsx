import { useCallback, useEffect, useState } from 'react';

import {
  api,
  metaOf,
  LoreOfflineError,
  MEMORY_KINDS,
  type Decision,
  type Economy,
  type Memory,
  type MemoryKind,
} from '../api';
import { Button, Spinner } from '../design-system';
import { formatRelative } from '../lib/format';
import { Icon } from '../lib/Icon';
import { useRouter } from '../lib/router';
import './today.css';

interface TodayData {
  economy: Economy;
  decisions: Decision[];
  staged: Memory[];
  /** Active memories, used only for the composition breakdown. */
  active: Memory[];
}

const KIND_LABELS: Record<MemoryKind, string> = {
  identity: 'Identity',
  preference: 'Preference',
  state: 'State',
  experience: 'Experience',
  project: 'Project',
};

/**
 * Home (v2-004). A two-column app pane, not a document:
 *
 *   left  — the work, in priority order. The staged queue leads because it is the only
 *           thing on this screen that requires the user; the day's kept memories follow
 *           as a dense, deliberately recessive record.
 *   right — context you glance at and never operate on. Nothing here is a control, which
 *           is what makes the split legible.
 *
 * Numbers live here and only here; the rail carries capture *state* (v2-004 AC 6).
 */
export function Today(): JSX.Element {
  const [data, setData] = useState<TodayData | null>(null);
  const [offline, setOffline] = useState(false);
  const { navigate } = useRouter();

  const load = useCallback(async (): Promise<void> => {
    try {
      const [economy, decisions, staged, active] = await Promise.all([
        api.economy(),
        api.decisions(100),
        api.listMemories({ status: 'staged', limit: 20 }),
        api.listMemories({ status: 'active', limit: 500 }),
      ]);
      setData({
        economy,
        decisions: decisions.items,
        staged: staged.items,
        active: active.items,
      });
      setOffline(false);
    } catch (e) {
      setOffline(e instanceof LoreOfflineError);
      setData({
        economy: { decisions_today: {}, promoted_today: 0, daily_budget: 0 },
        decisions: [],
        staged: [],
        active: [],
      });
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (data === null) {
    return (
      <div className="app-page today-loading">
        <Spinner size={24} />
      </div>
    );
  }

  const kept = data.decisions.filter(
    (decision) =>
      (decision.action === 'promoted' ||
        decision.action === 'revised' ||
        decision.action === 'user_promoted') &&
      isToday(decision.at),
  );

  const counts = data.economy.decisions_today;
  const episodes = counts['closed'] ?? 0;
  const passed = counts['no_facts'] ?? 0;
  const stagedToday = counts['staged'] ?? 0;

  return (
    <div className="app-page">
      <div className="app-page__head">
        <span className="app-page__crumb">Home</span>
        <div className="app-page__acts">
          <Button
            variant="secondary"
            size="sm"
            icon="search"
            onClick={() => navigate('memory')}
          >
            Search memory
          </Button>
        </div>
      </div>

      {offline ? (
        <section className="today-offline">
          <span className="today-offline__icon">
            <Icon name="moon" size={20} />
          </span>
          <div>
            <strong>Lore is resting.</strong>
            <p>
              The local agent is not responding. This view will fill in when it
              returns.
            </p>
          </div>
          <Button
            variant="ghost"
            size="sm"
            icon="refresh-cw"
            onClick={() => void load()}
          >
            Check again
          </Button>
        </section>
      ) : (
        <div className="today-layout">
          <div className="today-work">
            <section>
              <div className="today-shead">
                <h2>Needs your judgment</h2>
                {data.staged.length > 0 && (
                  <span className="today-chip">{data.staged.length}</span>
                )}
              </div>
              {data.staged.length === 0 ? (
                <p className="app-empty">
                  The staging area is clear. Uncertain memories wait here until
                  more evidence arrives.
                </p>
              ) : (
                <div className="today-queue">
                  {data.staged.map((memory) => (
                    <QueueCard key={memory.id} memory={memory} onActed={load} />
                  ))}
                </div>
              )}
            </section>

            <section className="today-record">
              <div className="today-shead today-shead--quiet">
                <h2>Kept today</h2>
                <span className="today-shead__spacer" />
                <span className="today-shead__count">{kept.length}</span>
              </div>
              {kept.length === 0 ? (
                <p className="app-empty">
                  Nothing new has earned a place yet. Lore is still paying
                  attention.
                </p>
              ) : (
                <div className="today-recs">
                  {kept.map((decision, index) => (
                    <div
                      className="today-rec"
                      key={`${decision.memory_id}-${index}`}
                    >
                      <span className="today-rec__dot" aria-hidden="true" />
                      <span className="today-rec__text">
                        {decision.statement}
                      </span>
                      <span className="today-rec__kind">{decision.kind}</span>
                      <span className="today-rec__time">
                        {formatRelative(decision.at)}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </section>
          </div>

          <aside className="today-ctx" aria-label="Today at a glance">
            <div className="today-ctx__block">
              <span className="today-vlabel">Today</span>
              <dl className="today-dl">
                <div>
                  <dt>Episodes observed</dt>
                  <dd>{episodes}</dd>
                </div>
                <div>
                  <dt>Kept</dt>
                  <dd>{data.economy.promoted_today}</dd>
                </div>
                <div>
                  <dt>Passed over</dt>
                  <dd>{passed}</dd>
                </div>
              </dl>
              <Ratio
                kept={data.economy.promoted_today}
                staged={stagedToday}
                passed={passed}
              />
            </div>

            <div className="today-ctx__rule" />

            <div className="today-ctx__block">
              <span className="today-vlabel">Memory budget</span>
              <dl className="today-dl">
                <div>
                  <dt>Used today</dt>
                  <dd>
                    {data.economy.promoted_today} of {data.economy.daily_budget}
                  </dd>
                </div>
              </dl>
              <div className="today-meter">
                <span
                  style={{
                    width: `${budgetPercent(
                      data.economy.promoted_today,
                      data.economy.daily_budget,
                    )}%`,
                  }}
                />
              </div>
            </div>

            <div className="today-ctx__rule" />

            <Composition memories={data.active} />
          </aside>
        </div>
      )}
    </div>
  );
}

/**
 * The day's selectivity, as a part-to-whole bar. Two hues plus a neutral remainder —
 * "passed over" is grey because it genuinely *is* the context, not a third series. The
 * numbers are direct-labelled above, so nothing is readable only from the colour.
 */
function Ratio({
  kept,
  staged,
  passed,
}: {
  kept: number;
  staged: number;
  passed: number;
}): JSX.Element | null {
  const total = kept + staged + passed;
  if (total === 0) {
    return null;
  }
  const pct = (n: number): string => `${(n / total) * 100}%`;
  return (
    <>
      <div
        className="today-ratio"
        role="img"
        aria-label={`Of ${total} episodes today: ${kept} kept, ${staged} staged, ${passed} passed over`}
      >
        {kept > 0 && (
          <span style={{ width: pct(kept), background: 'var(--viz-1)' }} />
        )}
        {staged > 0 && (
          <span style={{ width: pct(staged), background: 'var(--viz-2)' }} />
        )}
        {passed > 0 && (
          <span style={{ width: pct(passed), background: 'var(--viz-rest)' }} />
        )}
      </div>
      <span className="today-vnote">
        Lore kept {kept} of the {total} episodes it saw today.
      </span>
    </>
  );
}

/** What Lore knows, by kind: magnitude, one hue, every bar direct-labelled. */
function Composition({ memories }: { memories: Memory[] }): JSX.Element {
  const counts = new Map<MemoryKind, number>();
  for (const memory of memories) {
    const meta = metaOf(memory);
    if (meta === null) {
      continue;
    }
    const kind = MEMORY_KINDS.find((k) => k === meta.kind);
    if (kind !== undefined) {
      counts.set(kind, (counts.get(kind) ?? 0) + 1);
    }
  }
  const rows = MEMORY_KINDS.map((kind) => ({
    kind,
    n: counts.get(kind) ?? 0,
  }))
    .filter((row) => row.n > 0)
    .sort((a, b) => b.n - a.n);
  const max = rows.length === 0 ? 0 : rows[0].n;

  return (
    <div className="today-ctx__block">
      <span className="today-vlabel">What Lore knows</span>
      {rows.length === 0 ? (
        <span className="today-vnote">Nothing kept yet.</span>
      ) : (
        <>
          <div className="today-kinds">
            {rows.map(({ kind, n }) => (
              <div className="today-kind" key={kind}>
                <span>{KIND_LABELS[kind]}</span>
                <span className="today-kind__track">
                  <span style={{ width: `${(n / max) * 100}%` }} />
                </span>
                <span className="today-kind__n">{n}</span>
              </div>
            ))}
          </div>
          <span className="today-vnote">
            {memories.length} active{' '}
            {memories.length === 1 ? 'memory' : 'memories'} in total.
          </span>
        </>
      )}
    </div>
  );
}

/**
 * A staged memory, as something you operate on. The evidence line answers the question
 * you actually have when judging one of these — "why does Lore think this?" — which the
 * previous list-row treatment left unanswerable.
 */
function QueueCard({
  memory,
  onActed,
}: {
  memory: Memory;
  onActed: () => Promise<void>;
}): JSX.Element {
  const [busy, setBusy] = useState(false);
  const meta = metaOf(memory);

  const act = async (fn: (id: string) => Promise<unknown>): Promise<void> => {
    setBusy(true);
    try {
      await fn(memory.id);
      await onActed();
    } finally {
      setBusy(false);
    }
  };

  const episodes = meta?.episodes.length ?? 0;
  const evidence = [
    episodes > 0
      ? `Seen in ${episodes} ${episodes === 1 ? 'episode' : 'episodes'}`
      : 'Not yet corroborated',
    meta !== null && meta.established_at > 0
      ? `first noticed ${formatRelative(
          new Date(meta.established_at * 1000).toISOString(),
        )}`
      : null,
  ]
    .filter((part): part is string => part !== null)
    .join(' · ');

  return (
    <article className="today-qcard">
      <span className="today-qcard__mark">
        <span aria-hidden="true" />
        {meta === null
          ? 'Awaiting evidence'
          : `${meta.kind} · awaiting evidence`}
      </span>
      <p>{memory.memory}</p>
      <p className="today-qcard__why">{evidence}</p>
      <div className="today-qcard__foot">
        <Button
          size="sm"
          variant="primary"
          disabled={busy}
          onClick={() => void act(api.promoteStaged)}
        >
          Keep
        </Button>
        <Button
          size="sm"
          variant="ghost"
          disabled={busy}
          onClick={() => void act(api.dismissStaged)}
        >
          Dismiss
        </Button>
      </div>
    </article>
  );
}

function isToday(iso: string): boolean {
  const date = new Date(iso);
  const now = new Date();
  return (
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate()
  );
}

function budgetPercent(used: number, budget: number): number {
  return budget <= 0 ? 0 : Math.min(100, Math.round((used / budget) * 100));
}
