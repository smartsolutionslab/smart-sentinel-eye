import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { federation } from '@module-federation/vite';
import { fontPreload } from '../shared/src/ui/fonts/fontPreload.ts';

// S1 fix (spec 316 phase-6 review): plan §3 says these pins are "read from
// each package's own package.json" — they were literal strings instead,
// which pass scripts/singleton-versions.mjs's cross-package comparison even
// after going stale against what this app actually depends on (T001's case
// (b): an uncaught `pageerror` at host startup). Read at config-build time,
// not hand-copied.
function readOwnDependencyVersion(dependencyName: string): string {
  const manifest: { dependencies?: Record<string, string> } = JSON.parse(
    readFileSync(fileURLToPath(new URL('./package.json', import.meta.url)), 'utf8'),
  );
  const version = manifest.dependencies?.[dependencyName];
  if (typeof version !== 'string') {
    throw new Error(`"${dependencyName}" is missing from this package's own package.json dependencies`);
  }
  return version;
}

// `@smart-sentinel-eye/shared` is a workspace package: this app's own
// dependency entry for it is the range "workspace:*", not a semver — the
// one concrete version lives in the shared package's own package.json.
function readSharedPackageVersion(): string {
  const manifest: { version: string } = JSON.parse(
    readFileSync(fileURLToPath(new URL('../shared/package.json', import.meta.url)), 'utf8'),
  );
  return manifest.version;
}

// Plan 316 §3's shared-dependency table — the same requiredVersion pins as
// the cameras remote (apps/management-cameras/vite.config.ts).
// scripts/singleton-versions.mjs (T006) catches a human bump to one side
// without the other at build time, before it would otherwise surface only
// as a runtime strictVersion mismatch (T001 finding 3: not reliably a
// catchable loadRemote rejection).
const shared = {
  react: { singleton: true, strictVersion: true, requiredVersion: readOwnDependencyVersion('react') },
  'react-dom': { singleton: true, strictVersion: true, requiredVersion: readOwnDependencyVersion('react-dom') },
  'react-router-dom': {
    singleton: true,
    strictVersion: true,
    requiredVersion: readOwnDependencyVersion('react-router-dom'),
  },
  'react-redux': { singleton: true, strictVersion: true, requiredVersion: readOwnDependencyVersion('react-redux') },
  '@reduxjs/toolkit': {
    singleton: true,
    strictVersion: true,
    requiredVersion: readOwnDependencyVersion('@reduxjs/toolkit'),
  },
  'react-oidc-context': {
    singleton: true,
    strictVersion: true,
    requiredVersion: readOwnDependencyVersion('react-oidc-context'),
  },
  // Trailing-slash key: shares every @smart-sentinel-eye/shared subpath
  // import (gateway.ts's token provider state, every api slice, …) as one
  // singleton instance (T001 finding 2), on the precondition that
  // apps/shared/package.json keeps a resolvable root "." export.
  '@smart-sentinel-eye/shared/': { singleton: true, strictVersion: true, requiredVersion: readSharedPackageVersion() },
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
            // B3 fix (spec 316 phase-6 review): a factory module specifier,
            // not an inline object — @module-federation/vite's own generated
            // runtime-init code does `import $runtimePlugin_0 from "<path>"`
            // then calls the default export as a factory
            // (lib/index.js:4956/4972, read directly). A project-root-relative
            // path with forward slashes, not `fileURLToPath`'s absolute
            // Windows path: the plugin embeds this string RAW into the
            // generated source (no JSON.stringify/escaping), so a backslash
            // is read back as a JS escape sequence — `\n` becomes an actual
            // newline, `\s`/`\a`/`\m` are silently dropped — which mangled the
            // path into garbage and broke the dev server on Windows (seen
            // live against the real Vite dev server, not predicted).
            runtimePlugins: ['./src/app/navigation/mismatchRuntimePlugin.ts'],
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
