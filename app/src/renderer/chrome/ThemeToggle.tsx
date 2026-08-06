import { useEffect, useState } from 'react';

import { applyLoreTheme, LORE_THEMES } from '../design-system';
import { Icon } from '../lib/Icon';

// Must match the design system's ThemeSelector default, so a preference set by either
// control is read by the other and survives this component replacing it.
const STORAGE_KEY = 'lore-theme-preset';

const ICONS: Record<string, string> = {
  coastal: 'sun',
  'coastal-dark': 'moon',
};

function initialThemeId(): string {
  let saved: string | null = null;
  try {
    saved = localStorage.getItem(STORAGE_KEY);
  } catch {
    // Storage can be unavailable; fall through to the OS preference.
  }
  if (saved !== null && LORE_THEMES.some((t) => t.id === saved)) {
    return saved;
  }
  const prefersDark = window.matchMedia?.(
    '(prefers-color-scheme: dark)',
  ).matches;
  const bySystem = LORE_THEMES.find((t) =>
    prefersDark ? t.mode === 'dark' : t.mode === 'light',
  );
  return bySystem?.id ?? LORE_THEMES[0].id;
}

/**
 * The rail's theme control: two icon buttons, nothing else.
 *
 * The design system's `ThemeSelector` renders a labelled segmented control with colour
 * dots — right for a settings page, too loud for a 236px rail where it sits below the
 * ambient block and competes with it. This keeps the system's theme records, storage key
 * and `applyLoreTheme` so behaviour is identical; only the presentation is compact.
 */
export function ThemeToggle(): JSX.Element {
  const [activeId, setActiveId] = useState<string>(initialThemeId);

  useEffect(() => {
    const theme = LORE_THEMES.find((t) => t.id === activeId) ?? LORE_THEMES[0];
    applyLoreTheme(theme);
  }, [activeId]);

  const select = (id: string): void => {
    setActiveId(id);
    try {
      localStorage.setItem(STORAGE_KEY, id);
    } catch {
      // A themed session that can't persist is better than a crash.
    }
  };

  return (
    <div className="theme-toggle" role="group" aria-label="Theme">
      {LORE_THEMES.map((theme) => (
        <button
          key={theme.id}
          type="button"
          className="theme-toggle__opt"
          aria-pressed={theme.id === activeId}
          aria-label={theme.label}
          title={theme.label}
          onClick={() => select(theme.id)}
        >
          <Icon name={ICONS[theme.id] ?? 'sun'} size={15} />
        </button>
      ))}
    </div>
  );
}
