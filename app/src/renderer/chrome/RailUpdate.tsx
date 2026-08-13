import { useEffect, useState, type ReactNode } from 'react';

import { Button, Dialog } from '../design-system';

interface Pending {
  version: string;
  notes: string;
}

/**
 * The rail's update affordance: a compact Coastal-blue card, shown only once an update has
 * finished
 * downloading, sitting just above the ambient "Now" block. Lore never installs on its own —
 * clicking the card opens the changelog, and the restart is the user's explicit choice from
 * there (or from the tray). This is the transparency posture for an open-source app: an update
 * is something you're shown and decide, not something that swaps itself in behind you.
 *
 * Self-contained (like the old banner it replaces): it owns its own state off the `window.lore`
 * bridge rather than being threaded through the rail's props.
 */
export function RailUpdate(): JSX.Element | null {
  const [update, setUpdate] = useState<Pending | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    let active = true;
    // Catch up if the download completed while this window was closed — that push was missed.
    void window.lore.getUpdateState().then((state) => {
      if (active && state) {
        setUpdate(state);
      }
    });
    const unsubscribe = window.lore.onUpdateReady((info) => setUpdate(info));
    return () => {
      active = false;
      unsubscribe();
    };
  }, []);

  if (!update) {
    return null;
  }
  const versionKnown = update.version !== 'a new version';

  return (
    <>
      <button
        type="button"
        className="rail-update"
        onClick={() => setOpen(true)}
        title={
          versionKnown
            ? `Lore ${update.version} is ready — see what's new`
            : `A new Lore version is ready — see what's new`
        }
      >
        <span className="rail-update__text">
          <span className="rail-update__title">Update ready</span>
          <span className="rail-update__meta">
            {update.version} · What’s new
          </span>
        </span>
      </button>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title={versionKnown ? `What’s new in ${update.version}` : `What’s new`}
        footer={
          <div className="update-modal__footer">
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Later
            </Button>
            <Button
              variant="primary"
              icon="refresh-cw"
              onClick={() => window.lore.installUpdate()}
            >
              Restart to update
            </Button>
          </div>
        }
      >
        <div className="update-notes">{renderNotes(update.notes)}</div>
      </Dialog>
    </>
  );
}

/**
 * A deliberately small markdown renderer for GitHub's auto-generated release notes — headings,
 * bullet lists, and bold, which is all those notes ever use. Everything is built from text
 * nodes and React elements (never `dangerouslySetInnerHTML`), so the feed can't inject markup.
 * Bare GitHub URLs are shortened to readable tokens rather than shown as links: the repo is
 * private, so the raw links would 404 for the very users this modal exists to inform.
 */
function renderNotes(markdown: string): ReactNode {
  const trimmed = markdown.trim();
  if (!trimmed) {
    return (
      <p className="update-notes__empty">
        Release notes aren’t available for this version.
      </p>
    );
  }

  const blocks: ReactNode[] = [];
  let bullets: string[] = [];
  let key = 0;

  const flushBullets = (): void => {
    if (bullets.length > 0) {
      const items = bullets;
      blocks.push(
        <ul key={key++}>
          {items.map((text, index) => (
            <li key={index}>{inline(text)}</li>
          ))}
        </ul>,
      );
      bullets = [];
    }
  };

  for (const raw of trimmed.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.length === 0) {
      flushBullets();
      continue;
    }
    const heading = /^#{1,6}\s+(.*)$/.exec(line);
    if (heading) {
      flushBullets();
      blocks.push(<h4 key={key++}>{inline(heading[1])}</h4>);
      continue;
    }
    const bullet = /^[*-]\s+(.*)$/.exec(line);
    if (bullet) {
      bullets.push(bullet[1]);
      continue;
    }
    flushBullets();
    blocks.push(<p key={key++}>{inline(line)}</p>);
  }
  flushBullets();

  return blocks;
}

/** Inline formatting: shorten GitHub links to tokens, then render `**bold**` runs. */
function inline(text: string): ReactNode[] {
  const shortened = text
    .replace(
      /https?:\/\/github\.com\/[^/\s]+\/[^/\s]+\/pull\/(\d+)/g,
      (_match, number) => `#${number}`,
    )
    .replace(
      /https?:\/\/github\.com\/[^/\s]+\/[^/\s]+\/compare\/(\S+)/g,
      (_match, range) => range,
    );

  const nodes: ReactNode[] = [];
  const bold = /\*\*([^*]+)\*\*/g;
  let last = 0;
  let match: RegExpExecArray | null;
  let key = 0;
  while ((match = bold.exec(shortened)) !== null) {
    if (match.index > last) {
      nodes.push(shortened.slice(last, match.index));
    }
    nodes.push(<strong key={key++}>{match[1]}</strong>);
    last = match.index + match[0].length;
  }
  if (last < shortened.length) {
    nodes.push(shortened.slice(last));
  }
  return nodes;
}
