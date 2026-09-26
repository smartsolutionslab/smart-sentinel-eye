import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

/**
 * Spec 056 — the names for a wall whose video actually arrives, handed from the
 * seed to the specs that assert against it.
 *
 * Same shape as `bound-overlay-wall.ts`, and for the same reason: the setup and
 * the specs run in **different worker processes**, so a `Date.now()` evaluated
 * at import time yields a different answer in each, and a spec would go hunting
 * for a wall that was never published.
 */

/** One tile's camera: a name to register it under, and where its video comes from. */
export interface LiveVideoWallCamera {
  name: string;
  rtspUrl: string;
}

/**
 * Spec 225 US2, raised by spec 258 (ADR-0156, D1 option A) — nine tiles, the
 * domain's real ceiling (`GridDimensions.MaxTiles` / `MaxCells`, now 9 —
 * `GridDimensions.cs:26,29`), not one and not the old four. All nine resolve
 * the **same** overlay and the same bound variable (`overlayName`/
 * `variableName` below stay singular), so a per-tile cost is distinguishable
 * from fixed overhead without a ninth overlay write.
 */
export const LIVE_VIDEO_WALL_TILE_COUNT = 9;

export interface LiveVideoWall {
  variableName: string;
  variableInitialValue: string;
  variableChangedValue: string;
  overlayName: string;
  layoutName: string;
  cameras: ReadonlyArray<LiveVideoWallCamera>;
}

/**
 * Where the fixture's video comes from.
 *
 * <p>
 * <b>One definition, and it must match the AppHost's container name.</b> The
 * AppHost adds a `fixture-video` container serving a single looping path; this
 * is the address the main SFU is asked to pull. The test process never connects
 * to it — it only registers it as the camera's URL, and the SFU resolves it on
 * the container network.
 * </p>
 *
 * <p>
 * Overridable so a stack that names it differently does not need a code change,
 * and defined here rather than in any spec file: a host and port written into a
 * test is a second thing to keep true, and when it rots the wall renders `WHEP
 * returned 404` and looks like a broken product rather than a broken fixture.
 * </p>
 */
export const FIXTURE_VIDEO_RTSP_URL = process.env['E2E_FIXTURE_VIDEO_RTSP_URL'] ?? 'rtsp://fixture-video:8554/loop';

const handoffPath = resolve(process.cwd(), 'test-results', 'spec056-live-video-wall.json');

/** Fresh names for one run. Called by the seed, once. */
export function newLiveVideoWall(): LiveVideoWall {
  const stamp = Date.now();
  // The `E2E ` prefix is what the cleanup teardown matches on. A name without
  // it survives the run, which is how a fixture leaves rows behind. Indexed
  // (1-9) rather than bare, because all nine share one `stamp` and would
  // otherwise collide on name uniqueness.
  const cameras: LiveVideoWallCamera[] = [];
  for (let index = 1; index <= LIVE_VIDEO_WALL_TILE_COUNT; index += 1) {
    cameras.push({ name: `E2E Spec056 Cam ${index} ${stamp}`, rtspUrl: FIXTURE_VIDEO_RTSP_URL });
  }

  return {
    // Lowercase — the variable grammar rejects anything else (spec 005).
    variableName: `spec056value${stamp}`,
    variableInitialValue: 'BEFORE',
    variableChangedValue: 'AFTER',
    overlayName: `Spec056 Overlay ${stamp}`,
    layoutName: `Spec056 Wall ${stamp}`,
    cameras,
  };
}

export function writeLiveVideoWall(wall: LiveVideoWall): void {
  mkdirSync(dirname(handoffPath), { recursive: true });
  writeFileSync(handoffPath, JSON.stringify(wall, null, 2), 'utf8');
}

/**
 * What the seed published. Throws rather than returning undefined: a spec that
 * silently skipped because the seed had not run would report success for a
 * check that never ran, which is the failure this fixture exists to remove.
 */
export function readLiveVideoWall(): LiveVideoWall {
  return JSON.parse(readFileSync(handoffPath, 'utf8')) as LiveVideoWall;
}

/**
 * Whether a picture is <b>moving</b>, given two frame counts and the gap
 * between them.
 *
 * <para>
 * A rule rather than an inline comparison so it can be checked in both
 * directions. A source that emitted one frame and stopped satisfies "frames
 * have been decoded" while showing something an operator cannot tell from a
 * frozen wall — and neither can a screenshot, which is why the delta is the
 * assertion and the count is not.
 * </para>
 */
export function isDecodeOngoing(framesBefore: number, framesAfter: number, minimumFrames: number): boolean {
  return framesAfter - framesBefore >= minimumFrames;
}
