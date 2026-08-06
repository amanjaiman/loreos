import { useCallback, useEffect, useState } from 'react';

import { api, LoreOfflineError, type Decision, type Episode } from '../api';
import { PageHeader } from '../chrome/PageHeader';
import { Card, Spinner, Tabs, Tag } from '../design-system';
import { formatRelative } from '../lib/format';
import { Icon } from '../lib/Icon';
import './activity.css';

type Feed = 'decisions' | 'episodes';

/** Whether the decision trail shows only memory-bearing rows, or the engine's full trace. */
type Scope = 'memories' | 'all';

/**
 * Raw reason codes leak straight from the engine. Translate the ones a user might
 * actually meet; everything else at least loses its underscores.
 */
const REASON_LABELS: Record<string, string> = {
  continuity_break_or_bound: 'activity moved on, so the episode closed',
  idle_timeout: 'the window went idle',
  max_duration: 'the episode hit its length limit',
  shutdown: 'Lore shut down',
};

function humanizeReason(reason: string): string {
  return REASON_LABELS[reason] ?? reason.replace(/_/g, ' ');
}

/**
 * A decision is about a *memory* when it carries a statement. Episode housekeeping —
 * "episode closed · continuity_break_or_bound", "nothing durable" — never does.
 *
 * Deriving it from the statement rather than an action allow-list means new engine
 * actions land on the right side of the filter without anyone remembering to update it.
 */
function isAboutAMemory(decision: Decision): boolean {
  return decision.statement.trim().length > 0;
}

const ACTION_LABELS: Record<string, string> = {
  closed: 'episode closed',
  no_facts: 'nothing durable',
  distill_failed: 'distillation failed',
  route_failed: 'routing failed',
  staged: 'staged',
  deferred: 'deferred (budget)',
  deferred_high_signal: 'deferred (budget)',
  promoted: 'remembered',
  reinforced: 'reinforced',
  revised: 'revised',
  deduped: 'merged into an existing memory',
  needs_confirmation: 'needs your confirmation',
  user_promoted: 'kept by you',
  user_dismissed: 'dismissed by you',
};

/**
 * Activity — the explainability surface (v2-001): the decision trail ("why does — or
 * doesn't — Lore know X?") and the episodes the distiller actually saw. Read-only;
 * trust is won here, not managed here.
 */
