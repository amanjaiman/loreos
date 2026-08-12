import { app, BrowserWindow, dialog, ipcMain, Menu, screen } from 'electron';
import { AgentProcess } from './agentProcess';
import { initAutoUpdates } from './autoUpdate';
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

// The single app window, held at module scope so the auto-updater can tell whether the
// user is looking at the app (prompt to restart) or not (install silently). Null between
// windows (closed, or before first create).
let mainWindow: BrowserWindow | null = null;

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

/**
 * Opening size. Onboarding's card is 650px before its own content, and steps 2-6 exceed
 * that at the old 760px height — the user had to scroll through setup, which is the worst
 * possible first impression. Roomier by default, but clamped to the display's work area so
 * a 1366x768 laptop doesn't get a window taller than its screen.
 */
const preferredSize = (): { width: number; height: number } => {
  const { width: availW, height: availH } =
    screen.getPrimaryDisplay().workAreaSize;
  return {
    width: Math.min(1320, Math.max(900, availW - 80)),
    height: Math.min(900, Math.max(600, availH - 80)),
  };
};

const createWindow = (): void => {
  const win = new BrowserWindow({
    ...preferredSize(),
    minWidth: 900,
    minHeight: 600,
    title: 'Lore',
    backgroundColor: '#fbf7ef', // Coastal --bg, avoids a white flash before styles load
    // Fully frameless, with the window controls drawn by the renderer.
    //
    // This started as `titleBarStyle: 'hidden'` + `titleBarOverlay`, which keeps the real
    // OS buttons (and with them Windows 11 Snap Layouts). The overlay is painted by the OS
    // as an OPAQUE rectangle, though, and no combination of colour, ground gradient or
    // shadow clamping made it disappear into the strip — it stayed visible as a box over
    // the app. Owning the buttons removes that whole class of problem: they are now our
    // elements, on our background, themed by our tokens.
    //
    // The cost is the Snap Layouts flyout on hover over Maximize. Win+Arrow and edge-drag
    // snapping are unaffected. Revert to titleBarOverlay if that flyout matters more than
    // the seam.
    frame: false,
    webPreferences: {
      // contextIsolation on / nodeIntegration off (Electron defaults). The renderer is
      // a pure client of the local API (005) over loopback fetch — there is no
      // account/sync/cloud IPC, by design (constitution §1–§3).
      preload: MAIN_WINDOW_PRELOAD_WEBPACK_ENTRY,
    },
  });

  mainWindow = win;
  win.on('closed', () => {
    if (mainWindow === win) {
      mainWindow = null;
    }
  });

  // The maximize/restore glyph has to follow the real window state, which the user can
  // change without touching our button (double-click the strip, Win+Up, snapping).
  const reportState = (): void => {
    if (!win.isDestroyed()) {
      win.webContents.send('lore:window-state', win.isMaximized());
    }
  };
  win.on('maximize', reportState);
  win.on('unmaximize', reportState);

  void win.loadURL(MAIN_WINDOW_WEBPACK_ENTRY);
};

// Window controls, driven by the renderer's own buttons. The command is validated
// against a closed set — the renderer is untrusted input like any other caller.
ipcMain.on('lore:window-command', (event, command: unknown): void => {
  const window = BrowserWindow.fromWebContents(event.sender);
  if (window === null) {
    return;
  }
  if (command === 'minimize') {
    window.minimize();
  } else if (command === 'toggle-maximize') {
    if (window.isMaximized()) {
      window.unmaximize();
    } else {
      window.maximize();
    }
  } else if (command === 'close') {
    window.close();
  }
});

ipcMain.handle('lore:is-maximized', (event): boolean => {
  const window = BrowserWindow.fromWebContents(event.sender);
  return window !== null && window.isMaximized();
});

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
  // In-app updates via Squirrel + the reused Hazel feed (packaged Windows only; a no-op
  // otherwise). Started after the window exists so it can prompt the user to restart.
  initAutoUpdates(() => mainWindow);
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
