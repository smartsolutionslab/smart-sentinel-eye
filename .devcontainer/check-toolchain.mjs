// Asserts the container's toolchain against the repo's own pins, so drift fails
// post-create in seconds rather than as a confusing build error minutes later.
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';

const run = (command, args) =>
  execFileSync(command, args, { encoding: 'utf8', shell: process.platform === 'win32' }).trim();

// A tool that cannot report its version is a mismatch to name, not a stack trace to read.
const versionOf = (command) => {
  try {
    return run(command, ['--version']).split('\n').pop().trim();
  } catch {
    return `unavailable (${command} --version failed)`;
  }
};

// npm bundles semver; reusing it avoids adding a dependency for one range check.
// The npm that ships beside the running node is the only one guaranteed to carry it.
const nodeDirectory = dirname(process.execPath);
const npmDirectories = [
  join(nodeDirectory, 'node_modules', 'npm'),
  join(nodeDirectory, '..', 'lib', 'node_modules', 'npm'),
];
const npmDirectory = npmDirectories.find((directory) => existsSync(join(directory, 'node_modules', 'semver')));
if (npmDirectory === undefined) {
  console.error(`Cannot find the semver package bundled with npm next to ${process.execPath}`);
  process.exit(1);
}
const semver = createRequire(join(npmDirectory, 'package.json'))('semver');

const packageJson = JSON.parse(readFileSync('package.json', 'utf8'));
const globalJson = JSON.parse(readFileSync('global.json', 'utf8'));

const failures = [];
const expect = (tool, expected, actual, satisfied) => {
  if (!satisfied) {
    failures.push(`${tool}: expected ${expected}, actual ${actual}`);
  }
};

const nodeActual = process.versions.node;
const results = [];

// A malformed pin is a failure line naming what was expected, never a TypeError that hides the other tools.
const nodeRange = packageJson.engines?.node;
if (typeof nodeRange !== 'string' || semver.validRange(nodeRange) === null) {
  expect('node', 'a valid engines.node range in package.json', String(nodeRange), false);
} else {
  expect('node', `satisfies engines.node "${nodeRange}"`, nodeActual, semver.satisfies(nodeActual, nodeRange));
  results.push(`node ${nodeActual}`);
}

// corepack may append a hash (pnpm@10.0.0+sha512.abc); the version is what precedes it.
const packageManager = typeof packageJson.packageManager === 'string' ? packageJson.packageManager : '';
const pnpmExpected = packageManager.split('@')[1]?.split('+')[0];
const pnpmActual = versionOf('pnpm');
if (!pnpmExpected) {
  expect('pnpm', 'a packageManager of the form pnpm@<version> in package.json', packageManager || 'missing', false);
} else {
  expect('pnpm', `${pnpmExpected} (packageManager)`, pnpmActual, pnpmActual === pnpmExpected);
  results.push(`pnpm ${pnpmActual}`);
}

// The comparison below encodes latestPatch: same major.minor and feature band, patch >= pinned.
// Any other policy needs the check rewritten, not silently misapplied.
const rollForward = globalJson.sdk?.rollForward;
if (rollForward !== 'latestPatch') {
  failures.push(`dotnet: check needs updating for rollForward ${rollForward}`);
}

const dotnetPinned = globalJson.sdk?.version;
if (semver.valid(dotnetPinned) === null) {
  expect('dotnet', 'a valid sdk.version in global.json', String(dotnetPinned), false);
} else {
  // The active SDK (`dotnet --version`) is not the test: any installed SDK satisfying the pin resolves it,
  // and the full list is the evidence when none does.
  const band = (version) =>
    `${semver.major(version)}.${semver.minor(version)}.${Math.floor(semver.patch(version) / 100)}`;
  let installed = [];
  let listing = 'unavailable (dotnet --list-sdks failed)';
  try {
    const lines = run('dotnet', ['--list-sdks'])
      .split('\n')
      .map((line) => line.trim())
      .filter(Boolean);
    installed = lines.map((line) => line.split(' ')[0]);
    listing = lines.length > 0 ? lines.join('; ') : 'no SDKs installed';
  } catch {
    // listing keeps its "unavailable" text, which is the mismatch to name.
  }
  const match = installed.find(
    (version) =>
      semver.valid(version) !== null && band(version) === band(dotnetPinned) && semver.gte(version, dotnetPinned),
  );
  expect(
    'dotnet',
    `an installed SDK >= ${dotnetPinned} in the same feature band (global.json, rollForward ${rollForward})`,
    listing,
    match !== undefined,
  );
  if (match !== undefined) {
    results.push(`dotnet ${match}`);
  }
}

if (failures.length > 0) {
  console.error('Toolchain does not match the repository pins:');
  failures.forEach((failure) => console.error(`  - ${failure}`));
  process.exit(1);
}

console.log(`Toolchain OK: ${results.join(', ')}`);
