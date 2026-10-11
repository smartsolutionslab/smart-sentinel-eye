import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { federation } from '@module-federation/vite';

// Mirrors apps/management-cameras/vite.config.ts line for line (issue #2780
// decision record) — read at config-build time, not hand-copied, so a human
// bump to one side without the other is caught by
// scripts/singleton-versions.mjs's cross-package comparison.
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

function readSharedPackageVersion(): string {
  const manifest: { version: string } = JSON.parse(
    readFileSync(fileURLToPath(new URL('../shared/package.json', import.meta.url)), 'utf8'),
  );
  return manifest.version;
}

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
  '@smart-sentinel-eye/shared/': { singleton: true, strictVersion: true, requiredVersion: readSharedPackageVersion() },
};

// Aspire injects backend service URLs as environment variables (ADR-0074).
// Dev port 5177 is the next free one after 5173/5174/5175/5176 (issue #2780
// decision record).

export default defineConfig({
  plugins: [
    react(),
    // Federation is off under Vitest, mirroring management-cameras's own
    // reasoning: unit tests import modules directly, and the plugin's dts
    // worker has no reason to run under the test runner.
    ...(process.env.VITEST
      ? []
      : [
          federation({
            name: 'eventIngestion',
            filename: 'remoteEntry.js',
            exposes: {
              './EventIngestionSurface': './src/EventIngestionSurface.tsx',
            },
            shared,
            dts: false,
          }),
        ]),
  ],
  server: {
    port: 5177,
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
