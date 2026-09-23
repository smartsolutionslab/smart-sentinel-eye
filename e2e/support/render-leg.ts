import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

/**
 * Spec 225 US1 — the composite-and-render leg's figure, escaping the run.
 *
 * <p>
 * `measureOverlayDraw` (`apps/shared/src/observability/kioskLatency.ts:135-142`)
 * and the span test's own harvest of its `[latency]` console lines
 * (`kiosk-shows-a-label-over-video.spec.ts:1137-1181`) already produce this
 * figure; both stay untouched (FR-007). This file is only the escape hatch —
 * the record's shape, the writer and the reader, in one place so the test and
 * the two scripts that read it back (`render-leg-summary.mjs`,
 * `render-leg-check.mjs`) cannot drift. Same shape as `live-video-wall.ts`'s
 * writer/reader pair, and for the same reason: a hand-off between processes,
 * here between the test worker that measured the leg and a later CI step.
 * </p>
 */

/**
 * One attempt's reading of the composite-and-render leg.
 *
 * <p>
 * <b>`samples` is the raw distribution, not only its percentiles</b> — FR-009
 * needs the raw figures in the tree to re-derive a variance, not trust one.
 * </p>
 *
 * <p>
 * <b>`p50`/`max`/`p95` are `null` at zero samples, never `0`</b> (FR-003): a
 * zero reads as a perfect score for a journey nobody timed, the same rule
 * `reportLegs` already follows for this leg.
 * </p>
 */
export interface RenderLegRecord {
  /** The one §IV leg this record carries. A closed set of one, stated so a
   * reader never has to guess what `samples` measures. */
  measurement: 'overlay_draw';
  /** `test.info().retry` — 0, 1, 2. One file per attempt, so a retry never
   * overwrites the attempt before it (FR-004). */
  attempt: number;
  /** `GITHUB_RUN_ID`, or `null` off CI. */
  runId: string | null;
  /** `GITHUB_SHA` (40 chars), or `null` off CI. */
  sha: string | null;
  /** Every `overlay_draw` sample this attempt harvested, in capture order. */
  samples: ReadonlyArray<number>;
  /** `samples.length` — carried explicitly so a reader need not recompute it
   * to tell "no samples" (FR-003) from "one sample" (`percentiles()`'s own
   * refusal to print a distribution below n=2 stays a summariser concern). */
  count: number;
  p50: number | null;
  max: number | null;
  /** `null` below the sample count that supports one — the same index
   * arithmetic and the same rule `percentiles()` already follows at
   * `kiosk-shows-a-label-over-video.spec.ts:756-766`. */
  p95: number | null;
  /**
   * The in-page `requestAnimationFrame` cadence probe, taken **before** the
   * timed loop begins.
   *
   * <p>
   * Milliseconds per frame, not a frequency — the same unit the figure it
   * explains is in, so the two can be compared without a conversion.
   * </p>
   */
  frameIntervalBeforeMilliseconds: number;
  /** And again **after** the loop ends — two readings, not one, because a
   * single pre-run reading would not notice the runner losing cadence
   * mid-test (plan.md §2.3). Never taken during the timed window (NFR-002). */
  frameIntervalAfterMilliseconds: number;
  /**
   * Whether this attempt is usable as a figure at all (spec FR-016, plan §8.2).
   *
   * <p>
   * Set from {@link isCompleteRenderLegMeasurement} against the same
   * per-camera sample counts the span test's own per-camera `expect`s already
   * check — one predicate, so the test and `render-leg-check.mjs` (T020)
   * cannot disagree about what "complete" means. A record with no `complete`
   * field at all is malformed, not `false` — the checker reads that case as
   * *unmeasured*, never as a pass.
   * </p>
   */
  complete: boolean;
}

/**
 * True exactly when the wall's samples carry `expectedCameras` distinct
 * cameras and every one of them contributed at least `iterations` samples
 * (spec FR-016, plan §8.2).
 *
 * <p>
 * Restates, as one reusable boolean, the identical rule the span test already
 * applies as two separate `expect`s
 * (`kiosk-shows-a-label-over-video.spec.ts:1474-1485`): "exactly the expected
 * camera count" (not "at least" — a fifth camera is as wrong as a missing
 * one) and "at least `iterations` samples per camera" (a tile that drew once
 * at mount and then froze must not read as complete).
 * </p>
 */
