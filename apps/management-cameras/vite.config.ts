import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { federation } from '@module-federation/vite';

// Plan 316 §3's shared-dependency table — the same requiredVersion pins as
// the shell (apps/management-web/vite.config.ts). A human bump to one side
// without the other is caught at build time by scripts/singleton-versions.mjs,
// before it would otherwise surface only as a runtime strictVersion mismatch
// (T001 finding 3: that mismatch is not reliably a catchable loadRemote
// rejection, so the build-time guard is the one mechanism that actually
// fires on drift).
const shared = {
  react: { singleton: true, strictVersion: true, requiredVersion: '19.3.0' },
  'react-dom': { singleton: true, strictVersion: true, requiredVersion: '19.3.0' },
  'react-router-dom': { singleton: true, strictVersion: true, requiredVersion: '7.18.4' },
  'react-redux': { singleton: true, strictVersion: true, requiredVersion: '9.3.0' },
  '@reduxjs/toolkit': { singleton: true, strictVersion: true, requiredVersion: '2.12.0' },
  'react-oidc-context': { singleton: true, strictVersion: true, requiredVersion: '3.3.1' },
  // Trailing-slash key: shares every @smart-sentinel-eye/shared subpath
  // import (gateway.ts's token provider state, every api slice, …) as one
  // singleton instance (T001 finding 2), on the precondition that
  // apps/shared/package.json keeps a resolvable root "." export.
  '@smart-sentinel-eye/shared/': { singleton: true, strictVersion: true, requiredVersion: '0.0.0' },
};

// Aspire injects backend service URLs as environment variables (ADR-0074).
// Dev port 5176 is the next free one after 5173/5174/5175 (plan §1).

export default defineConfig({
  plugins: [
    react(),
    // Federation is off under Vitest (plan §3): unit tests import modules
    // directly, and the plugin's dts worker shells out to
    // `tsc --showConfig` at dev-server start, which has no reason to run
    // under the test runner (T001: dts:false is load-bearing for a second,
    // independent reason beyond the one below).
    ...(process.env.VITEST
      ? []
      : [
          federation({
            name: 'cameras',
            filename: 'remoteEntry.js',
            exposes: {
              './CamerasSurface': './src/CamerasSurface.tsx',
            },
            shared,
            // The exposed contract is the shared RemoteSurfaceModule type
            // (plan §2.2), not plugin-generated types.
            dts: false,
          }),
        ]),
  ],
  server: {
    port: 5176,
    strictPort: true,
  },
  build: {
    target: 'esnext',
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    testTimeout: 30_000,
  },
});
