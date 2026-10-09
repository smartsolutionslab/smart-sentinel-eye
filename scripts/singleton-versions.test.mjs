// Plan 316 §3 / §6 test 9 (tasks.md T006, US3) — the build-time guard that
// catches the human edit module-federation's own `strictVersion: true` would
// otherwise only surface at runtime (plan.md §3): a pinned-version-string
// drift between the shell and a remote for one of the shared singletons
// (react, react-dom, react-router-dom, react-redux, @reduxjs/toolkit,
// react-oidc-context, @smart-sentinel-eye/shared).
//
// `checkSingletonVersions({ packages })` is a pure function over fixture
// `{ path, manifest }` pairs — no file I/O — mirroring
// scripts/node-types-alignment.mjs's split between a pure checker and a
// `readFederationPackages(repositoryRoot)` reader that is the one piece of
// I/O (plan.md §3: "reads every apps/*/package.json that declares a
// federation role"). The marker is the presence of `@module-federation/vite`
// in a package's `devDependencies` — a package without it (today, every
// `apps/*/package.json`) is not part of the comparison at all.
//
// Red: `scripts/singleton-versions.mjs` does not exist yet (US3 is new
// behaviour, tasks.md's colour table). T007 (frontend-engineer) creates it,
// alongside the remote package that first declares the marker, and must
// leave these assertions unmodified.
//
// Picked up by the root `test:guards` script (`node --test
// "scripts/**/*.test.mjs" ...`) — no package.json edit needed.

import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';
import { checkSingletonVersions, readFederationPackages } from './singleton-versions.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// ---- fixture builders -------------------------------------------------------

// A non-federation package (today's shape of every apps/*/package.json): no
// `@module-federation/vite` marker, so it is excluded from the comparison
// however its own versions are pinned.
function plainPackage(path_, overrides = {}) {
  return {
    path: path_,
    manifest: {
      name: path_,
      dependencies: {
        react: '19.3.0',
        'react-dom': '19.3.0',
        ...overrides.dependencies,
      },
      devDependencies: { ...overrides.devDependencies },
    },
  };
}

// A federation-role package (shell or remote, plan.md §3): carries
// `@module-federation/vite` in devDependencies, and the shared singletons as
// plain dependencies, pinned exactly (no range) as plan.md §3's table
// prescribes.
function federationPackage(path_, { singletons = {}, devDependencies = {} } = {}) {
  return {
    path: path_,
    manifest: {
      name: path_,
      dependencies: {
        react: '19.3.0',
        'react-dom': '19.3.0',
        'react-router-dom': '7.18.4',
        'react-redux': '9.3.0',
        '@reduxjs/toolkit': '2.12.0',
        'react-oidc-context': '3.3.1',
        '@smart-sentinel-eye/shared': 'workspace:*',
        ...singletons,
      },
      devDependencies: {
        '@module-federation/vite': '1.23.3',
        ...devDependencies,
      },
    },
  };
}

function describeProblems(problems) {
  return `problems: ${JSON.stringify(problems)}`;
}

// ==== no federation-role package exists yet (today's repository, before T007)

test('no package declares the @module-federation/vite marker — passes vacuously, nothing to compare', () => {
  const problems = checkSingletonVersions({
    packages: [plainPackage('apps/management-web/package.json'), plainPackage('apps/shared/package.json')],
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

// ==== matching pins across every federation-role package pass ===============

test('a shell and a remote pinning every singleton identically — passes', () => {
  const problems = checkSingletonVersions({
    packages: [
      federationPackage('apps/management-web/package.json'),
      federationPackage('apps/management-cameras/package.json'),
    ],
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

// A plain package's own differing pin is irrelevant: only federation-role
// packages are compared with each other.
test('a non-federation package pinning react-redux differently from the federation packages is not compared', () => {
  const problems = checkSingletonVersions({
    packages: [
      federationPackage('apps/management-web/package.json'),
      federationPackage('apps/management-cameras/package.json'),
      plainPackage('apps/kiosk-web/package.json', { dependencies: { 'react-redux': '9.0.0' } }),
    ],
  });

  assert.deepEqual(problems, [], describeProblems(problems));
});

// ==== one bumped pin fails, naming the module and both packages (plan.md §6
//      test 9's own example) =================================================

test('the remote pins a newer react-redux than the shell — fails, naming react-redux and both package paths', () => {
  const problems = checkSingletonVersions({
    packages: [
      federationPackage('apps/management-web/package.json'),
      federationPackage('apps/management-cameras/package.json', { singletons: { 'react-redux': '9.4.0' } }),
    ],
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /react-redux/, describeProblems(problems));
  assert.match(problems[0], /9\.3\.0/, describeProblems(problems));
  assert.match(problems[0], /9\.4\.0/, describeProblems(problems));
  assert.match(problems[0], /apps\/management-web\/package\.json/, describeProblems(problems));
  assert.match(problems[0], /apps\/management-cameras\/package\.json/, describeProblems(problems));
});

test('three federation-role packages, one of which pins a different @smart-sentinel-eye/shared range — fails, naming it', () => {
  const problems = checkSingletonVersions({
    packages: [
      federationPackage('apps/management-web/package.json'),
      federationPackage('apps/management-cameras/package.json'),
      federationPackage('apps/management-rules/package.json', {
        singletons: { '@smart-sentinel-eye/shared': '0.0.1' },
      }),
    ],
  });

  assert.equal(problems.length, 1, describeProblems(problems));
  assert.match(problems[0], /@smart-sentinel-eye\/shared/, describeProblems(problems));
  assert.match(problems[0], /workspace:\*/, describeProblems(problems));
  assert.match(problems[0], /0\.0\.1/, describeProblems(problems));
});

// ==== the real repository (mirrors node-types-alignment.test.mjs's own real-
//      repository case) — the one case that asks the running system =========

test('readFederationPackages(repositoryRoot) against the real apps/*/package.json files — passes with no problems', () => {
  const problems = checkSingletonVersions({ packages: readFederationPackages(repositoryRoot) });

  assert.deepEqual(problems, [], describeProblems(problems));
});
