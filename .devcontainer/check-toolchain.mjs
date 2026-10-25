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
const nodeRange = packageJson.engines.node;
expect('node', `satisfies engines.node "${nodeRange}"`, nodeActual, semver.satisfies(nodeActual, nodeRange));

const [, pnpmExpected] = packageJson.packageManager.split('@');
const pnpmActual = versionOf('pnpm');
expect('pnpm', `${pnpmExpected} (packageManager)`, pnpmActual, pnpmActual === pnpmExpected);

// global.json rollForward is latestPatch: same major.minor and feature band, patch >= pinned.
const dotnetPinned = globalJson.sdk.version;
const dotnetActual = versionOf('dotnet');
const band = (version) => `${semver.major(version)}.${semver.minor(version)}.${Math.floor(semver.patch(version) / 100)}`;
const dotnetOk =
  semver.valid(dotnetActual) !== null &&
  band(dotnetActual) === band(dotnetPinned) &&
  semver.gte(dotnetActual, dotnetPinned);
expect(
  'dotnet',
  `>= ${dotnetPinned} in the same feature band (global.json, rollForward ${globalJson.sdk.rollForward})`,
  dotnetActual,
  dotnetOk,
);

if (failures.length > 0) {
  console.error('Toolchain does not match the repository pins:');
  failures.forEach((failure) => console.error(`  - ${failure}`));
  process.exit(1);
}

console.log(`Toolchain OK: node ${nodeActual}, pnpm ${pnpmActual}, dotnet ${dotnetActual}`);
