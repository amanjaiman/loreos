import { useCallback, useEffect, useState, type FormEvent } from 'react';

import {
  api,
  metaOf,
  LoreOfflineError,
  MEMORY_KINDS,
  type Memory as MemoryRecord,
  type MemoryKind,
  type MemoryMeta,
} from '../api';
import { PageHeader } from '../chrome/PageHeader';
import {
  Badge,
  Button,
  Card,
  Dialog,
  Input,
  Select,
  Spinner,
  Tabs,
  Tag,
  Textarea,
} from '../design-system';
import { formatRelative } from '../lib/format';
import { Icon } from '../lib/Icon';
import './memory.css';

const KIND_LABELS: Record<string, string> = {
  identity: 'Who you are',
  preference: 'What you prefer',
  state: "What you're going through",
  experience: "What you've done",
  project: "What you're building",
};

type Section = 'profile' | 'staged' | 'archived';

/**
 * Memory — the user's profile as Lore knows it: active facts grouped by kind, the
 * staging area (candidates awaiting a second look), and the read-only archive.
 * Edit / pin / delete / confirm are user authority — every write outranks capture.
 */
export function Memory(): JSX.Element {
  const [section, setSection] = useState<Section>('profile');
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [items, setItems] = useState<MemoryRecord[] | null>(null);
  const [offline, setOffline] = useState(false);
  const [selected, setSelected] = useState<MemoryRecord | null>(null);

  const load = useCallback(async (q: string, s: Section): Promise<void> => {
    setItems(null);
    setOffline(false);
    try {
      if (q.trim().length > 0) {
        const res = await api.searchMemories({
          query: q.trim(),
          limit: 50,
          filters: { status: s === 'profile' ? 'active' : s },
        });
        setItems(res.results);
      } else {
        const status = s === 'profile' ? 'active' : s;
        const page = await api.listMemories({ status, limit: 500 });
        setItems(page.items);
      }
    } catch (e) {
      setOffline(e instanceof LoreOfflineError);
      setItems([]);
    }
  }, []);

  useEffect(() => {
    void load(submitted, section);
  }, [load, submitted, section]);

  const refresh = (): void => {
    void load(submitted, section);
  };

  const onSearch = (e: FormEvent): void => {
    e.preventDefault();
    setSubmitted(query);
  };

  const groups =
    items === null
      ? []
      : MEMORY_KINDS.map((kind) => ({
          kind,
          memories: items.filter((m) => (metaOf(m)?.kind ?? '') === kind),
        })).filter((g) => g.memories.length > 0);
  const ungrouped =
    items?.filter((m) => {
      const kind = metaOf(m)?.kind ?? '';
      return !MEMORY_KINDS.includes(kind as MemoryKind);
    }) ?? [];

  return (
    <div className="app-page">
      <PageHeader
        eyebrow="// memory"
        title="What Lore knows."
        description="Every durable fact is visible, editable, and yours to remove."
        action={
          items !== null && !offline ? (
            <span className="mem-total">
              <strong>{items.length}</strong>
              <span>{items.length === 1 ? 'memory' : 'memories'}</span>
            </span>
          ) : undefined
        }
      />
      <div className="mem-toolbar">
        <Tabs
          tabs={[
            { value: 'profile', label: 'Profile' },
            { value: 'staged', label: 'Staged' },
            { value: 'archived', label: 'Archived' },
          ]}
          value={section}
          onChange={(v) => setSection(v as Section)}
        />
        <form className="mem-search" onSubmit={onSearch}>
          <Input
            icon="search"
            placeholder="Search your memory…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            aria-label="Search memories"
          />
          <Button type="submit" variant="secondary">
            Search
          </Button>
        </form>
      </div>

      {items === null ? (
        <div className="mem-loading">
          <Spinner size={20} />
        </div>
      ) : offline ? (
        <p className="app-empty">
          Lore isn't running — memory is unavailable right now.
        </p>
      ) : items.length === 0 ? (
        <p className="app-empty">
          {submitted.trim().length > 0
            ? `Nothing matches “${submitted.trim()}”.`
            : section === 'staged'
              ? 'Nothing is staged right now.'
              : section === 'archived'
                ? 'Nothing has been archived yet.'
                : 'No memories yet. Lore stages a candidate when an episode reveals something durable, and keeps it once a second episode agrees.'}
        </p>
      ) : (
        <>
          {groups.map(({ kind, memories }) => (
            <section key={kind} className="mem-group">
              <div className="mem-group__head">
                <span className="mem-group__icon">
                  <Icon name={kindIcon(kind)} size={15} />
                </span>
                <div>
                  <h2>{KIND_LABELS[kind] ?? kind}</h2>
                  <span>{memories.length}</span>
                </div>
              </div>
              <div className="mem-list">
                {memories.map((m) => (
                  <MemoryCard
                    key={m.id}
                    memory={m}
                    section={section}
                    onSelect={() => setSelected(m)}
                    onChanged={refresh}
                  />
                ))}
              </div>
            </section>
          ))}
          {ungrouped.length > 0 && (
            <section className="mem-group">
              <p className="app-eyebrow">{'// from before the restart'}</p>
              <div className="mem-list">
                {ungrouped.map((m) => (
                  <MemoryCard
                    key={m.id}
                    memory={m}
                    section={section}
                    onSelect={() => setSelected(m)}
                    onChanged={refresh}
                  />
                ))}
              </div>
            </section>
          )}
        </>
      )}

      {selected !== null && (
        <MemoryDialog
          memory={selected}
          onClose={() => setSelected(null)}
          onChanged={() => {
            setSelected(null);
            refresh();
          }}
        />
      )}
    </div>
  );
}

