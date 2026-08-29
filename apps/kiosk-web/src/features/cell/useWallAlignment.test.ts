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
      result.current.reportLag('a', 'cam-a', 20, 10);
      result.current.reportLag('b', 'cam-b', 30, 15);
      result.current.reportLag('c', 'cam-c', 120, 60);
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
      result.current.reportLag('a', 'cam-a', 20, 10);
      result.current.reportLag('b', 'cam-b', 30, 15);
      result.current.reportLag('slow', 'cam-slow', 400, 200);
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
      result.current.reportLag('only', 'cam-only', 40, 20);
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
      result.current.reportLag('a', 'cam-a', 20, 10);
      result.current.reportLag('b', 'cam-b', 30, 15);
      result.current.reportLag('laggiest', 'cam-laggiest', 120, 60);
    });
    cycle();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    const skew = posted.find((body) => (body as { measurement: string }).measurement === 'wall_skew');
    // The endpoint wants the camera, not the tile key — the tile key is internal.
    expect(skew).toEqual({ measurement: 'wall_skew', camera: 'cam-laggiest', elapsedMilliseconds: 100 });

    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  /** A tile that stops reporting has gone; it must not keep setting the target. */
  it('Drops a tile that has stopped reporting rather than let it hold the wall', () => {
    const { result } = renderHook(() => useWallAlignment(3));

    act(() => {
      result.current.reportLag('a', 'cam-a', 20, 10);
      result.current.reportLag('b', 'cam-b', 30, 15);
      result.current.reportLag('departing', 'cam-departing', 150, 70);
    });
    cycle();
    expect(result.current.targetFor('a')).toBe(140); // 150 − 10 processing

    // Only the survivors keep reporting; the third ages out.
    for (let n = 0; n < 10; n += 1) {
      act(() => {
        result.current.reportLag('a', 'cam-a', 20, 10);
        result.current.reportLag('b', 'cam-b', 30, 15);
      });
      cycle();
    }

    expect(result.current.targetFor('a')).toBe(20); // 30 − 10 processing
  });

  /**
   * **Code review finding.** `targetFor` used to gate on `released`, but a tile
   * the wall cannot hold is not badged until it has breached on consecutive
   * cycles — so on the first cycle it sat in neither set and was handed a
   * target it could not reach, collapsing the buffer of the very tile the wall
   * had just declined to carry.
   */
  it('Gives no target to a tile it cannot hold, even before that tile is badged', () => {
    const { result } = renderHook(() => useWallAlignment(3));

    act(() => {
      result.current.reportLag('a', 'cam-a', 20, 10);
      result.current.reportLag('b', 'cam-b', 30, 15);
      result.current.reportLag('slow', 'cam-slow', 400, 200);
    });
    // ONE cycle: not enough to badge, but enough to decide it cannot be held.
    cycle();

    expect(result.current.released.has('slow'), 'not badged yet').toBe(false);
    expect(result.current.targetFor('slow'), 'and still given nothing').toBeNull();
    expect(result.current.targetFor('a')).toBe(20);
  });

  /**
   * **Code review finding.** The target is the max *measured* lag and
   * `jitterBufferTarget` is a playout floor, so a held tile reads at or above
   * its setpoint. Feeding that back unfiltered makes the wall climb on noise
   * alone — the slow form of the runaway T026 caught fast.
   */
  it('Does not ratchet upward on per-cycle noise', () => {
    const { result } = renderHook(() => useWallAlignment(2));

    act(() => {
      result.current.reportLag('a', 'cam-a', 100, 60);
      result.current.reportLag('b', 'cam-b', 100, 60);
    });
    cycle();
    const first = result.current.targetFor('a');

    // Twenty cycles of small positive noise, as a playout floor produces.
    // Lag and buffer move together so each tile's *processing* stays at 40 ms —
    // otherwise the setpoint legitimately tracks the processing change and the
    // assertion would be measuring the wrong thing.
    for (let n = 1; n <= 20; n += 1) {
      act(() => {
        result.current.reportLag('a', 'cam-a', 100 + (n % 3), 60 + (n % 3));
        result.current.reportLag('b', 'cam-b', 100 + ((n + 1) % 3), 60 + ((n + 1) % 3));
      });
      cycle();
    }

    // With processing fixed, the setpoint moves only if the target moved.
    expect(result.current.targetFor('a')).toBe(first);
  });

  /**
   * **Code review finding.** Shrinking below two tiles tore down the interval
   * but kept `target` and `released`, so the surviving tile was pinned to a
   * target computed from departed cameras and a badge could never clear.
   */
  it('Stops claiming anything when the wall shrinks to one tile', () => {
    const { result, rerender } = renderHook(({ tiles }) => useWallAlignment(tiles), {
      initialProps: { tiles: 3 },
    });

    act(() => {
      result.current.reportLag('a', 'cam-a', 20, 10);
      result.current.reportLag('b', 'cam-b', 30, 15);
      result.current.reportLag('slow', 'cam-slow', 400, 200);
    });
    cycle(2);
    expect(result.current.released.has('slow')).toBe(true);
    expect(result.current.targetFor('a')).not.toBeNull();

    rerender({ tiles: 1 });

    expect(result.current.targetFor('a'), 'no target from departed tiles').toBeNull();
    expect(result.current.released.size, 'and no badge left behind').toBe(0);
    expect(result.current.skewMilliseconds).toBeNull();
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

  /**
   * Spec 046 SC-004. The link between an induced buffer and the delay a label
   * is held for: `frameAgeFor` is the figure `useLabelDelay` consumes, and
   * both halves were tested while the join between them was not.
   *
   * <p>
   * <b>Induced, twice, and asserted on the change.</b> Reporting one lag and
   * asserting a plausible number passes with the wiring crossed to another
   * tile, or frozen at whatever arrived first.
   * </p>
   */
  it('Reports each tile its own frame age, and follows an induced buffer', () => {
    const { result } = renderHook(() => useWallAlignment(2));

    act(() => {
      result.current.reportLag('a', 'cam-a', 40, 12);
      result.current.reportLag('b', 'cam-b', 140, 30);
    });

    expect(result.current.frameAgeFor('a')).toBe(40);
    expect(result.current.frameAgeFor('b')).toBe(140);

    // Buffer induced on 'a' alone: its age follows, and 'b' is left alone.
    act(() => {
      result.current.reportLag('a', 'cam-a', 190, 12);
    });

    expect(result.current.frameAgeFor('a')).toBe(190);
    expect(result.current.frameAgeFor('b')).toBe(140);
  });

  /**
   * A tile that has never reported gets null, not zero. A zero age reads as a
   * perfectly fresh picture and would hold no label at all — the failure
   * looking exactly like the success.
   */
  it('Reports no frame age for a tile that has never measured one', () => {
    const { result } = renderHook(() => useWallAlignment(2));

    expect(result.current.frameAgeFor('a')).toBeNull();
    expect(result.current.frameAgeFor('a')).not.toBe(0);
  });
});
