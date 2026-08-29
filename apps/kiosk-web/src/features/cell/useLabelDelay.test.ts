import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useLabelDelay } from './useLabelDelay.js';

/**
 * Spec 046 US2. Holding a label back so it is as old as the picture under it.
 *
 * <p>
 * <b>Every test here induces a frame age before asserting a hold.</b> A tile
 * with no readable age shows its label immediately, which is also what this
 * feature does when it is deleted — so a test that renders a tile and asserts
 * the label appears passes either way and proves nothing. The same trap spec
 * 045 fell into with an already-aligned wall.
 * </p>
 *
 * <p>
 * <b>None of this shows an operator is better off.</b> The gap is tens of
 * milliseconds, below what an eye resolves. These tests prove the mechanism
 * does what it says; whether it is worth doing is ADR-0129's argument, not a
 * test's.
 * </p>
 */

describe('useLabelDelay', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  /**
   * **The core claim (FR-008).** With an induced 120 ms frame age the new label
   * is withheld for exactly that long, so it lands describing the same moment
   * as the picture. Deleting the hold makes the first assertion fail.
   */
  it('Withholds a new label for as long as the picture is old', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 120 as number | null } },
    );

    rerender({ text: 'second', age: 120 });
    expect(result.current).toBe('first');

    act(() => {
      vi.advanceTimersByTime(119);
    });
    expect(result.current).toBe('first');

    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current).toBe('second');
  });

  /**
   * **FR-011.** No measurement, no hold. The tile has never reported a lag —
   * statistics unavailable, or a session that just restarted — and guessing a
   * delay would be an assumption wearing a measurement's clothes.
   */
  it('Shows the label at once when the frame age is unreadable', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: null as number | null } },
    );

    rerender({ text: 'second', age: null });
    expect(result.current).toBe('second');
  });

  /**
   * **FR-009.** Past the cap the wait costs the operator more than the mismatch
   * does, so freshness wins. Induced at 260 ms against a 200 ms cap — the same
   * shape a badly-buffered tile produces.
   */
  it('Shows the label at once rather than holding it past the cap', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 260 as number | null } },
    );

    rerender({ text: 'second', age: 260 });
    expect(result.current).toBe('second');
  });

  /**
   * **FR-012, and the one that actually bites.** Two updates inside one hold
   * window must arrive in order and neither may vanish. Without the monotonic
   * sequence the first timer still fires after the second and the operator is
   * left reading a superseded value — a stale number that looks live.
   */
  it('Leaves the later of two labels showing when both land inside one window', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 150 as number | null } },
    );

    rerender({ text: 'second', age: 150 });
    act(() => {
      vi.advanceTimersByTime(100);
    });
    rerender({ text: 'third', age: 150 });

    // 'second' would have been due here had its timer not been superseded.
    act(() => {
      vi.advanceTimersByTime(50);
    });
    expect(result.current).not.toBe('second');

    act(() => {
      vi.advanceTimersByTime(100);
    });
    expect(result.current).toBe('third');
  });

  /**
   * **FR-013.** A tile with no overlay is untouched. `undefined` in, `undefined`
   * out, and — the part that matters — no timer scheduled, so an empty wall
   * costs nothing. Asserted on the timer count, because asserting only on the
   * returned value passes with a timer running.
   */
  it('Schedules nothing for a tile that carries no overlay', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: undefined as string | undefined, age: 120 as number | null } },
    );

    rerender({ text: undefined, age: 120 });

    expect(result.current).toBeUndefined();
    expect(vi.getTimerCount()).toBe(0);
  });

  /**
   * **The one the no-overlay test uncovered.** A removed overlay must not
   * leave its label behind: held text outliving the overlay that owns it is a
   * stale value an operator would read as live. Removal is shown at once, and
   * the earlier hold is what makes this a real case rather than a trivial one.
   */
  it('Drops a label when its overlay is removed rather than holding the old text', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 150 as number | null } },
    );

    rerender({ text: 'second', age: 150 });
    act(() => {
      vi.advanceTimersByTime(150);
    });
    expect(result.current).toBe('second');

    rerender({ text: undefined, age: 150 });
    expect(result.current).toBeUndefined();
  });

  /**
   * A sub-millisecond hold is not worth a timer. Induced at 0.4 ms — a real
   * figure for a tile on a quiet local network — and asserted on the timer
   * count, since the returned value is identical either way.
   */
  it('Schedules nothing for a hold shorter than a millisecond', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 0.4 as number | null } },
    );

    rerender({ text: 'second', age: 0.4 });

    expect(result.current).toBe('second');
    expect(vi.getTimerCount()).toBe(0);
  });

  /**
   * **FR-014, the failure this must not have.** A held label whose tile then
   * stops reporting an age must still arrive. The hold is released on the next
   * render rather than waiting for a timer that is now measuring nothing — a
   * label that never appears is worse than one that appears unmatched.
   */
  it('Releases a held label when the tile stops reporting an age', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 150 as number | null } },
    );

    rerender({ text: 'second', age: 150 });
    expect(result.current).toBe('first');

    rerender({ text: 'second', age: null });
    expect(result.current).toBe('second');
  });

  /**
   * The age jitters every couple of seconds. If it were a dependency the timer
   * would restart on each sample and a label could be held indefinitely — the
   * mechanism silently eating the value it exists to align.
   */
  it('Does not restart the hold when the measured age jitters', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 100 as number | null } },
    );

    rerender({ text: 'second', age: 100 });
    act(() => {
      vi.advanceTimersByTime(60);
    });

    rerender({ text: 'second', age: 105 });
    rerender({ text: 'second', age: 98 });

    act(() => {
      vi.advanceTimersByTime(40);
    });
    expect(result.current).toBe('second');
  });

  /**
   * **FR-015.** The hold that was achieved, never the one that was asked for.
   * A timer fires late under load; reporting the intended figure would make
   * the metric agree with itself no matter what the browser did, which is a
   * dashboard that cannot show its own mechanism failing.
   *
   * <p>
   * Induced by advancing past the due time before letting the timer run, so a
   * report of exactly 150 fails. Passive observation on an idle box gives the
   * intended figure either way and proves nothing.
   * </p>
   */
  it('Reports the hold it achieved rather than the one it intended', () => {
    const reported: number[] = [];
    let now = 0;
    vi.spyOn(performance, 'now').mockImplementation(() => now);

    const { rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) =>
        useLabelDelay(text, age, (achieved) => reported.push(achieved)),
      { initialProps: { text: 'first' as string | undefined, age: 150 as number | null } },
    );

    rerender({ text: 'second', age: 150 });
    expect(reported, 'nothing reported while still held').toHaveLength(0);

    // The timer was due at 150 but the tab was busy until 190.
    now = 190;
    act(() => {
      vi.advanceTimersByTime(150);
    });

    expect(reported).toEqual([190]);
  });

  /**
   * Nothing held, nothing reported. A zero-length sample would read as a
   * perfect hold for a label nobody delayed — the same trap the latency
   * client names in its own guard.
   */
  it('Reports nothing for a tile whose label was never held', () => {
    const reported: number[] = [];

    const { rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) =>
        useLabelDelay(text, age, (achieved) => reported.push(achieved)),
      { initialProps: { text: 'first' as string | undefined, age: null as number | null } },
    );

    rerender({ text: 'second', age: null });
    act(() => {
      vi.advanceTimersByTime(500);
    });

    expect(reported).toHaveLength(0);
  });

  /**
   * **The cold-load path, and it is the normal one.** A tile mounts before its
   * overlay query resolves, so the first label arrives while the tile has not
   * yet reported a lag. The age becomes readable a moment later — the wall
   * controller settles every couple of seconds — and nothing about the label
   * changed in between.
   *
   * <p>
   * Found in review. The label vanished and stayed vanished: held state was
   * only ever written by the timer, so it was still holding the `undefined`
   * it was seeded with at mount, and the hook returned that the instant a
   * readable age made it start holding. The mechanism built to stop an
   * operator reading a stale value was blanking the tile instead.
   * </p>
   */
  it('Keeps showing a label that arrived before the tile could measure itself', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: undefined as string | undefined, age: null as number | null } },
    );

    // The overlay resolves; no lag sample yet, so it shows at once.
    rerender({ text: 'first', age: null });
    expect(result.current).toBe('first');

    // The tile's first lag report lands. Nothing about the label changed.
    rerender({ text: 'first', age: 120 });
    expect(result.current, 'the label must not vanish when an age becomes readable').toBe('first');
  });

  /**
   * The same defect in steady state: a label that changed while the age was
   * briefly unreadable was shown, then reverted to its predecessor as soon as
   * the age came back — a superseded value displayed as live, which is the
   * exact failure this hook exists to prevent.
   */
  it('Does not revert to a superseded label when the age becomes readable again', () => {
    const { result, rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) => useLabelDelay(text, age),
      { initialProps: { text: 'first' as string | undefined, age: 120 as number | null } },
    );

    // Stats go unreadable, and the label changes in that window.
    rerender({ text: 'second', age: null });
    expect(result.current).toBe('second');

    // Stats return.
    rerender({ text: 'second', age: 120 });
    expect(result.current, 'a shown label must not be taken back').toBe('second');
  });

  /**
   * **FR-015 again, on the path that skips the timer.** A hold released early —
   * the tile stopped reporting an age, or its age jumped past the cap, both of
   * which a live jittering measurement does — must report what it actually
   * held, not the figure it set out to hold. Otherwise a tile whose statistics
   * flicker inflates the histogram on every overlay update.
   */
  it('Reports the partial hold it achieved when the hold is released early', () => {
    const reported: number[] = [];
    let now = 0;
    vi.spyOn(performance, 'now').mockImplementation(() => now);

    const { rerender } = renderHook(
      ({ text, age }: { text: string | undefined; age: number | null }) =>
        useLabelDelay(text, age, (achieved) => reported.push(achieved)),
      { initialProps: { text: 'first' as string | undefined, age: 150 as number | null } },
    );

    rerender({ text: 'second', age: 150 });

    // Released at 100 ms of a planned 150 because the age went unreadable.
    now = 100;
    rerender({ text: 'second', age: null });

    expect(reported, 'the hold that happened, not the one that was planned').toEqual([100]);

    // And the abandoned timer must not fire a second report.
    act(() => {
      vi.advanceTimersByTime(500);
    });
    expect(reported).toEqual([100]);
  });
});