function kindIcon(kind: string): string {
  switch (kind) {
    case 'identity':
      return 'fingerprint';
    case 'preference':
      return 'heart';
    case 'state':
      return 'activity';
    case 'experience':
      return 'milestone';
    case 'project':
      return 'folder-kanban';
    default:
      return 'bookmark';
  }
}

function MemoryCard({
  memory,
  section,
  onSelect,
  onChanged,
}: {
  memory: MemoryRecord;
  section: Section;
  onSelect: () => void;
  onChanged: () => void;
}): JSX.Element {
  const meta = metaOf(memory);
  const [busy, setBusy] = useState(false);

  const act = async (fn: (id: string) => Promise<unknown>): Promise<void> => {
    setBusy(true);
    try {
      await fn(memory.id);
      onChanged();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card
      interactive
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          onSelect();
        }
      }}
      className="mem-card"
    >
      <p className="mem-card__text">{memory.memory}</p>
      <div className="mem-card__meta">
        <span className="mem-card__tags">
          {meta?.pinned === true && (
            <Badge variant="neutral" dot>
              pinned
            </Badge>
          )}
          {meta !== null && meta.reinforced > 0 && (
            <Tag>{`seen ×${meta.reinforced + 1}`}</Tag>
          )}
          {meta !== null && meta.established_at > 0 && (
            <span className="mem-card__time">
              since{' '}
              {formatRelative(
                new Date(meta.established_at * 1000).toISOString(),
              )}
            </span>
          )}
        </span>
        {section === 'staged' && (
          <span
            className="mem-card__actions"
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => e.stopPropagation()}
            role="presentation"
          >
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
        )}
      </div>
    </Card>
  );
}

function MemoryDialog({
  memory,
  onClose,
  onChanged,
}: {
  memory: MemoryRecord;
  onClose: () => void;
  onChanged: () => void;
}): JSX.Element {
  const meta: MemoryMeta | null = metaOf(memory);
  const [text, setText] = useState(memory.memory);
  const [kind, setKind] = useState(meta?.kind ?? '');
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const textDirty = text.trim() !== memory.memory && text.trim().length > 0;
  const kindDirty = kind !== (meta?.kind ?? '') && kind.length > 0;

  const run = async (fn: () => Promise<unknown>): Promise<void> => {
    setBusy(true);
    setError(null);
    try {
      await fn();
      onChanged();
    } catch {
      setError("Couldn't save. Is Lore running?");
      setBusy(false);
    }
  };

  const save = (): Promise<void> =>
    run(() =>
      api.updateMemory(memory.id, {
        ...(textDirty ? { text: text.trim() } : {}),
        ...(kindDirty ? { kind: kind as MemoryKind } : {}),
      }),
    );

  return (
    <Dialog
      open
      onClose={onClose}
      title="Memory"
      footer={
        <div className="mem-dialog__footer">
          {confirmDelete ? (
            <Button
              variant="danger"
              onClick={() => void run(() => api.deleteMemory(memory.id))}
              disabled={busy}
            >
              Confirm delete
            </Button>
          ) : (
            <Button
              variant="ghost"
              icon="trash-2"
              onClick={() => setConfirmDelete(true)}
              disabled={busy}
            >
              Delete
            </Button>
          )}
          <div className="mem-dialog__right">
            <Button variant="ghost" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button
              variant="primary"
              onClick={() => void save()}
              disabled={(!textDirty && !kindDirty) || busy}
            >
              {busy ? 'Saving…' : 'Save'}
            </Button>
          </div>
        </div>
      }
    >
      <Textarea
        label="Statement"
        rows={4}
        value={text}
        onChange={(e) => setText(e.target.value)}
        aria-label="Memory statement"
      />
      {meta !== null && (
        <>
          <div className="mem-dialog__row">
            <Select
              label="Kind"
              value={kind}
              onChange={(e) => setKind(e.target.value)}
              options={MEMORY_KINDS.map((k) => ({ value: k, label: k }))}
            />
          </div>
          <div className="mem-dialog__row mem-dialog__actions">
            <Button
              variant="outline"
              size="sm"
              icon={meta.pinned ? 'pin-off' : 'pin'}
              disabled={busy}
              onClick={() =>
                void run(() =>
                  api.updateMemory(memory.id, { pinned: !meta.pinned }),
                )
              }
            >
              {meta.pinned ? 'Unpin' : 'Pin'}
            </Button>
            {meta.status === 'active' && meta.kind === 'state' && (
              <Button
                variant="outline"
                size="sm"
                icon="check"
                disabled={busy}
                onClick={() => void run(() => api.confirmMemory(memory.id))}
              >
                Still true
              </Button>
            )}
          </div>
          <p className="mem-dialog__hint">
            {meta.status} · confidence{' '}
            <span className="mem-mono">{meta.confidence.toFixed(2)}</span>
            {meta.episodes.length > 0 && (
              <>
                {' '}
                · from {meta.episodes.length} episode
                {meta.episodes.length === 1 ? '' : 's'}
              </>
            )}
          </p>
        </>
      )}
      {error !== null && <p className="mem-dialog__error">{error}</p>}
    </Dialog>
  );
}
