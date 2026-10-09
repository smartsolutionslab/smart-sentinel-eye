// Guard for #2374 / spec 321 / ADR-0169.
//
// ADR-0169 decides that a component under `apps/shared/src/ui/composites/**`
// renders without a Redux `<Provider>` in the tree, and names its
// enforcement: "an architecture test asserting that no file under
// `ui/composites/**` imports `react-redux` or `@reduxjs/toolkit`." That rule
// is held today only by one assertion inside `OverlayEditorKeyboard.test.tsx`
// (an unrelated keyboard-handling suite) surviving. This guard lints
// fixtures through the REAL, shipped `apps/shared/eslint.config.js` (`new
// ESLint({ cwd })`, unmodified) rather than a hand-rolled copy of the rule —
// the one property that makes it worth anything (memory: guards that read
// the design artefact). `ESLint#lintText` is given a virtual `filePath`
// matching the rule's `files` glob so flat-config resolution applies the
// same block `eslint src` would; the path need not exist on disk, so no
// fixture file is written under `apps/shared/src` and nothing is left to
// clean up (plan.md §4: inline fixture strings).
//
// FIRST-RUN NOTE, read before treating any of this as a status report
// (spec.md §7, plan.md §4):
//   * The must-flag tests are expected RED right now, reporting 0 problems
//     where each asserts exactly 1 — `apps/shared/eslint.config.js` has no
//     ADR-0169 block yet.
//   * The wiring test is expected RED right now, finding
//     `no-restricted-imports` undefined rather than `error`.
//   * The must-not-flag and no-escape-hatch tests are expected GREEN on this
//     very first run, and that proves NOTHING on its own: a rule that does
//     not exist flags nothing, so these would read "zero problems" whether
//     the rule existed or not. They only become evidence once the must-flag
//     tests have been observed red-then-green with the same rule in place.

