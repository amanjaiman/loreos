import { useCallback, useEffect, useState, type FormEvent } from 'react';

import { api, LoreOfflineError, type Memory } from '../api';
import {
  Button,
  Card,
  Dialog,
  Input,
  Spinner,
  Tag,
  Textarea,
} from '../design-system';
import { formatRelative } from '../lib/format';
import './library.css';

function categoryOf(memory: Memory): string | null {
  const category = memory.metadata?.['category'];
  return typeof category === 'string' && category.length > 0 ? category : null;
}

/**
 * Library — search-first browse of every memory, with view/edit/delete in a dialog. All
 * reads and writes round-trip through 005 (acceptance criterion 5); no business logic here.
 */
export function Library(): JSX.Element {
  const [query, setQuery] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [results, setResults] = useState<Memory[] | null>(null);
  const [offline, setOffline] = useState(false);
  const [selected, setSelected] = useState<Memory | null>(null);

  const load = useCallback(async (q: string): Promise<void> => {
    setResults(null);
    setOffline(false);
    try {
      if (q.trim().length > 0) {
        const res = await api.searchMemories({ query: q.trim(), limit: 50 });
        setResults(res.results);
      } else {
        const page = await api.listMemories({ limit: 100 });
        setResults(page.items);
      }
    } catch (e) {
      setOffline(e instanceof LoreOfflineError);
      setResults([]);
    }
  }, []);

  useEffect(() => {
    void load('');
  }, [load]);

  const onSearch = (e: FormEvent): void => {
    e.preventDefault();
    setSubmitted(query);
    void load(query);
  };

  const onSaved = (updated: Memory): void => {
    setResults((rs) =>
      (rs ?? []).map((m) => (m.id === updated.id ? updated : m)),
    );
    setSelected(null);
  };

  const onDeleted = (id: string): void => {
    setResults((rs) => (rs ?? []).filter((m) => m.id !== id));
    setSelected(null);
  };

  return (
    <div className="app-page">
      <form className="lib-search" onSubmit={onSearch}>
        <Input
          icon="search"
          placeholder="Search your memory…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          aria-label="Search memories"
        />
        <Button type="submit" variant="primary">
          Search
        </Button>
      </form>

      {results === null ? (
        <div className="lib-empty">
          <Spinner size={20} />
        </div>
      ) : offline ? (
        <p className="lib-empty__text">
          Lore isn't running — memory is unavailable right now.
        </p>
      ) : results.length === 0 ? (
        <p className="lib-empty__text">
          {submitted.trim().length > 0
            ? `Nothing matches “${submitted.trim()}”.`
            : 'Nothing kept yet. Memories appear here as Lore listens, or add one manually.'}
        </p>
      ) : (
        <div className="lib-list">
          {results.map((memory) => (
            <Card
              key={memory.id}
              interactive
              onClick={() => setSelected(memory)}
              className="lib-card"
            >
              <p className="lib-card__text">{memory.memory}</p>
              <div className="lib-card__meta">
                {categoryOf(memory) !== null && <Tag>{categoryOf(memory)}</Tag>}
                {memory.created_at !== undefined && (
                  <span className="lib-card__time">
                    {formatRelative(memory.created_at)}
                  </span>
                )}
              </div>
            </Card>
          ))}
        </div>
      )}

      {selected !== null && (
        <MemoryDialog
          memory={selected}
          onClose={() => setSelected(null)}
          onSaved={onSaved}
          onDeleted={onDeleted}
        />
      )}
    </div>
  );
}

function MemoryDialog({
  memory,
  onClose,
  onSaved,
  onDeleted,
}: {
  memory: Memory;
  onClose: () => void;
  onSaved: (updated: Memory) => void;
  onDeleted: (id: string) => void;
}): JSX.Element {
  const [text, setText] = useState(memory.memory);
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dirty = text.trim() !== memory.memory && text.trim().length > 0;

  const save = async (): Promise<void> => {
    setBusy(true);
    setError(null);
    try {
      const updated = await api.updateMemory(memory.id, text.trim());
      onSaved(updated);
    } catch {
      setError("Couldn't save. Is Lore running?");
      setBusy(false);
    }
  };

  const remove = async (): Promise<void> => {
    setBusy(true);
    setError(null);
    try {
      await api.deleteMemory(memory.id);
      onDeleted(memory.id);
    } catch {
      setError("Couldn't delete. Is Lore running?");
      setBusy(false);
    }
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title="Memory"
      footer={
        <div className="lib-dialog__footer">
          {confirmDelete ? (
            <Button variant="danger" onClick={remove} disabled={busy}>
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
          <div className="lib-dialog__right">
            <Button variant="ghost" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button variant="primary" onClick={save} disabled={!dirty || busy}>
              {busy ? 'Saving…' : 'Save'}
            </Button>
          </div>
        </div>
      }
    >
      <Textarea
        label="Text"
        rows={5}
        value={text}
        onChange={(e) => setText(e.target.value)}
        aria-label="Memory text"
      />
      {categoryOf(memory) !== null && (
        <p className="lib-dialog__hint">
          category <span className="lib-mono">{categoryOf(memory)}</span>
        </p>
      )}
      {error !== null && <p className="lib-dialog__error">{error}</p>}
    </Dialog>
  );
}
