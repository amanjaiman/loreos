// View-only formatting helpers. The brand voice shows numbers terse and in mono with
// units/arrows; times read as calm relative phrases.

export function formatCount(n: number): string {
  return n.toLocaleString('en-US');
}

/** A short relative time like "just now", "2m ago", "3h ago", "5d ago". */
export function formatRelative(iso: string): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return '';
  }
  const seconds = Math.max(0, Math.round((Date.now() - then) / 1000));
  if (seconds < 45) {
    return 'just now';
  }
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes}m ago`;
  }
  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return `${hours}h ago`;
  }
  const days = Math.round(hours / 24);
  return `${days}d ago`;
}
