import { type ReactNode } from 'react';

/**
 * The one page header, shared by every screen.
 *
 * It renders full — eyebrow, title, description — and condenses to a thin glass bar once
 * content scrolls beneath it, rather than scrolling away and leaving nothing behind.
 * `.app-card` carries the `data-scrolled` flag (AppShell sets it from the pane's scroll
 * position), so the state lives in one place and every page condenses identically.
 *
 * Home used to have a different, much smaller header than the other three, so moving
 * between tabs felt like moving between apps. Same component everywhere now.
 *
 * It mounts as a SIBLING of `.app-page`, not inside it: the bar spans the full pane while
 * its contents stay aligned to the page's column, and — importantly — it carries no
 * margins. Sticky positioning constrains the margin box, so a header pulled up with a
 * negative margin sticks that far down the scrollport and leaves a band that content
 * visibly scrolls through. Keeping margins at zero removes that trap for good.
 */
export function PageHeader({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow: string;
  title: string;
  description: string;
  action?: ReactNode;
}): JSX.Element {
  return (
    <header className="page-head">
      <div className="page-head__inner">
        <div className="page-head__copy">
          <span className="page-head__eyebrow">{eyebrow}</span>
          <h1 className="page-head__title">{title}</h1>
          <p className="page-head__description">{description}</p>
        </div>
        {action !== undefined && (
          <div className="page-head__action">{action}</div>
        )}
      </div>
    </header>
  );
}