export function isCompleteRenderLegMeasurement(
  samplesPerCamera: ReadonlyMap<string, number>,
  expectedCameras: number,
  iterations: number,
): boolean {
  if (samplesPerCamera.size !== expectedCameras) {
    return false;
  }
  for (const count of samplesPerCamera.values()) {
    if (count < iterations) {
      return false;
    }
  }
  return true;
}

const RENDER_LEG_DIRECTORY = resolve(process.cwd(), 'test-results');
const RENDER_LEG_FILE_PATTERN = /^render-leg-attempt-(\d+)\.json$/;

function renderLegPath(attempt: number, directory: string): string {
  return join(directory, `render-leg-attempt-${attempt}.json`);
}

/** Writes one attempt's record. Called once per test attempt, never appended to. */
export function writeRenderLegRecord(record: RenderLegRecord, directory: string = RENDER_LEG_DIRECTORY): void {
  mkdirSync(directory, { recursive: true });
  writeFileSync(renderLegPath(record.attempt, directory), JSON.stringify(record, null, 2), 'utf8');
}

/**
 * One attempt file, read and either parsed or found unreadable.
 *
 * <p>
 * <b>A malformed file is a result, never a thrown exception.</b> Both reading
 * scripts (`render-leg-summary.mjs`, `render-leg-check.mjs`) must be able to
 * name a bad file rather than crash over it — the summariser because a script
 * that dies is a step that must never redden a build (NFR-001), the checker
 * because a record it cannot read is exactly the *unmeasured* case FR-012
 * requires it to fail on, worded distinctly from a regression.
 * </p>
 */
export type RenderLegAttempt =
  { file: string; ok: true; record: RenderLegRecord } | { file: string; ok: false; error: string };

export function readRenderLegRecords(directory: string = RENDER_LEG_DIRECTORY): RenderLegAttempt[] {
  let entries: string[];
  try {
    entries = readdirSync(directory);
  } catch {
    return [];
  }

  return entries
    .filter((name) => RENDER_LEG_FILE_PATTERN.test(name))
    .sort()
    .map((name) => {
      try {
        const record = JSON.parse(readFileSync(join(directory, name), 'utf8')) as RenderLegRecord;
        return { file: name, ok: true, record };
      } catch (error) {
        return { file: name, ok: false, error: error instanceof Error ? error.message : String(error) };
      }
    });
}

/**
 * The provenance pair every record carries, read once from CI's own
 * environment.
 *
 * <p>
 * Phase-6 review (spec 225): on a `pull_request` run, `GITHUB_SHA` is the
 * synthetic merge commit GitHub creates for the check, not the branch's own
 * head — it resolves today but lands on no branch, so `git show` after a
 * plain fetch misses it. The real head SHA is on the event payload
 * (`github.event.pull_request.head.sha`), not a plain env var, so it is read
 * from `GITHUB_EVENT_PATH`'s JSON when the event is `pull_request`, falling
 * back to `GITHUB_SHA` for every other trigger (push, workflow_dispatch) and
 * for any failure to read or parse the event file — this function must never
 * throw, since a provenance nit must not fail the span test that calls it.
 * </p>
 */
export function currentRunProvenance(): { runId: string | null; sha: string | null } {
  const runId = process.env['GITHUB_RUN_ID'] ?? null;

  if (process.env['GITHUB_EVENT_NAME'] === 'pull_request') {
    const eventPath = process.env['GITHUB_EVENT_PATH'];
    if (eventPath) {
      try {
        const event = JSON.parse(readFileSync(eventPath, 'utf8')) as {
          pull_request?: { head?: { sha?: string } };
        };
        const headSha = event.pull_request?.head?.sha;
        if (typeof headSha === 'string' && headSha.length > 0) {
          return { runId, sha: headSha };
        }
      } catch {
        // Fall through to GITHUB_SHA below — better a synthetic merge SHA
        // than no SHA at all.
      }
    }
  }

  return { runId, sha: process.env['GITHUB_SHA'] ?? null };
}
