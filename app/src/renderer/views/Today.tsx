import { useCallback, useEffect, useState } from 'react';

import {
  api,
  metaOf,
  LoreOfflineError,
  type Decision,
  type Economy,
  type Memory,
} from '../api';
import { Button, Card, Spinner, Tag } from '../design-system';
import { formatRelative } from '../lib/format';
import './today.css';

interface TodayData {
  economy: Economy;
  decisions: Decision[];
  staged: Memory[];
}

/**
 * Today — the answer to "what has Lore been doing?" in one calm column: what was
 * remembered today, what's staged awaiting a second look, and the day's capture
 * economy in a single mono line. Primary action: review staged candidates.
 */
export function Today(): JSX.Element {
  const [data, setData] = useState<TodayData | null>(null);
  const [offline, setOffline] = useState(false);

  const load = useCallback(async (): Promise<void> => {
    try {
      const [economy, decisions, staged] = await Promise.all([
        api.economy(),
        api.decisions(100),
        api.listMemories({ status: 'staged', limit: 20 }),
      ]);
      setData({ economy, decisions: decisions.items, staged: staged.items });
      setOffline(false);
    } catch (e) {
      setOffline(e instanceof LoreOfflineError);
      setData({
        economy: { decisions_today: {}, promoted_today: 0, daily_budget: 0 },
        decisions: [],
        staged: [],
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
  if (offline) {
    return (
      <div className="app-page">
        <p className="app-empty">
          Lore isn't running — nothing to report right now.
        </p>
      </div>
    );
  }

  const remembered = data.decisions.filter(
    (d) =>
      (d.action === 'promoted' ||
        d.action === 'revised' ||
        d.action === 'user_promoted') &&
      isToday(d.at),
  );
  const counts = data.economy.decisions_today;
  const seen = counts['closed'] ?? 0;

  return (
    <div className="app-page">
      <div>
        <p className="app-eyebrow">{'// today'}</p>
        <h2 className="app-section__title">
          {remembered.length === 0
            ? 'Nothing new remembered today — that usually means a quiet, ordinary day.'
            : `Lore remembered ${remembered.length} thing${remembered.length === 1 ? '' : 's'} today.`}
        </h2>
        <p className="today-economy">
          episodes {seen} → staged {counts['staged'] ?? 0} → promoted{' '}
          {data.economy.promoted_today}/{data.economy.daily_budget} · deferred{' '}
          {counts['deferred'] ?? 0} · nothing-durable {counts['no_facts'] ?? 0}
        </p>
      </div>

      {remembered.length > 0 && (
        <section className="today-section">
          <p className="app-eyebrow">{'// remembered'}</p>
          <div className="today-list">
            {remembered.map((d, i) => (
              <Card key={`${d.memory_id}-${i}`} className="today-card">
                <p className="today-card__text">{d.statement}</p>
                <div className="today-card__meta">
                  <Tag>{d.kind}</Tag>
                  <span className="today-card__time">
                    {formatRelative(d.at)}
                  </span>
                </div>
              </Card>
            ))}
          </div>
        </section>
      )}

      <section className="today-section">
        <p className="app-eyebrow">{'// staged — awaiting a second look'}</p>
        {data.staged.length === 0 ? (
          <p className="app-empty">
            Nothing is staged. Candidates appear here until a second episode
            supports them — or until you decide.
          </p>
        ) : (
          <div className="today-list">
            {data.staged.map((memory) => (
              <StagedCard key={memory.id} memory={memory} onActed={load} />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function StagedCard({
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

  return (
    <Card className="today-card">
      <p className="today-card__text">{memory.memory}</p>
      <div className="today-card__meta">
        {meta !== null && <Tag>{meta.kind}</Tag>}
        <span className="today-card__actions">
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
        </span>
      </div>
    </Card>
  );
}

function isToday(iso: string): boolean {
  const d = new Date(iso);
  const now = new Date();
  return (
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
  );
}
