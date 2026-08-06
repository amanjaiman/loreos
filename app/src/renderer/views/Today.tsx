import { useCallback, useEffect, useRef, useState } from 'react';

import {
  api,
  metaOf,
  LoreOfflineError,
  MEMORY_KINDS,
  type Decision,
  type Economy,
  type Episode,
  type Memory,
  type MemoryKind,
  type MemoryStats,
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
  stats: MemoryStats;
}

const EMPTY_STATS: MemoryStats = {
  active: {},
  staged: 0,
  archived: 0,
  total_active: 0,
};

/** Keep in step with the `land` / `collapse` keyframes in today.css. */
const LAND_MS = 900;
const COLLAPSE_MS = 340;

/** Honour the OS setting for the two places we *wait* on an animation, not just style it. */
function motionAllowed(): boolean {
  return !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
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
 *   left  — status first (one sentence on the day), then what Lore learned, then the
 *           judgment queue as an expandable inbox. Judgment used to lead as a stack of
 *           large cards; a card says "dwell on this", but this is triage, and a handful
 *           of pending items ended up owning the whole pane.
 *   right — context you glance at and never operate on. Nothing here is a control, which
 *           is what makes the split legible.
 *
 * Numbers live here and only here; the rail carries capture *state* (v2-004 AC 6).
 */
export function Today(): JSX.Element {
  const [data, setData] = useState<TodayData | null>(null);
  const [offline, setOffline] = useState(false);
  const { navigate } = useRouter();

  // The signature moment: a memory *arriving*. Capture is the whole product and it was
  // previously silent. `seen` is null until the first load so the initial paint doesn't
  // animate the entire backlog as if it had all just landed.
  const seen = useRef<Set<string> | null>(null);
  const [fresh, setFresh] = useState<ReadonlySet<string>>(new Set());

  const load = useCallback(async (): Promise<void> => {
    try {
      // Composition comes from the counts endpoint (v2-005 R1). This used to fetch 500
      // active memories and tally them in the browser purely to draw a five-row bar.
      const [economy, decisions, staged, stats] = await Promise.all([
        api.economy(),
        api.decisions(100),
        api.listMemories({ status: 'staged', limit: 20 }),
        api.memoryStats(),
      ]);

      const keptIds = decisions.items.filter(isKept).map((d) => d.memory_id);
      if (seen.current === null) {
        seen.current = new Set(keptIds);
      } else {
        const previous = seen.current;
        const arrived = keptIds.filter((id) => !previous.has(id));
        for (const id of arrived) {
          previous.add(id);
        }
        if (arrived.length > 0 && motionAllowed()) {
          setFresh(new Set(arrived));
          window.setTimeout(() => setFresh(new Set()), LAND_MS);
        }
      }

      setData({
        economy,
        decisions: decisions.items,
        staged: staged.items,
        stats,
      });
      setOffline(false);
    } catch (e) {
      setOffline(e instanceof LoreOfflineError);
      setData({
        economy: { decisions_today: {}, promoted_today: 0, daily_budget: 0 },
        decisions: [],
        staged: [],
        stats: EMPTY_STATS,
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
    (decision) => isKept(decision) && isToday(decision.at),
  );

  const counts = data.economy.decisions_today;
  const episodes = counts['closed'] ?? 0;
  const passed = counts['no_facts'] ?? 0;
  const stagedToday = counts['staged'] ?? 0;

  return (
    // The header is a sibling of .app-page, not a child: it needs to span the full pane
    // width and carry no margins (see the sticky note in chrome.css).
    <>
      <div className="app-page__head">
        <span className="app-page__crumb">Home</span>
        <span className="app-page__sep">/</span>
        <span className="app-page__mini">
          {data.staged.length > 0
            ? `${data.staged.length} awaiting your judgment`
            : 'Nothing awaiting your judgment'}
        </span>
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

      <div className="app-page">
        {offline ? (
          <section className="today-offline">
            <span className="today-offline__icon">
              <Icon name="moon" size={20} />
            </span>
            <div>
              <strong>Lore is resting.</strong>
              <p>
                The local agent is not responding. This view will fill in when
                it returns.
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
              {/* Status first: one sentence on what happened today, then what you got
                  out of it. Judgment moves below — it matters, but it is triage, and
                  leading with it made a handful of pending items own the screen. */}
              <section className="today-lead">
                <p>
                  Lore kept <b>{data.economy.promoted_today}</b> of the{' '}
                  <b>{episodes}</b> {episodes === 1 ? 'episode' : 'episodes'} it
                  saw today.
                </p>
              </section>

              <section className="today-record">
                <div className="today-shead today-shead--quiet">
                  <h2>Learned today</h2>
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
                        className={
                          fresh.has(decision.memory_id)
                            ? 'today-rec today-rec--new'
                            : 'today-rec'
                        }
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

              <section>
                <div className="today-shead today-shead--quiet">
                  <h2>Needs your judgment</h2>
                  <span className="today-shead__spacer" />
                  {data.staged.length > 0 && (
                    <span className="today-chip">{data.staged.length}</span>
                  )}
                </div>
                {data.staged.length === 0 ? (
                  <p className="app-empty">
                    The staging area is clear. Uncertain memories wait here
                    until more evidence arrives.
                  </p>
                ) : (
                  <div className="today-inbox">
                    {data.staged.map((memory) => (
                      <QueueItem
                        key={memory.id}
                        memory={memory}
                        onActed={load}
                      />
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
                      {data.economy.promoted_today} of{' '}
                      {data.economy.daily_budget}
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

              <Composition stats={data.stats} />
            </aside>
          </div>
        )}
      </div>
    </>
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
function Composition({ stats }: { stats: MemoryStats }): JSX.Element {
  const rows = MEMORY_KINDS.map((kind) => ({
    kind,
    n: stats.active[kind] ?? 0,
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
            {stats.total_active} active{' '}
            {stats.total_active === 1 ? 'memory' : 'memories'} in total.
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
function QueueItem({
  memory,
  onActed,
}: {
  memory: Memory;
  onActed: () => Promise<void>;
}): JSX.Element {
  const [busy, setBusy] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [open, setOpen] = useState(false);
  const meta = metaOf(memory);

  // Collapse the row out of the list before refetching, so the queue visibly heals
  // itself rather than an item blinking out from under the cursor.
  const act = async (fn: (id: string) => Promise<unknown>): Promise<void> => {
    setBusy(true);
    try {
      await fn(memory.id);
      if (motionAllowed()) {
        setRemoving(true);
        await new Promise((resolve) => window.setTimeout(resolve, COLLAPSE_MS));
      }
      await onActed();
    } catch {
      setRemoving(false);
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
    <article
      className={removing ? 'today-item today-item--removing' : 'today-item'}
    >
      <button
        type="button"
        className="today-item__summary"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        <span className="today-item__chevron" aria-hidden="true">
          <Icon name={open ? 'chevron-down' : 'chevron-right'} size={14} />
        </span>
        <span className="today-item__dot" aria-hidden="true" />
        <span className="today-item__text">{memory.memory}</span>
        {meta !== null && <span className="today-item__kind">{meta.kind}</span>}
      </button>

      {open && (
        <div className="today-item__body">
          <p className="today-item__why">{evidence}</p>
          {episodes > 0 && <Evidence memoryId={memory.id} />}
          <div className="today-item__foot">
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
        </div>
      )}
    </article>
  );
}

/**
 * The episodes that support a staged memory (v2-004 T009), fetched on demand from
 * v2-005 R3. This answers the question the evidence *line* only summarises — "why does
 * Lore think this?" — without which judging a staged memory is guesswork.
 *
 * Titles here come from the same filter chain capture applies, so nothing appears that
 * Lore would not have captured in the first place.
 */
function Evidence({ memoryId }: { memoryId: string }): JSX.Element {
  const [open, setOpen] = useState(false);
  const [episodes, setEpisodes] = useState<Episode[] | null>(null);
  const [failed, setFailed] = useState(false);

  const toggle = async (): Promise<void> => {
    const next = !open;
    setOpen(next);
    if (!next || episodes !== null) {
      return;
    }
    try {
      const result = await api.memoryEvidence(memoryId);
      setEpisodes(result.episodes);
      setFailed(false);
    } catch {
      setFailed(true);
    }
  };

  return (
    <div className="today-evidence">
      <button
        type="button"
        className="today-evidence__toggle"
        aria-expanded={open}
        onClick={() => void toggle()}
      >
        <Icon name={open ? 'chevron-down' : 'chevron-right'} size={13} />
        {open ? 'Hide evidence' : 'Show evidence'}
      </button>
      {open && (
        <div className="today-evidence__body">
          {failed ? (
            <span className="today-vnote">
              Evidence isn&rsquo;t available right now.
            </span>
          ) : episodes === null ? (
            <span className="today-vnote">Loading&hellip;</span>
          ) : episodes.length === 0 ? (
            <span className="today-vnote">
              The supporting episodes are no longer stored.
            </span>
          ) : (
            episodes.map((episode) => (
              <div className="today-evidence__row" key={episode.id}>
                <span className="today-evidence__app">
                  {episode.executables.join(', ') || 'Unknown app'}
                </span>
                <span className="today-evidence__title">
                  {episode.titles[0] ?? 'No window title'}
                </span>
                <span className="today-evidence__when">
                  {formatRelative(episode.started_at)}
                </span>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}

/** The decision actions that mean "this ended up in memory". */
function isKept(decision: Decision): boolean {
  return (
    decision.action === 'promoted' ||
    decision.action === 'revised' ||
    decision.action === 'user_promoted'
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