import assert from 'node:assert/strict';
import { readdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';
import { ESLint } from 'eslint';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sharedApp = path.join(repositoryRoot, 'apps', 'shared');

const severityOf = (entry) => (Array.isArray(entry) ? entry[0] : entry);

// Lints `code` through the real `apps/shared` ESLint config, at `filePath`
// (relative to `sharedApp`). The path need not exist on disk: flat config's
// `files`/`ignores` matchers and the parser both operate on the string.
async function lintShared(code, relativeFilePath) {
  const eslint = new ESLint({ cwd: sharedApp });
  const [result] = await eslint.lintText(code, {
    filePath: path.join(sharedApp, relativeFilePath),
  });

  // A parse failure (`ruleId: null`, `fatal: true`) or ESLint skipping the
  // file (a "File ignored…" warning, also `ruleId: null`) would otherwise be
  // filtered out below along with everything else that isn't
  // no-restricted-imports, making a broken parse or an unmatched `files`
  // glob look identical to a genuinely clean lint. Catch both before
  // narrowing to the rule under test.
  assert.equal(
    result.fatalErrorCount,
    0,
    `expected no fatal errors linting ${relativeFilePath}, got: ${JSON.stringify(result.messages, null, 2)}`,
  );

  const ruleIdLessMessages = result.messages.filter((message) => message.ruleId === null);
  assert.deepEqual(
    ruleIdLessMessages,
    [],
    `expected no ruleId-less messages (parse failure or file ignored) linting ${relativeFilePath}, got: ` +
      `${JSON.stringify(ruleIdLessMessages, null, 2)}`,
  );

  return result.messages.filter((message) => message.ruleId === 'no-restricted-imports');
}

const mustFlagCases = [
  ['a named import from react-redux', "import { useSelector } from 'react-redux';", 'src/ui/composites/__probe__.tsx'],
  [
    'a named import from @reduxjs/toolkit',
    "import { configureStore } from '@reduxjs/toolkit';",
    'src/ui/composites/__probe__.tsx',
  ],
  [
    'a named import from a @reduxjs/toolkit subpath',
    "import { createApi } from '@reduxjs/toolkit/query/react';",
    'src/ui/composites/__probe__.tsx',
  ],
  [
    'a named import from a react-redux subpath',
    "import { useSelector } from 'react-redux/es/exports';",
    'src/ui/composites/__probe__.tsx',
  ],
  ['re-exporting from react-redux', "export { Provider } from 'react-redux';", 'src/ui/composites/__probe__.tsx'],
  [
    'a type-only import from @reduxjs/toolkit',
    "import type { Store } from '@reduxjs/toolkit';",
    'src/ui/composites/__probe__.tsx',
  ],
  [
    'a nested composite directory (the ** reaches subdirectories)',
    "import { useSelector } from 'react-redux';",
    'src/ui/composites/nested/__probe__.ts',
  ],
];

for (const [description, code, relativeFilePath] of mustFlagCases) {
  test(`a composite file is flagged for ${description}`, async () => {
    const problems = await lintShared(code, relativeFilePath);

    assert.equal(
      problems.length,
      1,
      `expected exactly 1 no-restricted-imports problem for ${relativeFilePath} ` +
        `(${description}), got ${problems.length}: ${JSON.stringify(problems, null, 2)}`,
    );

    assert.match(
      problems[0].message,
      /ADR-0169/,
      `expected the message to name ADR-0169 for ${relativeFilePath} (${description}), ` +
        `got: ${problems[0].message}`,
    );
  });
}

const mustNotFlagCases = [
  [
    'react-redux imported from apps/shared/src/api/** (out of scope, ADR-0169)',
    "import { Provider } from 'react-redux';",
    'src/api/__probe__.ts',
  ],
  [
    '@reduxjs/toolkit imported from apps/shared/src/api/** (out of scope, ADR-0169)',
    "import { configureStore } from '@reduxjs/toolkit';",
    'src/api/__probe__.ts',
  ],
  [
    'react-redux imported from apps/shared/src/ui/primitives/** (out of scope, ADR-0169)',
    "import { Provider } from 'react-redux';",
    'src/ui/primitives/__probe__.tsx',
  ],
  [
    '@reduxjs/toolkit imported from apps/shared/src/ui/primitives/** (out of scope, ADR-0169)',
    "import { configureStore } from '@reduxjs/toolkit';",
    'src/ui/primitives/__probe__.tsx',
  ],
  [
    "a composite's own test file building a store around it (exempt, as CameraViewerCameraSwap.test.tsx does on develop)",
    "import { configureStore } from '@reduxjs/toolkit';\nimport { Provider } from 'react-redux';",
    'src/ui/composites/__probe__.test.tsx',
  ],
  [
    // Discrimination (plan.md §4.2): a rule matching `*` would also pass the
    // must-flag tests above. An innocuous composite import must stay clean.
    'an innocuous composite import (discrimination: a rule matching * would fail here)',
    "import { useState } from 'react';",
    'src/ui/composites/__probe__.tsx',
  ],
];

for (const [description, code, relativeFilePath] of mustNotFlagCases) {
  test(`${description} is not flagged`, async () => {
    const problems = await lintShared(code, relativeFilePath);

    assert.deepEqual(
      problems,
      [],
      `expected zero no-restricted-imports problems for ${relativeFilePath} ` +
        `(${description}), got: ${JSON.stringify(problems, null, 2)}`,
    );
  });
}

test('no-restricted-imports is registered at error for a real composite file', async () => {
  const eslint = new ESLint({ cwd: sharedApp });
  const testFile = path.join(sharedApp, 'src', 'ui', 'composites', 'Badge.tsx');
  const configuration = await eslint.calculateConfigForFile(testFile);
  const entry = configuration.rules?.['no-restricted-imports'];
  const severity = severityOf(entry);

  assert.equal(
    severity,
    2,
    `expected no-restricted-imports at severity error (2) for ` +
      `${path.relative(repositoryRoot, testFile)}, got ${JSON.stringify(severity)}`,
  );
});

test('no eslint-disable for no-restricted-imports exists under composites today', async () => {
  const compositesRoot = path.join(sharedApp, 'src', 'ui', 'composites');
  const entries = await readdir(compositesRoot, { withFileTypes: true, recursive: true });
  const nonTestFiles = entries.filter(
    (entry) => entry.isFile() && /\.(ts|tsx)$/.test(entry.name) && !/\.test\.(ts|tsx)$/.test(entry.name),
  );

  // Asserts the scan itself is non-vacuous (commit 7c9f68ea's lesson): a
  // rename that emptied `nonTestFiles` would otherwise make this pass for
  // the wrong reason.
  assert.ok(nonTestFiles.length > 0, 'expected at least one non-test file under apps/shared/src/ui/composites');

  const { readFile } = await import('node:fs/promises');
  const offenders = [];

  for (const entry of nonTestFiles) {
    const filePath = path.join(entry.parentPath ?? entry.path, entry.name);
    const content = await readFile(filePath, 'utf8');
    if (/eslint-disable[^\n]*no-restricted-imports/.test(content)) {
      offenders.push(path.relative(repositoryRoot, filePath));
    }
  }

  assert.deepEqual(
    offenders,
    [],
    `expected no eslint-disable naming no-restricted-imports under composites, found: ${offenders.join(', ')}`,
  );
});