export function Activity(): JSX.Element {
  const [feed, setFeed] = useState<Feed>('decisions');
  const [scope, setScope] = useState<Scope>('memories');
  const [decisions, setDecisions] = useState<Decision[] | null>(null);
  const [episodes, setEpisodes] = useState<Episode[] | null>(null);
  const [offline, setOffline] = useState(false);

  const load = useCallback(async (): Promise<void> => {
    try {
      const [d, e] = await Promise.all([api.decisions(200), api.episodes(50)]);
      setDecisions(d.items);
      setEpisodes(e.items);
      setOffline(false);
    } catch (err) {
      setOffline(err instanceof LoreOfflineError);
      setDecisions([]);
      setEpisodes([]);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const loading = decisions === null || episodes === null;
  // The engine's own housekeeping ("episode closed · idle_timeout") is the bulk of the
  // trail and answers nothing a user asked. It stays available, just not by default.
  const shown =
    decisions === null
      ? []
      : scope === 'all'
        ? decisions
        : decisions.filter(isAboutAMemory);

  return (
    <>
      <PageHeader
        eyebrow="// timeline"
        title="Every choice, in context."
        description="See what Lore observed, why it acted, and why most activity never became a memory."
      />
      <div className="app-page">
        <div className="act-toolbar">
          <Tabs
            tabs={[
              { value: 'decisions', label: 'Decisions' },
              { value: 'episodes', label: 'Episodes' },
            ]}
            value={feed}
            onChange={(v) => setFeed(v as Feed)}
          />
          {!loading && !offline && feed === 'decisions' && (
            <button
              type="button"
              className="act-toolbar__scope"
              aria-pressed={scope === 'all'}
              onClick={() =>
                setScope((s) => (s === 'all' ? 'memories' : 'all'))
              }
            >
              <Icon name={scope === 'all' ? 'eye' : 'eye-off'} size={13} />
              {scope === 'all' ? 'Showing engine steps' : 'Show engine steps'}
            </button>
          )}
          {!loading && !offline && (
            <span className="act-toolbar__count">
              {feed === 'decisions'
                ? `${shown.length} ${shown.length === 1 ? 'decision' : 'decisions'}`
                : `${episodes.length} episodes`}
            </span>
          )}
        </div>

        {loading ? (
          <div className="act-loading">
            <Spinner size={20} />
          </div>
        ) : offline ? (
          <p className="app-empty">Lore isn't running — no activity to show.</p>
        ) : feed === 'decisions' ? (
          shown.length === 0 ? (
            <p className="app-empty">
              {scope === 'memories' && decisions.length > 0
                ? 'No memory decisions yet — Lore has been watching, but nothing has earned a place. Turn on engine steps to see what it did instead.'
                : 'No decisions yet. As episodes close, every choice Lore makes — remembered, staged, or passed over — is recorded here.'}
            </p>
          ) : (
            <div className="act-list">
              {shown.map((d, i) => (
                <DecisionRow key={i} decision={d} />
              ))}
            </div>
          )
        ) : episodes.length === 0 ? (
          <p className="app-empty">
            No episodes yet. An episode is a stretch of related activity; it
            closes after a break and is summarized for the distiller.
          </p>
        ) : (
          <div className="act-list">
            {episodes.map((e) => (
              <EpisodeCard key={e.id} episode={e} />
            ))}
          </div>
        )}
      </div>
    </>
  );
}

function DecisionRow({ decision }: { decision: Decision }): JSX.Element {
  const label = ACTION_LABELS[decision.action] ?? decision.action;
  const reason = humanizeReason(decision.reason);

  // Housekeeping gets a single quiet line, not a card. It only appears at all when the
  // user has asked for engine steps, and giving it the same weight as a real memory
  // decision is what made the trail unreadable.
  if (!isAboutAMemory(decision)) {
    return (
      <div className="act-step">
        <span className="act-step__dot" aria-hidden="true" />
        <span className="act-step__label">{label}</span>
        {reason.length > 0 && (
          <span className="act-step__reason">{reason}</span>
        )}
        <span className="act-step__time">{formatRelative(decision.at)}</span>
      </div>
    );
  }

  return (
    <Card className="act-row">
      <span className={`act-row__marker act-row__marker--${decision.action}`}>
        <Icon name={actionIcon(decision.action)} size={14} />
      </span>
      <div className="act-row__content">
        <div className="act-row__head">
          <Tag>{label}</Tag>
          <span className="act-row__time">{formatRelative(decision.at)}</span>
        </div>
        <p className="act-row__statement">{decision.statement}</p>
        {reason.length > 0 && <p className="act-row__reason">{reason}</p>}
      </div>
    </Card>
  );
}

function actionIcon(action: string): string {
  if (['promoted', 'user_promoted', 'reinforced', 'revised'].includes(action)) {
    return 'bookmark-check';
  }
  if (action === 'staged' || action === 'needs_confirmation') {
    return 'circle-dashed';
  }
  if (action.includes('failed')) {
    return 'triangle-alert';
  }
  if (action.startsWith('deferred')) {
    return 'clock-3';
  }
  return 'minus';
}

function EpisodeCard({ episode }: { episode: Episode }): JSX.Element {
  const [open, setOpen] = useState(false);
  const minutes = Math.max(
    1,
    Math.round(
      (new Date(episode.ended_at).getTime() -
        new Date(episode.started_at).getTime()) /
        60_000,
    ),
  );
  return (
    <Card
      interactive
      role="button"
      tabIndex={0}
      className="act-episode"
      onClick={() => setOpen((o) => !o)}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          setOpen((o) => !o);
        }
      }}
    >
      <div className="act-row__head">
        <span className="act-episode__heading">
          <span className="act-episode__icon">
            <Icon name="panels-top-left" size={15} />
          </span>
          <span className="act-episode__title">
            {episode.titles[0] ?? episode.executables.join(', ')}
          </span>
        </span>
        <span className="act-row__time">
          {formatRelative(episode.ended_at)}
        </span>
      </div>
      <p className="act-episode__stats">
        {minutes} min · {episode.executables.join(', ')} ·{' '}
        {episode.observation_count} observation
        {episode.observation_count === 1 ? '' : 's'}
      </p>
      {open && (
        <div className="act-episode__detail">
          {episode.titles.length > 1 && (
            <ul className="act-episode__titles">
              {episode.titles.map((t, i) => (
                <li key={i}>{t}</li>
              ))}
            </ul>
          )}
          {episode.samples.map((s, i) => (
            <p key={i} className="act-episode__sample">
              {s}
            </p>
          ))}
        </div>
      )}
    </Card>
  );
}
