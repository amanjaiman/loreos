import { Button } from '../design-system';
import { Icon } from '../lib/Icon';

/**
 * The calm "Lore isn't running" state shown when the local API can't be reached, instead
 * of an error (spec 010 resilience requirement). T011 reuses this across views.
 */
export function OfflineNotice({
  onRetry,
}: {
  onRetry: () => void;
}): JSX.Element {
  return (
    <div className="offline">
      <div className="offline__card">
        <span className="offline__glyph">
          <Icon name="moon" size={28} />
        </span>
        <h1 className="offline__title">Lore isn't running</h1>
        <p className="offline__body">
          The Lore agent isn't responding yet. Once it's running, your memory
          and settings appear here — nothing was lost.
        </p>
        <Button variant="primary" icon="refresh-cw" onClick={onRetry}>
          Try again
        </Button>
      </div>
    </div>
  );
}
