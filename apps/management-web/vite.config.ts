import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { federation } from '@module-federation/vite';
import { fontPreload } from '../shared/src/ui/fonts/fontPreload.ts';

// Plan 316 §3's shared-dependency table — the same requiredVersion pins as
// the cameras remote (apps/management-cameras/vite.config.ts).
// scripts/singleton-versions.mjs (T006) catches a human bump to one side
// without the other at build time, before it would otherwise surface only
// as a runtime strictVersion mismatch (T001 finding 3: not reliably a
// catchable loadRemote rejection).
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
// Local dev port chosen to match the Aspire JS resource wiring.

export default defineConfig({
  plugins: [
    react(),
    // The sign-in screen's <h1> and body text are this app's first-paint
    // faces: Sans SemiBold for the heading, Sans Regular for the body.
    fontPreload(['IBMPlexSans-Regular-Latin1.woff2', 'IBMPlexSans-SemiBold-Latin1.woff2']),
    // Federation is off under Vitest (plan §3): unit tests import modules
    // directly, and the plugin's dts worker shells out to
    // `tsc --showConfig` at dev-server start, which has no reason to run
    // under the test runner.
    ...(process.env.VITEST
      ? []
      : [
          federation({
            name: 'shell',
            // Empty: the shell never declares the cameras remote statically
            // here. `RemoteSurface` (navigation/RemoteSurface.tsx) calls
            // `registerRemotes`/`loadRemote` itself, at runtime, only once an
            // entitled session renders a route that owns it (FR-013) — the
            // remote's location comes from `navigation/remotes.ts`, which
            // reads the Aspire-injected VITE_CAMERAS_REMOTE_URL.
            remotes: {},
            shared,
            // The exposed contract is the shared RemoteSurfaceModule type
            // (plan §2.2), not plugin-generated types.
            dts: false,
          }),
        ]),
  ],
  server: {
    port: 5173,
    strictPort: true,
  },
  build: {
    target: 'esnext',
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // A wait allowed 10 s (src/test/setup.ts) that is killed at Vitest's 5 s
    // default has not been fixed — its error message has been changed from
    // "Unable to find an element" to "Test timed out in 5000ms", which names
    // nothing. Measured, not predicted: with the deadline raised and this
    // default left alone, a contended run failed exactly that way (spec 216
    // §Claim 8), and the failing test consumed 4630 ms under contention even
    // while still capped at the old 1000 ms deadline.
    testTimeout: 30_000,
  },
});
