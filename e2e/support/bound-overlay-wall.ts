import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

/**
 * Issue 1921 — the names the SC-004 seed publishes, handed to the
 * reconciliation spec that has to find them again.
 *
 * A plain module rather than exports on the `.setup.ts`, because Playwright
 * refuses to let one test file import another and a setup file is a test file.
 *
 * The names are written to disk rather than computed in both places. They carry
 * a timestamp — layout, overlay and variable names are unique per fab, so fixed
 * ones would collide on a second run against a surviving database — and the
 * setup and the spec run in **different worker processes**, so a `Date.now()`
 * evaluated at import time yields a different answer in each. The spec then
 * hunts for a wall that was never published, which is exactly how this was
 * found.
 *
 * `test-results/` is gitignored and Playwright empties it at the start of every
 * run, so the handoff cannot outlive the run that wrote it.
 */

export interface BoundOverlayWall {
  variableName: string;
  variableInitialValue: string;
  variableChangedValue: string;
  overlayName: string;
  layoutName: string;
  cameraName: string;
}

const handoffPath = resolve(process.cwd(), 'test-results', 'sc004-bound-overlay-wall.json');

/** Fresh names for one run. Called by the seed, once. */
export function newBoundOverlayWall(): BoundOverlayWall {
  const stamp = Date.now();
  return {
    // Lowercase — the variable grammar rejects anything else (spec 005).
    variableName: `sc004value${stamp}`,
    variableInitialValue: 'BEFORE',
    variableChangedValue: 'AFTER',
    overlayName: `SC004 Overlay ${stamp}`,
    layoutName: `SC004 Wall ${stamp}`,
    // The `E2E ` prefix is what the cleanup teardown matches on (issue 1895).
    cameraName: `E2E SC004 Cam ${stamp}`,
  };
}

export function writeBoundOverlayWall(wall: BoundOverlayWall): void {
  mkdirSync(dirname(handoffPath), { recursive: true });
  writeFileSync(handoffPath, JSON.stringify(wall, null, 2), 'utf8');
}

/**
 * What the seed published. Throws rather than returning undefined: a spec that
 * silently skipped because the seed had not run is the failure mode this whole
 * fixture exists to avoid.
 */
export function readBoundOverlayWall(): BoundOverlayWall {
  return JSON.parse(readFileSync(handoffPath, 'utf8')) as BoundOverlayWall;
}
