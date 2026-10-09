// scripts/singleton-versions.mjs
//
// Plan 316 §3 / §6 test 9 (tasks.md T007, US3) — the build-time guard that
// catches the human edit module-federation's own `strictVersion: true`
// would otherwise only surface at runtime (T001 finding 3 found that
// surfacing unreliable: a remote-stricter mismatch never rejects
// `loadRemote`'s promise, so this build-time check is the one mechanism
// that actually fires on a pinned-version-string drift between the shell
// and a remote).
//
// `checkSingletonVersions({ packages })` is a pure function over fixture
// `{ path, manifest }` pairs — no file I/O — mirroring
// scripts/node-types-alignment.mjs's split between a pure checker and a
// `readFederationPackages(repositoryRoot)` reader that is the one piece of
// I/O. The marker a package must carry to be compared at all is the
// presence of `@module-federation/vite` in its own `devDependencies` (plan
// §3: "reads every apps/*/package.json that declares a federation role").
//
// Picked up by the root `test:guards` script (`node --test
// "scripts/**/*.test.mjs" ...`) via scripts/singleton-versions.test.mjs —
// no package.json edit needed.

import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';

const FEDERATION_MARKER = '@module-federation/vite';

// Plan 316 §3's shared-dependency table — the singletons whose identity is
// load-bearing (hooks/context, router context, the store's `<Provider>`,
// `useAuth`'s `AuthProvider`, and every @smart-sentinel-eye/shared subpath
// via the trailing-slash key). A dependency not pinned by a given package is
// not compared for that package — only packages that actually declare it.
const SINGLETON_MODULES = [
  'react',
  'react-dom',
  'react-router-dom',
  'react-redux',
  '@reduxjs/toolkit',
  'react-oidc-context',
  '@smart-sentinel-eye/shared',
];

function isFederationRole(manifest) {
  return typeof manifest?.devDependencies?.[FEDERATION_MARKER] === 'string';
}

export function checkSingletonVersions({ packages }) {
  const federationPackages = packages.filter((pkg) => isFederationRole(pkg.manifest));
  const problems = [];

  for (const moduleName of SINGLETON_MODULES) {
    // S1 fix (spec 316 phase-6 review): a singleton no federation-role
    // package declares anywhere is simply unused — nothing to compare,
    // nothing at risk. Once at least one package shares it, every other
    // federation-role package must pin it too, as a real `dependencies`
    // entry — a package that is missing it, or that only has it under
    // `devDependencies` (previously silently excluded from the comparison
    // by the old string-typed-pin filter, rather than reported), is a
    // problem in its own right, not merely "nothing to compare".
    const anyPackageUsesIt = federationPackages.some(
      (pkg) =>
        typeof pkg.manifest.dependencies?.[moduleName] === 'string' ||
        typeof pkg.manifest.devDependencies?.[moduleName] === 'string',
    );
    if (!anyPackageUsesIt) {
      continue;
    }

    const pins = [];
    for (const pkg of federationPackages) {
      const dependencyVersion = pkg.manifest.dependencies?.[moduleName];
      if (typeof dependencyVersion === 'string') {
        pins.push({ path: pkg.path, version: dependencyVersion });
        continue;
      }

      const devDependencyVersion = pkg.manifest.devDependencies?.[moduleName];
      if (typeof devDependencyVersion === 'string') {
        problems.push(
          `${moduleName} is pinned in ${pkg.path}'s devDependencies, not its dependencies — a shared ` +
            'module-federation singleton must be a real runtime dependency of every federation-role package ' +
            'that shares it (plan 316 §3).',
        );
        continue;
      }

      problems.push(
        `${moduleName} is shared by another federation-role package but missing from ${pkg.path}'s own ` +
          'dependencies — every federation-role package sharing a singleton must pin it too (plan 316 §3).',
      );
    }

    const distinctVersions = new Set(pins.map((pin) => pin.version));
    if (distinctVersions.size <= 1) {
      continue;
    }

    const describedPins = pins.map((pin) => `${pin.version} (${pin.path})`).join(', ');
    problems.push(
      `${moduleName} is pinned to different versions across federation-role packages: ${describedPins} — ` +
        'every app that declares the @module-federation/vite marker must pin this shared singleton identically ' +
        '(plan 316 §3).',
    );
  }

  return problems;
}

export function readFederationPackages(repositoryRoot) {
  const appsDirectory = path.join(repositoryRoot, 'apps');
  const appNames = readdirSync(appsDirectory, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();

  const packages = [];
  for (const appName of appNames) {
    // Forward-slash, repository-relative — not `path.join`, whose separator
    // is platform-dependent and would break every path-shaped assertion on
    // Windows (memory: source-scanning tests need slash normalising).
    const relativePath = `apps/${appName}/package.json`;
    const absolutePath = path.join(appsDirectory, appName, 'package.json');
    const manifest = JSON.parse(readFileSync(absolutePath, 'utf8'));
    packages.push({ path: relativePath, manifest });
  }

  return packages.filter((pkg) => isFederationRole(pkg.manifest));
}
