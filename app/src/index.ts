import { app, BrowserWindow, dialog, ipcMain } from 'electron';
import { AgentProcess } from './agentProcess';
import { applySquirrelPathHook } from './windowsIntegration';

// Webpack magic constants injected by Electron Forge's webpack plugin: they
// point at the bundled renderer entry and preload script for dev vs. packaged.
declare const MAIN_WINDOW_WEBPACK_ENTRY: string;
declare const MAIN_WINDOW_PRELOAD_WEBPACK_ENTRY: string;

// On a Squirrel install/update/uninstall the app is launched with a lifecycle flag.
// Keep the bundled CLI on the user PATH (spec 011 T002), then let electron-squirrel-
// startup create/remove shortcuts and quit.
applySquirrelPathHook();
// Handle creating/removing shortcuts on Windows when installing/uninstalling.
// eslint-disable-next-line @typescript-eslint/no-var-requires
if (require('electron-squirrel-startup')) {
  app.quit();
}

// The Lore agent: spawned and supervised for the app's lifetime in a packaged build
// (it in turn supervises memoryd — spec 002). A no-op in dev, where it's run separately.
const agent = new AgentProcess();

const createWindow = (): void => {
  const mainWindow = new BrowserWindow({
    width: 1180,
    height: 760,
    minWidth: 900,
    minHeight: 600,
    title: 'Lore',
    backgroundColor: '#fbf7ef', // Coastal --bg, avoids a white flash before styles load
    webPreferences: {
      // contextIsolation on / nodeIntegration off (Electron defaults). The renderer is
      // a pure client of the local API (005) over loopback fetch — there is no
      // account/sync/cloud IPC, by design (constitution §1–§3).
      preload: MAIN_WINDOW_PRELOAD_WEBPACK_ENTRY,
    },
  });

  void mainWindow.loadURL(MAIN_WINDOW_WEBPACK_ENTRY);
};

// Native document picker for import (T009). The renderer can't obtain a real filesystem
// path on its own; it asks the main process, which returns the chosen PDF's absolute path.
ipcMain.handle('lore:pick-document', async (): Promise<string | null> => {
  const result = await dialog.showOpenDialog({
    title: 'Choose a document to import',
    properties: ['openFile'],
    filters: [{ name: 'PDF documents', extensions: ['pdf'] }],
  });
  return result.canceled || result.filePaths.length === 0
    ? null
    : result.filePaths[0];
});

app.on('ready', () => {
  agent.start();
  createWindow();
});

// Quit when all windows are closed, except on macOS where apps conventionally
// stay active until the user quits explicitly.
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

// Tear the agent (and thus memoryd) down cleanly when the app exits.
app.on('will-quit', () => {
  agent.stop();
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
  }
});
