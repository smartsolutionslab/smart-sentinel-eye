// scripts/node-types-alignment.mjs
//
// Spec 285 §5 / plan.md §4 — the @types/node <-> CI Node major alignment
// guard (closes #2645). §1's measurement found the repository already
// aligned (CI Node 22 on all three setup-node steps, engines.node floor 22,
// @types/node ^22.20.4) and NOT due for a bump to 26 — this module is what
// keeps that alignment from drifting apart unnoticed a second time (the rule
// was already written down twice, in specs 088 and 272, and still produced
// this issue).
//
// `checkNodeTypesAlignment` is a pure function over fixture-shaped data (no
// file I/O). `readRepositoryInputs` is the one exception: it reads the real
// `.github/workflows/*.yml`/`*.yaml` files and the root `package.json`, and
// is the only I/O in the guard (plan.md §4.2's closing paragraph).
//
// Picked up by the root `test:guards` script
// (`node --test "scripts/**/*.test.mjs" ...`) via
// scripts/node-types-alignment.test.mjs — no CI or package.json edit needed.

import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';

const WORKFLOW_FILE_PATTERN = /\.ya?ml$/;
const SETUP_NODE_USES_PATTERN = /uses:\s*actions\/setup-node@[^\s]*/g;
const STEP_BOUNDARY_PATTERN = /^([ \t]*)-[ \t]/gm;
const NODE_VERSION_LINE_PATTERN = /^[ \t]*node-version:[ \t]*(.+?)[ \t]*$/m;
const LEADING_INTEGER_PATTERN = /^(\d+)/;
const TYPES_RANGE_PATTERN = /^[\^~]?(\d+)/;
const ENGINES_ALTERNATIVE_PATTERN = /^(?:\^|~|>=|=)?(\d+)/;

// ---- workflow parsing (plan.md §4.2's "Locating a step's node-version") ---

function stripQuotes(value) {
  if (value.length >= 2) {
    const first = value[0];
    const last = value[value.length - 1];
    if ((first === "'" && last === "'") || (first === '"' && last === '"')) {
      return value.slice(1, -1);
    }
  }
  return value;
}

function majorFromLeadingInteger(text) {
  const match = LEADING_INTEGER_PATTERN.exec(text);
  return match ? Number(match[1]) : null;
}

function findStepBoundaries(text) {
  const boundaries = [];
  STEP_BOUNDARY_PATTERN.lastIndex = 0;
  let match;
  while ((match = STEP_BOUNDARY_PATTERN.exec(text)) !== null) {
    boundaries.push({ index: match.index, indent: match[1].length });
  }
  return boundaries;
}

// The slice from a setup-node step's own dash line up to the next sibling
// step (a boundary at an indent <= this step's own), or EOF — so a following,
// unrelated step's `node-version`-shaped line is never read into this step.
function sliceForStep(text, matchIndex, boundaries) {
  let own = null;
  for (const boundary of boundaries) {
    if (boundary.index <= matchIndex && (own === null || boundary.index > own.index)) {
      own = boundary;
    }
  }
  if (own === null) {
    own = { index: matchIndex, indent: 0 };
  }

  let end = text.length;
  for (const boundary of boundaries) {
    if (boundary.index > own.index && boundary.indent <= own.indent) {
      end = boundary.index;
      break;
    }
  }
  return text.slice(own.index, end);
}

function extractNodeVersion(stepSlice) {
  const match = NODE_VERSION_LINE_PATTERN.exec(stepSlice);
  if (!match) {
    return { present: false, raw: null };
  }
  return { present: true, raw: stripQuotes(match[1].trim()) };
}

// Every setup-node step across every workflow, each reduced to its declared
// major (or `major: null` when unreadable) — regardless of key order
// (`name:`/`uses:`/`with:` may appear in any order, per plan.md §4.2).
function collectCiSteps(workflows) {
  const steps = [];
  for (const workflow of workflows) {
    const boundaries = findStepBoundaries(workflow.text);
    SETUP_NODE_USES_PATTERN.lastIndex = 0;
    let match;
    while ((match = SETUP_NODE_USES_PATTERN.exec(workflow.text)) !== null) {
      const stepSlice = sliceForStep(workflow.text, match.index, boundaries);
      const { present, raw } = extractNodeVersion(stepSlice);
      const major = present ? majorFromLeadingInteger(raw) : null;
      steps.push({ path: workflow.path, present, raw, major });
    }
  }
  return steps;
}

// ---- package.json readers (plan.md §4.1) -----------------------------------

// Readable only as `^N…`, `~N…`, or an exact `N…` — never `>=` or `*`, since
// either could resolve to any future major.
function readTypesMajor(manifest) {
  const raw = manifest?.devDependencies?.['@types/node'];
  if (typeof raw !== 'string') {
    return { unreadable: true, major: null };
  }
  const match = TYPES_RANGE_PATTERN.exec(raw.trim());
  if (!match) {
    return { unreadable: true, major: null };
  }
  return { unreadable: false, major: Number(match[1]) };
}

