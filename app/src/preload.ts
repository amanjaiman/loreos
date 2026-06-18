// Preload: the narrow, safe bridge between the renderer and the main process. The
// renderer is a pure client of the local API (005) for all data; the only thing it needs
// from the OS is a native file picker for document import (it can't read a real filesystem
// path otherwise). No account/sync/business IPC — by design (constitution §1–§3).
import { contextBridge, ipcRenderer } from 'electron';

contextBridge.exposeInMainWorld('lore', {
  /** Open a native picker for a PDF to import; resolves to an absolute path, or null if cancelled. */
  pickDocument: (): Promise<string | null> =>
    ipcRenderer.invoke('lore:pick-document'),
});
