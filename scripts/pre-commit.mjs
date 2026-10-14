// scripts/pre-commit.mjs
//
// Spec 355 / #2869 — the Husky pre-commit hook. It rejects a commit whose
// staged files would fail CI's two format gates, and nothing else:
//
//  - `*.cs`          -> `dotnet format --verify-no-changes` (ci.yml `backend`)
//  - format:check    -> `prettier --check` on the *staged* content (ci.yml `frontend`)
//
// Check-only by design: it never writes to the working tree or the index, so a
// partially staged file cannot have unstaged hunks committed behind the
// developer's back. (`dotnet format`'s design-time build does write gitignored
// obj/ files; tracked files and the index are never touched.) The planning
// functions are pure and exported for scripts/pre-commit.test.mjs; the I/O
// shell runs only as the entry point.

import { spawnSync } from 'node:child_process';
import { readdirSync, realpathSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import * as prettier from 'prettier';

// Must stay verbatim in package.json's `format:check`; findScopeDrift and its
// guard test fail when the two disagree.
export const FORMAT_CHECK_SCOPE = Object.freeze([
  '{apps,e2e}/**/*.{ts,tsx,js,jsx,json,css,md,yaml,yml}',
  'playwright.config.ts',
]);

const SCOPE_DIRECTORIES = new Set(['apps', 'e2e']);
const SCOPE_EXTENSIONS = new Set(['.ts', '.tsx', '.js', '.jsx', '.json', '.css', '.md', '.yaml', '.yml']);
const SCOPE_FILES = new Set(['playwright.config.ts']);

// `dotnet format` loads the whole project graph it is pointed at. Measured warm
// (2026-10-14): Shared.Kernel/Domain 11-12 s, Api 18-19 s, the solution 43-51 s.
// Four small projects (~48 s) cost about the solution; a typical four-project
// slice (~60 s) is slower than the solution (~49 s), so beyond three projects one
// solution-wide run is used.
export const SOLUTION_FALLBACK_PROJECT_COUNT = 3;
const SOLUTION = 'SmartSentinelEye.slnx';

// Windows rejects command lines above 32 767 characters.
const COMMAND_LINE_LIMIT = 30000;

const SKIP_NOTE = "Skip once with git commit --no-verify; CI's format gates still apply (ADR-0033).";

export function findScopeDrift(formatCheckCommand) {
  return FORMAT_CHECK_SCOPE.filter((glob) => !formatCheckCommand.includes(glob));
}

function inFormatScope(file) {
  if (SCOPE_FILES.has(file)) return true;
  const segments = file.split('/');
  return segments.length > 1 && SCOPE_DIRECTORIES.has(segments[0]) && SCOPE_EXTENSIONS.has(path.posix.extname(file));
}

export function classify(files) {
  const csharp = [];
  const prettierScope = [];
  for (const file of files) {
    if (file.endsWith('.cs')) csharp.push(file);
    else if (inFormatScope(file)) prettierScope.push(file);
  }
  return { csharp, prettier: prettierScope };
}

export function owningProject(file, listDirectory) {
  let directory = path.posix.dirname(file);
  if (directory === '.') directory = '';
  for (;;) {
    const projects = listDirectory(directory).filter((entry) => entry.endsWith('.csproj'));
    if (projects.length === 1) return directory === '' ? projects[0] : `${directory}/${projects[0]}`;
    if (directory === '') return null;
    const parent = path.posix.dirname(directory);
    directory = parent === '.' ? '' : parent;
  }
}

export function planDotnetFormat(files, lookupProject) {
  const byProject = new Map();
  let orphaned = false;
  for (const file of files) {
    const project = lookupProject(file);
    if (project === null) {
      orphaned = true;
      continue;
    }
    byProject.set(project, [...(byProject.get(project) ?? []), file]);
  }
  if (orphaned || byProject.size > SOLUTION_FALLBACK_PROJECT_COUNT) {
    return [{ project: SOLUTION, files }];
  }
  return [...byProject].map(([project, projectFiles]) => ({ project, files: projectFiles }));
}

export function dotnetArgs({ project, files }) {
  return [
    'format',
    project,
    '--verify-no-changes',
    '--no-restore',
    '--exclude-diagnostics',
    'IDE1006',
    '--include',
    ...files,
  ];
}

export function dotnetInvocations({ project, files }, limit = COMMAND_LINE_LIMIT) {
  const invocations = [];
  let chunk = [];
  for (const file of files) {
    const candidate = [...chunk, file];
    if (chunk.length > 0 && dotnetArgs({ project, files: candidate }).join(' ').length > limit) {
      invocations.push(dotnetArgs({ project, files: chunk }));
      chunk = [file];
    } else {
      chunk = candidate;
    }
  }
  if (chunk.length > 0) invocations.push(dotnetArgs({ project, files: chunk }));
  return invocations;
}

function quoted(file) {
  return /\s/.test(file) ? `"${file}"` : file;
}

// `runner(args)` returns `{ status, stdout, stderr }`; dotnet exits 2 for a
// formatting violation, so any other non-zero (or null: could not start) is a
// tool failure and still blocks rather than letting an unchecked commit through.
// Exit 2 whose output holds a compile error (`error CS####`) is also a tool
// failure: the project is not restored, so no formatting verdict was reached.
const COMPILE_ERROR = /\berror CS\d{4}\b/;

export function checkCsharp(plan, runner) {
  const messages = [];
  for (const entry of plan) {
    let status = 0;
    let output = '';
    for (const args of dotnetInvocations(entry)) {
      const result = runner(args);
      status = result.status;
      output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`;
      if (status !== 0) break;
    }
    if (status === 0) continue;
    if (status === 2 && !COMPILE_ERROR.test(output)) {
      messages.push(
        `C#: ${entry.files.map(quoted).join(', ')} is mis-formatted (dotnet format, project ${entry.project}).\n` +
          `  Fix: dotnet format ${entry.project} --include ${entry.files.map(quoted).join(' ')}`,
      );
    } else {
      messages.push(
        `C#: dotnet format could not check ${entry.files.map(quoted).join(', ')} (project ${entry.project}; ` +
          `exit status ${status}).\n` +
          `  Run dotnet restore ${entry.project} (a project that is not restored cannot be checked), ` +
          'or bypass the check with --no-verify.',
      );
    }
  }
  return { ok: messages.length === 0, messages };
}

