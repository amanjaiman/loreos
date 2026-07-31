// The main-process bridge exposed on window by preload.ts. Global augmentation (this
// file is a script, not a module), so it merges with the Window members declared elsewhere.

interface Window {
  lore: {
    /** Open a native PDF picker; resolves to an absolute path, or null if cancelled. */
    pickDocument: () => Promise<string | null>;
    /** Recolour the native window-control overlay to match the active theme (v2-004). */
    setTitleBar: (color: string, symbolColor: string) => void;
  };
}
