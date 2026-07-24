import { useCallback, useEffect, useState } from 'react';

import {
  api,
  metaOf,
  LoreOfflineError,
  type Decision,
  type Economy,
  type Memory,
} from '../api';
import { PageHeader } from '../chrome/PageHeader';
import { Button, Spinner, Tag } from '../design-system';
import { formatRelative } from '../lib/format';
import { Icon } from '../lib/Icon';
import { useRouter } from '../lib/router';
import './today.css';

interface TodayData {
  economy: Economy;
  decisions: Decision[];
  staged: Memory[];
}

export function Today(): JSX.Element {
  const [data, setData] = useState<TodayData | null>(null);
  const [offline, setOffline] = useState(false);
  const { navigate } = useRouter();

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

  const remembered = data.decisions.filter(
    (decision) =>
      (decision.action === 'promoted' ||
        decision.action === 'revised' ||
        decision.action === 'user_promoted') &&
      isToday(decision.at),
  );
  const counts = data.economy.decisions_today;
  const episodes = counts['closed'] ?? 0;
  const stagedToday = counts['staged'] ?? 0;
  const passed = counts['no_facts'] ?? 0;

  return (
    <div className="app-page">
      <PageHeader
        eyebrow="// overview"
        title="Memory, kept warm."
        description="A quiet view of what Lore noticed, what it kept, and what still needs your judgment."
        action={
          <Button
            variant="secondary"
            size="sm"
            icon="search"
            onClick={() => navigate('memory')}
          >
            Search memory
          </Button>
        }
      />

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
        <>
          <section className="today-brief">
            <div className="today-brief__lead">
              <span className="today-brief__pulse" aria-hidden="true">
                <span />
              </span>
              <div>
                <span className="today-brief__label">Today</span>
                <h2>
                  {remembered.length === 0
                    ? 'Lore is listening for what lasts.'
                    : `Lore remembered ${remembered.length} ${remembered.length === 1 ? 'thing' : 'things'}.`}
                </h2>
                <p>
                  {remembered.length === 0
                    ? 'Ordinary activity stays out of your memory until it earns a place.'
                    : 'Each memory was supported by the activity Lore observed today.'}
                </p>
              </div>
            </div>
            <div className="today-metrics" aria-label="Today’s capture summary">
              <Metric value={episodes} label="Episodes" />
              <Metric value={stagedToday} label="Staged" />
              <Metric value={data.economy.promoted_today} label="Remembered" />
              <Metric value={passed} label="Passed over" />
            </div>
            <div className="today-budget">
              <div className="today-budget__copy">
                <span>Daily memory budget</span>
                <span>
                  {data.economy.promoted_today} / {data.economy.daily_budget}
                </span>
              </div>
              <div className="today-budget__track">
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
          </section>

          <section className="today-section">
            <SectionHeading
              title="Remembered today"
              detail={`${remembered.length} ${remembered.length === 1 ? 'memory' : 'memories'}`}
              icon="sparkles"
            />
            {remembered.length === 0 ? (
              <p className="app-empty">
                Nothing new has earned a place yet. Lore is still paying
                attention.
              </p>
            ) : (
              <div className="today-list">
                {remembered.map((decision, index) => (
                  <article
                    key={`${decision.memory_id}-${index}`}
                    className="today-memory"
                  >
                    <span className="today-memory__glyph">
                      <Icon name="bookmark" size={16} />
                    </span>
                    <div className="today-memory__body">
                      <p>{decision.statement}</p>
                      <div className="today-memory__meta">
                        <Tag>{decision.kind}</Tag>
                        <span>{formatRelative(decision.at)}</span>
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>

          <section className="today-section">
            <SectionHeading
              title="Awaiting your judgment"
              detail={`${data.staged.length} staged`}
              icon="inbox"
            />
            {data.staged.length === 0 ? (
              <p className="app-empty">
                The staging area is clear. Uncertain memories wait here until
                more evidence arrives.
              </p>
            ) : (
              <div className="today-list">
                {data.staged.map((memory) => (
                  <StagedCard key={memory.id} memory={memory} onActed={load} />
                ))}
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}

function Metric({
  value,
  label,
}: {
  value: number;
  label: string;
}): JSX.Element {
  return (
    <div className="today-metric">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function SectionHeading({
  title,
  detail,
  icon,
}: {
  title: string;
  detail: string;
  icon: string;
}): JSX.Element {
  return (
    <div className="today-section__head">
      <div className="today-section__title">
        <span>
          <Icon name={icon} size={16} />
        </span>
        <h2>{title}</h2>
      </div>
      <span className="today-section__detail">{detail}</span>
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
    <article className="today-memory today-memory--staged">
      <span className="today-memory__glyph">
        <Icon name="circle-dashed" size={16} />
      </span>
      <div className="today-memory__body">
        <p>{memory.memory}</p>
        <div className="today-memory__meta">
          {meta !== null && <Tag>{meta.kind}</Tag>}
          <span>Waiting for evidence</span>
        </div>
      </div>
      <div className="today-memory__actions">
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
