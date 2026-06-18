import { useEffect, useState } from 'react';

import { api, LoreOfflineError, type RawCapture } from '../api';
import { Badge, Card, Divider, Spinner } from '../design-system';
import { formatCount, formatRelative } from '../lib/format';
import { useConfig, useSystemStatus } from '../lib/hooks';
import './home.css';

/**
 * Home — the ambient surface. Is Lore listening, what it has read lately, and a few terse
 * stats. Calm and single-focus; all data comes through api.ts.
 */
export function Home(): JSX.Element {
  const { latencyMs, offline } = useSystemStatus();
  const { config } = useConfig();
  const [memoryCount, setMemoryCount] = useState<number | null>(null);
  const [recent, setRecent] = useState<RawCapture[] | null>(null);

  useEffect(() => {
    let alive = true;
    void (async (): Promise<void> => {
      try {
        const [page, captures] = await Promise.all([
          api.listMemories({ limit: 1 }),
          api.recent(6),
        ]);
        if (alive) {
          setMemoryCount(page.total);
          setRecent(captures.items);
        }
      } catch (e) {
        if (e instanceof LoreOfflineError && alive) {
          setRecent([]);
        }
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const listening = !offline && config?.capture?.enabled !== false;
  const lastSeen =
    recent && recent.length > 0 ? formatRelative(recent[0].at) : null;

  return (
    <div className="app-page">
      <Card>
        <div className="home-status">
          <Badge
            variant={offline ? 'warning' : listening ? 'success' : 'neutral'}
            dot
          >
            {offline
              ? "Lore isn't running"
              : listening
                ? 'Listening'
                : 'Paused'}
          </Badge>
          <p className="home-status__line">
            {offline
              ? 'Start Lore to begin keeping your memory warm.'
              : listening
                ? 'Lore is watching quietly and threading what matters.'
                : 'Lore is paused. Turn capture back on in Settings whenever you like.'}
          </p>
        </div>

        <Divider />

        <div className="home-stats">
          <Stat
            label="// memories"
            value={memoryCount === null ? '—' : formatCount(memoryCount)}
          />
          <Stat label="// last seen" value={lastSeen ?? '—'} />
          <Stat
            label="// api latency"
            value={latencyMs === null ? '—' : `→ ${latencyMs}ms`}
          />
        </div>
      </Card>

      <Card eyebrow="// lately" title="What Lore has read">
        {recent === null ? (
          <div className="home-empty">
            <Spinner size={18} />
          </div>
        ) : recent.length === 0 ? (
          <p className="home-empty__text">
            Nothing yet. Once Lore is listening, recent windows show up here.
          </p>
        ) : (
          <ul className="home-recent">
            {recent.map((capture, i) => (
              <li key={`${capture.at}-${i}`} className="home-recent__item">
                <span className="home-recent__title">
                  {capture.window_title ||
                    capture.executable ||
                    'Untitled window'}
                </span>
                <span className="home-recent__time">
                  {formatRelative(capture.at)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div className="home-stat">
      <span className="home-stat__label">{label}</span>
      <span className="home-stat__value">{value}</span>
    </div>
  );
}
