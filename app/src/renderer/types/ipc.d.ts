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
    /** The pending downloaded update, or null — read on mount to catch a missed push. */
    getUpdateState: () => Promise<{ version: string; notes: string } | null>;
    /** Subscribe to "an update downloaded and is ready"; carries version + notes. */
    onUpdateReady: (
      listener: (info: { version: string; notes: string }) => void,
    ) => () => void;
    /** Restart now to apply a downloaded update. */
    installUpdate: () => void;
    /** Start-with-Windows: `supported` is false in dev and off Windows (v2-006 R2). */
    getAutostart: () => Promise<{ supported: boolean; enabled: boolean }>;
    /** Register/remove the login item; resolves to the state the OS reports afterwards. */
    setAutostart: (enabled: boolean) => Promise<boolean>;
    /** Hint to the main process that the tray's view of capture may be stale. */
    notifyLifecycleChanged: () => void;
    /** Whether this build can start a stopped agent (false in dev). */
    canStartLore: () => Promise<boolean>;
    /** Start the agent — the one lifecycle action that can't go through `api.ts`. */
    startLore: () => void;
    /** Subscribe to running/paused/stopped as the tray sees it; returns an unsubscribe fn. */
    onLifecycleState: (
      listener: (state: 'running' | 'paused' | 'stopped') => void,
    ) => () => void;
  };
}
