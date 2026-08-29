import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useWallAlignment } from './useWallAlignment.js';

/**
 * Spec 045 US1. The wall's control loop.
 *
 * <p>
 * <b>Every test here induces a spread before asserting one closed.</b> On an
 * idle box with two identical sources the measured spread was already 9.4 ms
 * before this feature existed (research R6) — well inside the 33 ms bound. So a
 * test that merely asserts "the spread is small" passes with the controller
 * deleted, and would have proved nothing at all. That is the single most likely
 * way this feature ships broken.
 * </p>
 */

const CYCLE_MS = 2_000;

/** Runs the controller for one settle cycle. */
function cycle(times = 1) {
  act(() => {
    vi.advanceTimersByTime(CYCLE_MS * times);
  });
}

describe('useWallAlignment', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /**
   * The core claim: three tiles are induced 100 ms apart, and the wall drives
   * them all to the slowest so the spread closes inside the bound.
   */
  it('Drives an induced spread to the slowest tile', () => {
    const { result } = renderHook(() => useWallAlignment(3));

    act(() => {
      result.current.reportLag('a', 20, 10);
      result.current.reportLag('b', 30, 15);
      result.current.reportLag('c', 120, 60);
    });
    // Induced spread, stated so the assertion below cannot be mistaken for a
    // wall that happened to be aligned.
    cycle();
    expect(result.current.skewMilliseconds).toBe(100);

    // **A buffer depth, not the target.** Each tile is asked for
    // `target − its own processing`, so that every tile lands on the same total
    // lag. Handing the target straight through makes the wall climb by one
    // processing time per cycle — T026 watched two tiles induced at 120 ms
    // reach ~654 ms, perfectly aligned with each other and half a second behind
    // the world.
    expect(result.current.targetFor('a')).toBe(110); // 120 − 10 processing
    expect(result.current.targetFor('b')).toBe(105); // 120 − 15
    expect(result.current.targetFor('c')).toBe(60); // 120 − 60

    // The invariant that makes 120 a fixed point rather than a ramp.
    for (const [camera, processing] of [
      ['a', 10],
      ['b', 15],
      ['c', 60],
    ] as const) {
      expect(result.current.targetFor(camera)! + processing).toBe(120);
    }
  });

  /**
   * FR-005 / FR-006. Holding to the 400 ms tile would make every other tile
   * ~400 ms late and push the leg past its 200 ms budget — alignment bought
   * past the budget it is a leg of.
   */
  it('Releases a tile that would drag the wall past the budget, and does not follow it', () => {
    const { result } = renderHook(() => useWallAlignment(3));

    act(() => {
      result.current.reportLag('a', 20, 10);
      result.current.reportLag('b', 30, 15);
      result.current.reportLag('slow', 400, 200);
    });
    // Two cycles: hysteresis requires consecutive breaches before marking.
    cycle(2);

    expect(result.current.released.has('slow')).toBe(true);
    expect(result.current.targetFor('slow')).toBeNull();
    // Buffer depths: 30 − 10 and 30 − 15 of processing.
    expect(result.current.targetFor('a')).toBe(20);
    expect(result.current.targetFor('b')).toBe(15);
  });

  /**
   * FR-004, and asserted as an **absence of any target** rather than as
   * unchanged latency. A controller that set a single tile to its own measured
   * lag would change nothing observable and would still be wrong.
   */
  it('Sets no target at all for a single-tile wall', () => {
    const { result } = renderHook(() => useWallAlignment(1));

    act(() => {
      result.current.reportLag('only', 40, 20);
    });
    cycle(3);

    expect(result.current.targetFor('only')).toBeNull();
    expect(result.current.released.size).toBe(0);
    expect(result.current.skewMilliseconds).toBeNull();
  });

  /**
   * US2. The skew reaches observability, attributed to the tile that *defines*
   * the spread — one sample per cycle, naming the laggiest held tile, so a wall
   * of four does not weight the histogram by its size and the actionable tile
   * is named (#1931).
   */
  it('Reports the induced skew, naming the tile that set it', async () => {
    const posted: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      // Typed structurally rather than with the DOM's `RequestInit`/`Response`,
      // which this package's lint config does not carry as globals. The
      // reporter awaits the call and ignores the result, so a plain object is
      // a faithful stand-in.
      vi.fn(async (_url: string, init: { body?: unknown }) => {
        posted.push(JSON.parse(String(init.body)));
        return { ok: true, status: 202 };
      }),
    );
    vi.spyOn(console, 'info').mockImplementation(() => {});

    const { result } = renderHook(() => useWallAlignment(3, () => Promise.resolve('a-token')));

    act(() => {
      result.current.reportLag('a', 20, 10);
      result.current.reportLag('b', 30, 15);
      result.current.reportLag('laggiest', 120, 60);
    });
    cycle();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    const skew = posted.find((body) => (body as { measurement: string }).measurement === 'wall_skew');
    expect(skew).toEqual({ measurement: 'wall_skew', camera: 'laggiest', elapsedMilliseconds: 100 });

    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  /** A tile that stops reporting has gone; it must not keep setting the target. */
  it('Drops a tile that has stopped reporting rather than let it hold the wall', () => {
    const { result } = renderHook(() => useWallAlignment(3));

    act(() => {
      result.current.reportLag('a', 20, 10);
      result.current.reportLag('b', 30, 15);
      result.current.reportLag('departing', 150, 70);
    });
    cycle();
    expect(result.current.targetFor('a')).toBe(140); // 150 − 10 processing

    // Only the survivors keep reporting; the third ages out.
    for (let n = 0; n < 10; n += 1) {
      act(() => {
        result.current.reportLag('a', 20, 10);
        result.current.reportLag('b', 30, 15);
      });
      cycle();
    }

    expect(result.current.targetFor('a')).toBe(20); // 30 − 10 processing
  });

  /**
   * FR-013. A wall that can report no lag at all makes no claim — and crucially
   * produces no target, rather than a target of zero that would jolt every
   * tile's playout.
   */
  it('Makes no claim when no tile reports a lag', () => {
    const { result } = renderHook(() => useWallAlignment(4));

    cycle(3);

    expect(result.current.targetFor('a')).toBeNull();
    expect(result.current.skewMilliseconds).toBeNull();
    expect(result.current.released.size).toBe(0);
  });
});
