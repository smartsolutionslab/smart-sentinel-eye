import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render } from '@testing-library/react';
import type { PlayoutTargetOutcome } from '@smart-sentinel-eye/shared/streaming/WhepClient';

/**
 * Spec 045 T019 / T025. **management-web has no wall, so it must never align.**
 *
 * <p>
 * It mounts the same shared `CameraViewer` a kiosk tile does, and shows one
 * camera at a time with nothing to align it against. The guarantee is that it
 * <b>passes no alignment props and therefore runs nothing</b> — no sampling
 * interval, and above all no `jitterBufferTarget` ever written to a receiver
 * that has no wall to be in step with.
 * </p>
 *
 * <p>
 * The server's `IsBrowserKiosk()` gate drops desktop measurements (#1893), but
 * that is the <b>backstop, not the design</b>. If these tests need it to pass,
 * the client is wrong.
 * </p>
 *
 * <p>
 * <b>Asserted as an absence of any write</b>, never as unchanged latency: a
 * controller that set a lone tile to its own measured lag would change nothing
 * observable and would still be wrong (FR-004).
 * </p>
 */

const setPlayoutTarget = vi.fn((): PlayoutTargetOutcome => 'applied');
const stats = vi.fn(async () => new Map());

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // **Must report `connected`.** Every sampler in CameraViewer guards on
    // `status !== 'live'`, so a double that never fires this callback makes the
    // whole suite vacuous — `stats` is never called, `reads` is 0, and the
    // read-count assertion below passes against a component that did nothing.
    // That is exactly how this file was first written.
    constructor(private readonly options: { onConnectionStateChange?: (state: string) => void }) {}

    connect = vi.fn(async () => {
      this.options.onConnectionStateChange?.('connected');
    });
    close = vi.fn();
    stats = stats;
    setPlayoutTarget = setPlayoutTarget;
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

// CameraViewer no longer calls useGetStreamQuery itself (ADR-0169, #2846) — it
// takes the stream as a prop, so this is passed directly instead of mocked.
const STREAM = {
  cameraIdentifier: 'cam-42',
  state: 'Healthy' as const,
  whepUrl: 'http://sfu/whep/cam-42',
  transcodeMode: 'Passthrough' as const,
  lastSuccessAt: null,
  error: null,
};

describe('CameraViewer on a page with no wall', () => {
  beforeEach(() => {
    setPlayoutTarget.mockClear();
    stats.mockClear();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Never writes a playout target when no wall asked for one', () => {
    render(<CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} stream={STREAM} />);

    act(() => {
      vi.advanceTimersByTime(30_000);
    });

    expect(setPlayoutTarget).not.toHaveBeenCalled();
  });

  it('Starts no lag sampling when nobody is listening for it', () => {
    render(<CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} stream={STREAM} />);

    const before = stats.mock.calls.length;
    act(() => {
      vi.advanceTimersByTime(30_000);
    });

    // The decode sampler (spec 040) still runs — it is not this feature's, and
    // silencing it would be a regression. What must not appear is the second,
    // faster alignment sampler on top of it: at a 2 s cadence over 30 s that
    // would be an order of magnitude more reads than the 5 s decode interval.
    const reads = stats.mock.calls.length - before;
    // Asserted from BOTH sides. The upper bound alone is satisfied by a tile
    // that never sampled at all, which is how this test used to pass while the
    // session sat in `connecting` forever.
    expect(reads, 'the decode sampler must still be running').toBeGreaterThan(0);
    expect(reads, 'the alignment sampler must not be running here').toBeLessThanOrEqual(6);
  });
});