async function checkPrettier(files, root, readStaged) {
  const ignorePath = [path.join(root, '.gitignore'), path.join(root, '.prettierignore')];
  const failing = [];
  const problems = [];
  for (const file of files) {
    const absolute = path.join(root, file);
    try {
      const info = await prettier.getFileInfo(absolute, { ignorePath });
      if (info.ignored) continue;
      const content = readStaged(file);
      if (content === null) {
        problems.push(`Prettier: could not read ${quoted(file)} from the index.`);
        continue;
      }
      const options = (await prettier.resolveConfig(absolute, { editorconfig: true })) ?? {};
      if (!(await prettier.check(content, { ...options, filepath: absolute }))) failing.push(file);
    } catch (error) {
      const reason = String(error?.message ?? error).split('\n')[0];
      problems.push(`Prettier: could not parse ${quoted(file)} (${reason}).`);
    }
  }
  return {
    ok: failing.length === 0 && problems.length === 0,
    messages: [
      ...problems,
      ...failing.map(
        (file) => `Prettier: ${quoted(file)} is mis-formatted.\n  Fix: pnpm prettier --write ${quoted(file)}`,
      ),
    ],
  };
}

function git(root, args) {
  return spawnSync('git', args, { cwd: root, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 });
}

function splitNul(output) {
  return output.split('\0').filter(Boolean);
}

function listDirectoryOf(root) {
  return (directory) => {
    try {
      return readdirSync(path.join(root, directory));
    } catch {
      return [];
    }
  };
}

export async function main() {
  if (process.env.HUSKY === '0') return 0;

  const toplevel = spawnSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8' });
  if (toplevel.status !== 0) return 0;
  const root = toplevel.stdout.trim();

  const staged = git(root, ['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z']);
  const { csharp, prettier: prettierFiles } = classify(splitNul(staged.stdout));

  const failures = [];

  const readStaged = (file) => {
    const shown = git(root, ['show', `:${file}`]);
    return shown.status === 0 ? shown.stdout : null;
  };
  failures.push(...(await checkPrettier(prettierFiles, root, readStaged)).messages);

  const plan = planDotnetFormat(csharp, (file) => owningProject(file, listDirectoryOf(root)));
  if (plan.length > 0) {
    const unstaged = new Set(splitNul(git(root, ['diff', '--name-only', '-z']).stdout));
    const partial = plan.flatMap((entry) => entry.files).filter((file) => unstaged.has(file));
    if (partial.length > 0) {
      console.error(
        `Note: ${partial.join(', ')} also has unstaged changes; dotnet format checked the working-tree copy, not the index.`,
      );
    }
    const runner = (args) => {
      const result = spawnSync('dotnet', args, { cwd: root, encoding: 'utf8', maxBuffer: 1024 * 1024 * 1024 });
      process.stdout.write(result.stdout ?? '');
      process.stderr.write(result.stderr ?? '');
      return { status: result.status, stdout: result.stdout ?? '', stderr: result.stderr ?? '' };
    };
    failures.push(...checkCsharp(plan, runner).messages);
  }

  if (failures.length === 0) return 0;
  console.error(`\npre-commit: commit blocked.\n\n${failures.join('\n\n')}\n\n${SKIP_NOTE}`);
  return 1;
}

function isEntryPoint() {
  if (process.argv[1] === undefined) return false;
  try {
    return realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url));
  } catch {
    return false;
  }
}

const isMainModule = isEntryPoint();
if (isMainModule) process.exitCode = await main();
