import { useEffect, useMemo, useState } from 'react';

import { api, LoreOfflineError, type ActivityEntry } from '../api';
import { Badge, Card, Spinner, Tabs, Tag, Tooltip } from '../design-system';
import { formatRelative } from '../lib/format';
import './activity.css';

type TabValue = 'all' | 'Captured' | 'Skipped' | 'Filtered';

const DECISION_VARIANT: Record<string, 'success' | 'neutral' | 'warning'> = {
  Captured: 'success',
  Skipped: 'neutral',
  Filtered: 'warning',
};

function reasonLabel(entry: ActivityEntry): string {
  if (entry.reason.length > 0) {
    return entry.reason;
  }
  return entry.decision === 'Captured' ? 'Stored to memory' : entry.decision;
}

/**
 * Activity — the transparency timeline: what Lore saw and decided per window. Captured /
 * Skipped / Filtered with the reason. Filtered (and skipped) rows show *that* and *why*
 * only — the agent never stores their content, so there is nothing sensitive to reveal
 * (acceptance criterion 5, activity half). Captured rows show their distilled observation.
 */
export function Activity(): JSX.Element {
  const [entries, setEntries] = useState<ActivityEntry[] | null>(null);
  const [offline, setOffline] = useState(false);
  const [tab, setTab] = useState<TabValue>('all');

  useEffect(() => {
    let alive = true;
    void (async (): Promise<void> => {
      try {
        const feed = await api.activity(200);
        if (alive) {
          setEntries(feed.items);
        }
      } catch (e) {
        if (alive) {
          setOffline(e instanceof LoreOfflineError);
          setEntries([]);
        }
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  const counts = useMemo(() => {
    const c = { all: 0, Captured: 0, Skipped: 0, Filtered: 0 };
    for (const e of entries ?? []) {
      c.all += 1;
      if (
        e.decision === 'Captured' ||
        e.decision === 'Skipped' ||
        e.decision === 'Filtered'
      ) {
        c[e.decision] += 1;
      }
    }
    return c;
  }, [entries]);

  const visible = useMemo(
    () => (entries ?? []).filter((e) => tab === 'all' || e.decision === tab),
    [entries, tab],
  );

  const tabs = [
    { value: 'all', label: `All · ${counts.all}` },
    { value: 'Captured', label: `Captured · ${counts.Captured}` },
    { value: 'Skipped', label: `Skipped · ${counts.Skipped}` },
    { value: 'Filtered', label: `Filtered · ${counts.Filtered}` },
  ];

  return (
    <div className="app-page">
      <Tabs tabs={tabs} value={tab} onChange={(v) => setTab(v as TabValue)} />

      {entries === null ? (
        <div className="act-empty">
          <Spinner size={20} />
        </div>
      ) : offline ? (
        <p className="act-empty__text">
          Lore isn't running — the activity log is unavailable.
        </p>
      ) : visible.length === 0 ? (
        <p className="act-empty__text">
          {tab === 'all'
            ? 'Nothing yet. As Lore watches, every decision shows up here.'
            : `No ${tab.toLowerCase()} windows yet.`}
        </p>
      ) : (
        <Card>
          <ul className="act-list">
            {visible.map((entry, i) => (
              <li key={`${entry.at}-${i}`} className="act-row">
                <div className="act-row__main">
                  <span className="act-row__title">
                    {entry.window_title ||
                      entry.executable ||
                      'Untitled window'}
                  </span>
                  {entry.decision === 'Captured' &&
                    entry.observation.length > 0 && (
                      <span className="act-row__obs">{entry.observation}</span>
                    )}
                </div>
                <div className="act-row__right">
                  {entry.decision === 'Captured' &&
                    entry.category.length > 0 && <Tag>{entry.category}</Tag>}
                  <Tooltip label={reasonLabel(entry)}>
                    <Badge
                      variant={DECISION_VARIANT[entry.decision] ?? 'neutral'}
                    >
                      {entry.decision}
                    </Badge>
                  </Tooltip>
                  <span className="act-row__time">
                    {formatRelative(entry.at)}
                  </span>
                </div>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  );
}