// Splits on `||`; each alternative must start with `^`, `~`, `>=`, `=`, or be
// a bare version to count as parseable. The floor is the minimum major across
// alternatives (order-independent).
function readEnginesFloor(manifest) {
  const raw = manifest?.engines?.node;
  if (typeof raw !== 'string' || raw.trim() === '') {
    return { unreadable: true, floor: null };
  }
  const alternatives = raw
    .split('||')
    .map((alternative) => alternative.trim())
    .filter((alternative) => alternative.length > 0);
  if (alternatives.length === 0) {
    return { unreadable: true, floor: null };
  }

  const majors = [];
  for (const alternative of alternatives) {
    const match = ENGINES_ALTERNATIVE_PATTERN.exec(alternative);
    if (!match) {
      return { unreadable: true, floor: null };
    }
    majors.push(Number(match[1]));
  }
  return { unreadable: false, floor: Math.min(...majors) };
}

// ---- the guard --------------------------------------------------------------

export function checkNodeTypesAlignment({ workflows, manifest, runningVersion }) {
  const problems = [];
  const ciSteps = collectCiSteps(workflows ?? []);

  if (ciSteps.length === 0) {
    problems.push(
      'No setup-node step found in any workflow file — cannot determine the CI Node major (a renamed action or a ' +
        'moved step must fail this check, not pass with nothing compared).',
    );
  }

  for (const step of ciSteps) {
    if (step.major === null) {
      const rawText = step.present ? ` "${step.raw}"` : ' (missing)';
      problems.push(
        `Unreadable node-version${rawText} for a setup-node step in ${step.path} — cannot determine the CI Node major.`,
      );
    }
  }

  const readableSteps = ciSteps.filter((step) => step.major !== null);
  let ciMajor = null;
  if (readableSteps.length > 0) {
    const distinctMajors = [...new Set(readableSteps.map((step) => step.major))].sort((a, b) => a - b);
    if (distinctMajors.length > 1) {
      const paths = [...new Set(readableSteps.map((step) => step.path))].sort();
      problems.push(
        `CI setup-node steps disagree on the Node major (${distinctMajors.join(', ')}) across ${paths.join(', ')} ` +
          '— align every setup-node step on one Node major.',
      );
    } else {
      ciMajor = distinctMajors[0];
    }
  }

  const types = readTypesMajor(manifest);
  if (types.unreadable) {
    problems.push(
      "@types/node's declared range in package.json is unreadable — expected \"^N…\", \"~N…\" or an exact " +
        '"N…" version, never ">=", "*" or missing.',
    );
  }

  const engines = readEnginesFloor(manifest);
  if (engines.unreadable) {
    problems.push(
      "engines.node's declared range in package.json is unreadable — every \"||\" alternative must start with " +
        '"^", "~", ">=", "=" or be a bare version.',
    );
  }

  if (ciMajor !== null) {
    if (!types.unreadable && types.major !== ciMajor) {
      problems.push(
        `@types/node declares major ${types.major} but CI's setup-node steps declare major ${ciMajor} — update ` +
          "package.json's @types/node range to match.",
      );
    }
    if (!engines.unreadable && engines.floor !== ciMajor) {
      problems.push(
        `engines.node's lowest supported major is ${engines.floor} but CI's setup-node steps declare major ` +
          `${ciMajor} — update package.json's engines.node range to match.`,
      );
    }
  }

  // Only compared once the types major is itself trustworthy (readable and
  // aligned with CI) — otherwise the mismatch above is the one root cause,
  // and comparing the running runtime against an already-wrong types major
  // would be a second message for the same problem rather than a new one.
  if (!types.unreadable && ciMajor !== null && types.major === ciMajor) {
    const runningMajor = majorFromLeadingInteger(String(runningVersion ?? ''));
    if (runningMajor !== null && runningMajor < types.major) {
      problems.push(
        `The running Node runtime (${runningVersion}) is older than the declared @types/node major ` +
          `${types.major} — the typings are a floor, so upgrade the runtime or lower @types/node's major.`,
      );
    }
  }

  return problems;
}

export function readRepositoryInputs(repositoryRoot) {
  const workflowsDirectory = path.join(repositoryRoot, '.github', 'workflows');
  const workflowFileNames = readdirSync(workflowsDirectory).filter((name) => WORKFLOW_FILE_PATTERN.test(name));

  const workflows = workflowFileNames
    .sort()
    .map((name) => ({
      // Forward-slash, repository-relative — not `path.join`, whose separator
      // is platform-dependent and would break every path-shaped assertion on
      // Windows (memory: source-scanning tests need slash normalising).
      path: `.github/workflows/${name}`,
      text: readFileSync(path.join(workflowsDirectory, name), 'utf8'),
    }));

  const manifest = JSON.parse(readFileSync(path.join(repositoryRoot, 'package.json'), 'utf8'));

  return { workflows, manifest };
}
