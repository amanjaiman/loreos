import { app, BrowserWindow, dialog, ipcMain, Menu } from 'electron';
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

/** Height of the app's own drag strip; the native controls are sized to match (v2-004). */
const TITLE_BAR_HEIGHT = 44;

/** Coastal (light) window-control colours — the first paint, before the renderer reports in. */
const DEFAULT_OVERLAY = { color: '#fbf7ef', symbolColor: '#565049' };

/**
 * The application menu is *hidden*, not removed. `Menu.setApplicationMenu(null)` also
 * unregisters the accelerators the roles carry, which on Windows silently breaks
 * Ctrl+C/V/X/A/Z inside every text input. Marking the top-level items `visible: false`
 * keeps the accelerators and renders no menu bar (v2-004 T001, AC 3).
 */
const installHiddenMenu = (): void => {
  const template: Electron.MenuItemConstructorOptions[] = [
    { role: 'editMenu', visible: false },
    // Devtools only: harmless in production because the item never renders.
    { role: 'viewMenu', visible: false },
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
};

const createWindow = (): void => {
  const mainWindow = new BrowserWindow({
    width: 1180,
    height: 760,
    minWidth: 900,
    minHeight: 600,
    title: 'Lore',
    backgroundColor: '#fbf7ef', // Coastal --bg, avoids a white flash before styles load
    // Frameless, but NOT `frame: false` — that removes the non-client area and with it
    // Windows 11 Snap Layouts (the flyout on hover over Maximize). 'hidden' + an overlay
    // keeps real, snap-aware controls in a strip whose colours we own (v2-004 AC 2).
    titleBarStyle: 'hidden',
    titleBarOverlay: { ...DEFAULT_OVERLAY, height: TITLE_BAR_HEIGHT },
    webPreferences: {
      // contextIsolation on / nodeIntegration off (Electron defaults). The renderer is
      // a pure client of the local API (005) over loopback fetch — there is no
      // account/sync/cloud IPC, by design (constitution §1–§3).
      preload: MAIN_WINDOW_PRELOAD_WEBPACK_ENTRY,
    },
  });

  void mainWindow.loadURL(MAIN_WINDOW_WEBPACK_ENTRY);
};

/** `#rrggbb` only — the renderer is untrusted input like any other caller. */
const isHexColor = (value: unknown): value is string =>
  typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value);

// The overlay's colours are fixed at construction and do not follow CSS, so the renderer
// reports the resolved theme colours after each switch (v2-004 AC 4).
ipcMain.on(
  'lore:set-titlebar',
  (event, color: unknown, symbolColor: unknown): void => {
    if (!isHexColor(color) || !isHexColor(symbolColor)) {
      return;
    }
    const window = BrowserWindow.fromWebContents(event.sender);
    // setTitleBarOverlay only exists where an overlay is in use (Windows/Linux).
    if (window !== null && typeof window.setTitleBarOverlay === 'function') {
      window.setTitleBarOverlay({
        color,
        symbolColor,
        height: TITLE_BAR_HEIGHT,
      });
    }
  },
);

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
  installHiddenMenu();
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
