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
   * The pending update, if one has already downloaded — read on mount, since a window opened
   * after the download completed would have missed the `onUpdateReady` push. Null otherwise.
   */
  getUpdateState: (): Promise<{ version: string; notes: string } | null> =>
    ipcRenderer.invoke('lore:get-update-state'),

  /**
   * Subscribe to "an update finished downloading and is ready to install on restart". Lore
   * never installs on its own now — this is the signal to surface the choice — and carries the
   * release version + notes so the changelog can be shown. Returns an unsubscribe fn.
   */
  onUpdateReady: (
    listener: (info: { version: string; notes: string }) => void,
  ): (() => void) => {
    const handler = (
      _event: unknown,
      info: { version: string; notes: string },
    ): void => listener(info);
    ipcRenderer.on('lore:update-ready', handler);
    return () => ipcRenderer.removeListener('lore:update-ready', handler);
  },

  /** Restart now to apply a downloaded update (Squirrel quitAndInstall). */
  installUpdate: (): void => {
    void ipcRenderer.invoke('lore:install-update');
  },

  /**
   * Start-with-Windows (v2-006 R2). `supported` is false in dev and off Windows, where
   * there is no Squirrel stub to register — the settings toggle explains itself rather
   * than writing a login item that would launch the wrong thing. `enabled` is read from
   * the OS every time, so it cannot drift from what Windows will actually do.
   */
  getAutostart: (): Promise<{ supported: boolean; enabled: boolean }> =>
    ipcRenderer.invoke('lore:autostart-get'),

  /** Register/remove the login item; resolves to the state the OS reports afterwards. */
  setAutostart: (enabled: boolean): Promise<boolean> =>
    ipcRenderer.invoke('lore:autostart-set', enabled),

  /**
   * Tell the main process that something which affects the tray just changed — in
   * practice, that the rail wrote `capture.enabled`. It re-reads the agent rather than
   * trusting the renderer, so this is a hint, not a state push.
   */
  notifyLifecycleChanged: (): void => {
    ipcRenderer.send('lore:lifecycle-refresh');
  },

  /**
   * Whether this build can start a stopped agent (false in dev, where it's run by hand).
   * The app asks so it can omit a Start button that couldn't work.
   */
  canStartLore: (): Promise<boolean> => ipcRenderer.invoke('lore:can-start'),

  /**
   * Start the agent. This is the one lifecycle action that cannot go through `api.ts`:
   * when the agent is stopped there is no local API to call.
   */
  startLore: (): void => {
    void ipcRenderer.invoke('lore:start-agent');
  },

  /**
   * Subscribe to running/paused/stopped as the tray sees it. Lets the rail react to a
   * pause issued from the tray menu without waiting out its own poll. Returns an
   * unsubscribe function.
   */
  onLifecycleState: (
    listener: (state: 'running' | 'paused' | 'stopped') => void,
  ): (() => void) => {
    const handler = (
      _event: unknown,
      state: 'running' | 'paused' | 'stopped',
    ): void => listener(state);
    ipcRenderer.on('lore:lifecycle-state', handler);
    return () => ipcRenderer.removeListener('lore:lifecycle-state', handler);
  },
});
