// The main-process bridge exposed on window by preload.ts. Global augmentation (this
// file is a script, not a module), so it merges with the Window members declared elsewhere.

interface Window {
  lore: {
    /** Open a native PDF picker; resolves to an absolute path, or null if cancelled. */
    pickDocument: () => Promise<string | null>;
    /** Minimise / maximise / close, driven by the renderer's own window controls. */
    windowCommand: (command: 'minimize' | 'toggle-maximize' | 'close') => void;
    /** Current maximised state, for the correct restore glyph on first paint. */
    isMaximized: () => Promise<boolean>;
    /** Subscribe to maximise/restore; returns an unsubscribe function. */
    onWindowState: (listener: (maximized: boolean) => void) => () => void;
    /** Subscribe to "an update downloaded and is ready to install"; returns an unsubscribe fn. */
    onUpdateReady: (listener: () => void) => () => void;
    /** Restart now to apply a downloaded update. */
    installUpdate: () => void;
  };
}
