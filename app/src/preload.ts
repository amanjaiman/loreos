// Preload: the narrow, safe bridge between the renderer and the main process. The
// renderer is a pure client of the local API (005) for all data; the only thing it needs
// from the OS is a native file picker for document import (it can't read a real filesystem
// path otherwise). No account/sync/business IPC — by design (constitution §1–§3).
import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('lore', {
  /** Open a native picker for a PDF to import; resolves to an absolute path, or null if cancelled. */
  pickDocument: (): Promise<string | null> =>
    ipcRenderer.invoke('lore:pick-document'),

  /** Minimise / maximise / close, driven by the renderer's own window controls. */
  windowCommand: (command: 'minimize' | 'toggle-maximize' | 'close'): void =>
    ipcRenderer.send('lore:window-command', command),

  /** Current maximised state, for the correct restore glyph on first paint. */
  isMaximized: (): Promise<boolean> => ipcRenderer.invoke('lore:is-maximized'),

  /**
   * Subscribe to maximise/restore. The window state changes without our button being
   * touched — double-clicking the strip, Win+Up, edge snapping — so the glyph has to
   * follow the window rather than our own last click. Returns an unsubscribe function.
   */
  onWindowState: (listener: (maximized: boolean) => void): (() => void) => {
    const handler = (_event: unknown, maximized: boolean): void =>
      listener(maximized);
    ipcRenderer.on('lore:window-state', handler);
    return () => ipcRenderer.removeListener('lore:window-state', handler);
  },

  /**
   * Subscribe to "an update finished downloading and is ready to install on restart". The
   * main process only emits this while the window is visible (otherwise it installs
   * silently), so this fires only when there's a user to prompt. Returns an unsubscribe fn.
   */
  onUpdateReady: (listener: () => void): (() => void) => {
    const handler = (): void => listener();
    ipcRenderer.on('lore:update-ready', handler);
    return () => ipcRenderer.removeListener('lore:update-ready', handler);
  },

  /** Restart now to apply a downloaded update (Squirrel quitAndInstall). */
  installUpdate: (): void => {
    void ipcRenderer.invoke('lore:install-update');
  },
});
