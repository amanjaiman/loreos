import { useEffect, useRef, useState } from 'react';

import { api, LoreOfflineError, type ImportJob } from '../api';
import { Button, Card, ProgressBar, Select, Textarea } from '../design-system';
import './add.css';

const CATEGORIES = [
  'general',
  'preferences',
  'coding',
  'work',
  'personal',
  'projects',
];

function isTerminal(status: string): boolean {
  return status === 'completed' || status === 'failed';
}

/**
 * Add — seed memory by hand (POST /memories) or import a document (POST /import + poll).
 * Both go through api.ts. Import needs a real file path, so it asks the main process for a
 * native picker (the renderer can't read the filesystem). 009 is shipped, so import is on.
 */
export function Add(): JSX.Element {
  return (
    <div className="app-page">
      <ManualAdd />
      <ImportDoc />
    </div>
  );
}

function ManualAdd(): JSX.Element {
  const [text, setText] = useState('');
  const [category, setCategory] = useState('general');
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const add = async (): Promise<void> => {
    setBusy(true);
    setNote(null);
    setError(null);
    try {
      const res = await api.addMemory({
        text: text.trim(),
        metadata: { category },
      });
      const count = res.results.length;
      setNote(
        count === 0
          ? 'Lore already knew that — nothing new to remember.'
          : `Remembered ${count === 1 ? 'it' : `${count} memories`}.`,
      );
      setText('');
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running — start it and try again."
          : "Couldn't save that. Try again.",
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card eyebrow="// seed a memory" title="Add a memory by hand">
      <Textarea
        label="What should Lore remember?"
        rows={4}
        placeholder="e.g. I prefer concise answers and TypeScript over JavaScript."
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          setNote(null);
        }}
      />
      <div className="add-row">
        <Select
          label="Category"
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          options={CATEGORIES.map((c) => ({ value: c, label: c }))}
        />
        <Button
          variant="primary"
          onClick={add}
          disabled={text.trim().length === 0 || busy}
        >
          {busy ? 'Saving…' : 'Remember'}
        </Button>
      </div>
      {note !== null && <p className="add-note add-note--ok">{note}</p>}
      {error !== null && <p className="add-note add-note--err">{error}</p>}
    </Card>
  );
}

function ImportDoc(): JSX.Element {
  const [job, setJob] = useState<ImportJob | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cancelled = useRef(false);

  useEffect(() => {
    return () => {
      cancelled.current = true;
    };
  }, []);

  const poll = (id: string): void => {
    window.setTimeout(async () => {
      if (cancelled.current) {
        return;
      }
      try {
        const updated = await api.getImport(id);
        if (cancelled.current) {
          return;
        }
        setJob(updated);
        if (!isTerminal(updated.status)) {
          poll(id);
        }
      } catch (e) {
        if (!cancelled.current) {
          setError(
            e instanceof LoreOfflineError
              ? "Lore isn't running."
              : "Couldn't read import status.",
          );
        }
      }
    }, 700);
  };

  const choose = async (): Promise<void> => {
    setError(null);
    const path = await window.lore.pickDocument();
    if (path === null) {
      return;
    }
    try {
      const started = await api.startImport({ path });
      setJob(started);
      if (!isTerminal(started.status)) {
        poll(started.id);
      }
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running — start it and try again."
          : 'That document could not be imported.',
      );
    }
  };

  const running = job !== null && !isTerminal(job.status);

  return (
    <Card eyebrow="// import a document" title="Seed from a PDF">
      <p className="add-lede">
        Lore reads the text, filters out anything sensitive, and threads the
        rest into memory. PDFs only for now.
      </p>
      <Button
        variant="secondary"
        icon="file-up"
        onClick={choose}
        disabled={running}
      >
        {running ? 'Importing…' : 'Choose a PDF'}
      </Button>

      {job !== null && (
        <div className="add-import">
          <div className="add-import__head">
            <span className="add-import__source">{job.source}</span>
            <span className="add-import__pct">
              {Math.round(job.progress * 100)}%
            </span>
          </div>
          <ProgressBar value={Math.round(job.progress * 100)} />
          {job.status === 'completed' && (
            <p className="add-note add-note--ok">
              Imported — {job.memories_created}{' '}
              {job.memories_created === 1 ? 'memory' : 'memories'} kept.
              {job.warning !== undefined &&
                job.warning !== null &&
                ` Note: ${job.warning}`}
            </p>
          )}
          {job.status === 'failed' && (
            <p className="add-note add-note--err">
              {job.error ?? 'The import failed.'}
            </p>
          )}
          {running && (
            <p className="add-note">
              Reading {job.processed_chunks}/{job.total_chunks} sections…
            </p>
          )}
        </div>
      )}
      {error !== null && <p className="add-note add-note--err">{error}</p>}
    </Card>
  );
}
