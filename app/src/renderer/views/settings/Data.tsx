import { useState } from 'react';

import { api, LoreOfflineError } from '../../api';
import { Button, Card, Dialog } from '../../design-system';
import { downloadText } from '../../lib/download';

/**
 * Data settings: export everything Lore knows (JSON or Markdown) and reset all memory
 * behind a confirm dialog. Exports stream from the local API and save locally — data
 * ownership, not lock-in. All via api.ts.
 */
export function Data({ onReset }: { onReset: () => void }): JSX.Element {
  const [busy, setBusy] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const exportJson = async (): Promise<void> => {
    setError(null);
    try {
      const data = await api.exportJson();
      downloadText(
        'lore-export.json',
        JSON.stringify(data, null, 2),
        'application/json',
      );
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running."
          : 'Export failed.',
      );
    }
  };

  const exportMarkdown = async (): Promise<void> => {
    setError(null);
    try {
      const md = await api.exportMarkdown();
      downloadText('lore-export.md', md, 'text/markdown');
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running."
          : 'Export failed.',
      );
    }
  };

  const reset = async (): Promise<void> => {
    setBusy(true);
    setError(null);
    setNote(null);
    try {
      const result = await api.resetData();
      setNote(
        `Forgot ${result.deleted} ${result.deleted === 1 ? 'memory' : 'memories'}.`,
      );
      setConfirmOpen(false);
      onReset();
    } catch (e) {
      setError(
        e instanceof LoreOfflineError
          ? "Lore isn't running."
          : "Couldn't reset.",
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="set-stack">
      <Card eyebrow="// export" title="Take your memory with you">
        <p className="set-subtle">
          Export everything Lore knows about you. Your data, your machine — no
          lock-in.
        </p>
        <div className="set-actions set-actions--start">
          <Button variant="secondary" icon="download" onClick={exportJson}>
            Export JSON
          </Button>
          <Button variant="secondary" icon="download" onClick={exportMarkdown}>
            Export Markdown
          </Button>
        </div>
      </Card>

      <Card eyebrow="// reset" title="Forget everything">
        <p className="set-subtle">
          Permanently delete every memory Lore has kept. The activity log is
          left intact. This can't be undone.
        </p>
        <div className="set-actions set-actions--start">
          <Button
            variant="danger"
            icon="trash-2"
            onClick={() => setConfirmOpen(true)}
          >
            Reset all memory
          </Button>
        </div>
        {note !== null && <p className="set-note set-note--ok">{note}</p>}
      </Card>

      {error !== null && <p className="set-note set-note--err">{error}</p>}

      <Dialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        title="Forget everything?"
        footer={
          <div className="set-dialog__footer">
            <Button
              variant="ghost"
              onClick={() => setConfirmOpen(false)}
              disabled={busy}
            >
              Cancel
            </Button>
            <Button variant="danger" onClick={reset} disabled={busy}>
              {busy ? 'Forgetting…' : 'Yes, forget everything'}
            </Button>
          </div>
        }
      >
        <p className="set-subtle">
          Every memory will be permanently deleted. Consider exporting first.
          This can't be undone.
        </p>
      </Dialog>
    </div>
  );
}
