import { useEffect, useRef, type ReactNode } from 'react';

/**
 * The one page header, shared by every screen: full by default, condensing to a thin
 * glass bar once content scrolls beneath it. `.app-card` carries the `data-scrolled`
 * flag (AppShell sets it), so every page condenses identically.
 *
 * It is deliberately OUT OF FLOW. The header used to be a normal sticky block, which
 * meant collapsing it shortened the scrollable content — and on a page that only just
 * overflows, that removed the overflow entirely, the browser clamped scrollTop back to
 * zero, and the header sprang open again. The visible symptom was a page you had to
 * scroll whose header would never collapse; lowering the scroll threshold would only
 * have turned that into a flicker.
 *
 * So the header lives inside a zero-height sticky slot and overlays the content, and
 * `.app-page` reserves its expanded height as padding. Flow contribution is zero in both
 * states, the scroll height never changes, and collapsing is free of side effects.
 *
 * The reserved height is measured rather than hard-coded, because it depends on how the
 * title and description wrap at the current pane width. A stale measurement only shifts
 * spacing slightly — it cannot reintroduce the loop.
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
  const headRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const head = headRef.current;
    const scroller = head?.closest<HTMLElement>('.app-card__scroll');
    const card = head?.closest<HTMLElement>('.app-card');
    if (head === null || !scroller || !card) {
      return undefined;
    }
    const measure = (): void => {
      // Only a measurement taken while expanded describes the space to reserve.
      if (card.dataset['scrolled'] === 'true') {
        return;
      }
      scroller.style.setProperty('--page-head-h', `${head.offsetHeight}px`);
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(head);
    return () => observer.disconnect();
  }, []);

  return (
    <div className="page-head-slot">
      <header className="page-head" ref={headRef}>
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
    </div>
  );
}
