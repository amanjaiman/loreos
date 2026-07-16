import { useCallback, useEffect, useState } from 'react';

import { api, LoreOfflineError, type Decision, type Episode } from '../api';
import { Card, Spinner, Tabs, Tag } from '../design-system';
import { formatRelative } from '../lib/format';
import './activity.css';

type Feed = 'decisions' | 'episodes';

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

  return (
    <div className="app-page">
      <Tabs
        tabs={[
          { value: 'decisions', label: 'Decisions' },
          { value: 'episodes', label: 'Episodes' },
        ]}
        value={feed}
        onChange={(v) => setFeed(v as Feed)}
      />

      {loading ? (
        <div className="act-loading">
          <Spinner size={20} />
        </div>
      ) : offline ? (
        <p className="app-empty">Lore isn't running — no activity to show.</p>
      ) : feed === 'decisions' ? (
        decisions.length === 0 ? (
          <p className="app-empty">
            No decisions yet. As episodes close, every choice Lore makes —
            remembered, staged, or passed over — is recorded here.
          </p>
        ) : (
          <div className="act-list">
            {decisions.map((d, i) => (
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
  );
}

function DecisionRow({ decision }: { decision: Decision }): JSX.Element {
  return (
    <Card className="act-row">
      <div className="act-row__head">
        <Tag>{ACTION_LABELS[decision.action] ?? decision.action}</Tag>
        <span className="act-row__time">{formatRelative(decision.at)}</span>
      </div>
      {decision.statement.length > 0 && (
        <p className="act-row__statement">{decision.statement}</p>
      )}
      {decision.reason.length > 0 && (
        <p className="act-row__reason">{decision.reason}</p>
      )}
    </Card>
  );
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
        <span className="act-episode__title">
          {episode.titles[0] ?? episode.executables.join(', ')}
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
