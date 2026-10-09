import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { federation } from '@module-federation/vite';

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
// the shell (apps/management-web/vite.config.ts). A human bump to one side
// without the other is caught at build time by scripts/singleton-versions.mjs,
// before it would otherwise surface only as a runtime strictVersion mismatch
// (T001 finding 3: that mismatch is not reliably a catchable loadRemote
// rejection, so the build-time guard is the one mechanism that actually
// fires on drift).
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
