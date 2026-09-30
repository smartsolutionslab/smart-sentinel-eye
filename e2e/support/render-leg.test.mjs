// Spec 225 §9 / plan.md §8.2, T027 — "complete" is defined once, by the test,
// and carried in the record (FR-016).
//
// `isCompleteRenderLegMeasurement` is the single predicate the span test's own
// per-camera `expect`s (`kiosk-shows-a-label-over-video.spec.ts:1474-1485`)
// and the checker (`render-leg-check.mjs`, T020) must share, so the gate and
// the test cannot disagree about what "complete" means. Landed by T027 in
// `e2e/support/render-leg.ts`, then tightened by T029 (FR-021, plan.md
// §9.2's signature):
//
//   isCompleteRenderLegMeasurement(
//     samplesPerCamera: ReadonlyMap<string, number>,
//     expectedCameras: number,
//     iterations: number,
//     loopCompleted: boolean,
//   ): boolean
//
// True exactly when there are `expectedCameras` distinct cameras, every one
// of them has at least `iterations` samples — the identical rule the span
// test already applies as two separate `expect`s, restated here as one
// reusable boolean — and the span loop itself ran to completion
// (`loopCompleted`): a truncated attempt must not read as complete merely
// because the counts it collected before breaking happen to be enough.

import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test, { after } from 'node:test';
import {
  isCompleteRenderLegMeasurement,
  readRenderLegRecords,
  writeRenderLegRecord,
} from './render-leg.ts';

const EXPECTED_CAMERAS = 4;
const ITERATIONS = 10;

function fourTileWall({ perCamera }) {
  // perCamera: array of [cameraId, sampleCount] pairs, one per tile.
  return new Map(perCamera);
}

// ---- the predicate itself ---------------------------------------------

test('every tile at or above the iteration count, exactly the expected camera count — complete', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 10],
      ['camera-4', 10],
    ],
  });

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), true);
});

test('one tile fell short of the iteration count — incomplete', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 9], // one short — drew once at mount, then froze
      ['camera-4', 10],
    ],
  });

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), false);
});

test('a tile never drew at all — fewer distinct cameras than expected — incomplete', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 10],
      // camera-4 contributed nothing — never entered the map.
    ],
  });

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), false);
});

test('more distinct cameras than expected — incomplete ("exactly", not "at least")', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 10],
      ['camera-4', 10],
      ['camera-5', 10], // a fifth wall this fixture should never produce
    ],
  });

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), false);
});

test('boundary — exactly ITERATIONS samples on every tile is enough ("at least")', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', ITERATIONS],
      ['camera-2', ITERATIONS],
      ['camera-3', ITERATIONS],
      ['camera-4', ITERATIONS],
    ],
  });

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), true);
});

test('boundary — one sample under ITERATIONS on one tile is not enough', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', ITERATIONS],
      ['camera-2', ITERATIONS],
      ['camera-3', ITERATIONS],
      ['camera-4', ITERATIONS - 1],
    ],
  });

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), false);
});

test('no samples at all — incomplete, not a crash', () => {
  const samplesPerCamera = new Map();

  assert.equal(isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true), false);
});

// ---- FR-021 (T029): the loop must have finished, not just met the count ----
//
// Spec §10.4 F7's exact shape: the span loop refused mid-run (e.g. "iteration
// 8: the value never painted") and broke out early, yet the per-camera counts
// it collected before breaking still happen to satisfy `ITERATIONS` (runs
// 35979443020, 35986570873: n = 40 across 4 cameras — 10 each — from a loop
// that never reached iteration 10). Today's three-argument predicate cannot
// see the break at all, so it reads this as complete.

test('every tile met its per-camera count, but the loop broke out early — incomplete (FR-021)', () => {
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 10],
      ['camera-4', 10],
    ],
  });
  const loopCompleted = false; // the loop refused at iteration 8 and broke

  assert.equal(
    isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, loopCompleted),
    false,
  );
});

// ---- the written record carries the field ------------------------------
//
// The record's shape is a plain object written with `JSON.stringify`
// (`writeRenderLegRecord`), so this is not a type-level check — it proves the
// field the predicate computes actually survives the write/read round trip
// the checker (T020) and the summariser (T028) both depend on.

function baseRecordFields({ attempt = 0 } = {}) {
  return {
    measurement: 'overlay_draw',
    attempt,
    runId: null,
    sha: null,
    samples: [30, 40, 50],
    count: 3,
    p50: 40,
    max: 50,
    p95: null,
    frameIntervalBeforeMilliseconds: 16.67,
    frameIntervalAfterMilliseconds: 16.4,
  };
}

// Phase-6 review (spec 225): none of these temp directories were ever
// cleaned up. Every `tempDirectory()` call is tracked here and swept once
// after the whole file's tests finish, rather than adding a try/finally to
// each test — keeps the existing test bodies untouched.
const tempDirectories = [];

function tempDirectory() {
  const directory = mkdtempSync(path.join(tmpdir(), 'render-leg-record-'));
  tempDirectories.push(directory);
  return directory;
}

after(() => {
  for (const directory of tempDirectories) {
    rmSync(directory, { recursive: true, force: true });
  }
});

test('a record built from a run where every tile got all its samples has complete: true', () => {
  const directory = tempDirectory();
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 10],
      ['camera-4', 10],
    ],
  });
  const complete = isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true);

  writeRenderLegRecord({ ...baseRecordFields(), complete }, directory);
  const [attempt] = readRenderLegRecords(directory);

  assert.equal(attempt.ok, true);
  assert.equal(attempt.record.complete, true);
});

test('a record built from a run where a tile fell short has complete: false', () => {
  const directory = tempDirectory();
  const samplesPerCamera = fourTileWall({
    perCamera: [
      ['camera-1', 10],
      ['camera-2', 10],
      ['camera-3', 6], // one tile had only 6 of 10 samples — spec §9.2 F1's own wording
      ['camera-4', 10],
    ],
  });
  const complete = isCompleteRenderLegMeasurement(samplesPerCamera, EXPECTED_CAMERAS, ITERATIONS, true);

  writeRenderLegRecord({ ...baseRecordFields(), complete }, directory);
  const [attempt] = readRenderLegRecords(directory);

  assert.equal(attempt.ok, true);
  assert.equal(attempt.record.complete, false);
});
