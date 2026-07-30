import type { ForgeConfig } from '@electron-forge/shared-types';
import { MakerSquirrel } from '@electron-forge/maker-squirrel';
import { MakerZIP } from '@electron-forge/maker-zip';
import { MakerDeb } from '@electron-forge/maker-deb';
import { MakerRpm } from '@electron-forge/maker-rpm';
import { AutoUnpackNativesPlugin } from '@electron-forge/plugin-auto-unpack-natives';
import { WebpackPlugin } from '@electron-forge/plugin-webpack';
import { FusesPlugin } from '@electron-forge/plugin-fuses';
import { FuseV1Options, FuseVersion } from '@electron/fuses';
import * as fs from 'fs';
import * as path from 'path';

import { mainConfig } from './webpack.main.config';
import { rendererConfig } from './webpack.renderer.config';

// The native payload (agent + CLI + skill + frozen memoryd) is assembled into
// app/native by installer/build.ps1 and bundled as a resource. It only exists during
// a full installer build; the per-PR `app` CI job runs `electron-forge package`
// without it, so include it only when present (a missing extraResource path would
// otherwise fail packaging).
const nativePayload = path.join(__dirname, 'native');
const extraResource = fs.existsSync(nativePayload) ? [nativePayload] : [];

// Code-signing is build-ready but opt-in (spec 011 T002): forge signs the Electron
// app binaries + the Squirrel setup exe when a certificate is configured via env, and
// is a no-op otherwise so the build runs without a cert in hand. The .NET/PyInstaller
// exes bundled under native/ are signed before this step by installer/sign-artifacts.ps1.
// The cert lives in a CI secret, never in the repo.
const certFile = process.env.LORE_SIGN_CERT_FILE;
const windowsSign = certFile
  ? {
      certificateFile: certFile,
      certificatePassword: process.env.LORE_SIGN_CERT_PASSWORD,
      timestampServer:
        process.env.LORE_SIGN_TIMESTAMP_URL ?? 'http://timestamp.digicert.com',
    }
  : undefined;

const config: ForgeConfig = {
  packagerConfig: {
    asar: true,
    // The native payload (agent + CLI + skill + frozen memoryd) lands in
    // resources/native; the CLI finds LoreAgent.exe as a sibling and the supervisor
    // finds memoryd at memoryd/lore-memoryd.exe (spec 011 T001).
    extraResource,
    // Signs the Electron app binaries when a cert is configured (no-op otherwise).
    ...(windowsSign ? { windowsSign } : {}),
  },
  rebuildConfig: {},
  makers: [
    new MakerSquirrel({
      name: 'Lore',
      setupExe: 'LoreSetup.exe',
      ...(windowsSign ? { windowsSign } : {}),
    }),
    new MakerZIP({}, ['darwin']),
    new MakerRpm({}),
    new MakerDeb({}),
  ],
  plugins: [
    new AutoUnpackNativesPlugin({}),
    new WebpackPlugin({
      mainConfig,
      // The dev server sends a Content-Security-Policy header; its default omits a
      // connect-src, so it falls back to `default-src 'self'` and Chromium blocks the
      // renderer's fetch() to the loopback API — the request never leaves the renderer,
      // surfacing as LoreOfflineError ("Lore isn't running") no matter how the agent's
      // CORS is configured. Allow the loopback API origin (and the webpack HMR socket)
      // here; keep `script-src 'unsafe-eval'` so eval-source-map dev source maps work.
      // Packaged builds load from file:// with no dev server, so this header is dev-only.
      devContentSecurityPolicy:
        "default-src 'self' 'unsafe-inline' data:; " +
        "script-src 'self' 'unsafe-eval' 'unsafe-inline' data:; " +
        "connect-src 'self' http://127.0.0.1:7842 ws://localhost:3000",
      renderer: {
        config: rendererConfig,
        entryPoints: [
          {
            html: './src/index.html',
            js: './src/renderer.tsx',
            name: 'main_window',
            preload: {
              js: './src/preload.ts',
            },
          },
        ],
      },
    }),
    // Fuses are used to enable/disable various Electron functionality
    // at package time, before code signing the application
    new FusesPlugin({
      version: FuseVersion.V1,
      [FuseV1Options.RunAsNode]: false,
      [FuseV1Options.EnableCookieEncryption]: true,
      [FuseV1Options.EnableNodeOptionsEnvironmentVariable]: false,
      [FuseV1Options.EnableNodeCliInspectArguments]: false,
      [FuseV1Options.EnableEmbeddedAsarIntegrityValidation]: true,
      [FuseV1Options.OnlyLoadAppFromAsar]: true,
    }),
  ],
};

export default config;
