// @vitest-environment jsdom
import { renderHook } from '@testing-library/react';
import { StrictMode } from 'react';
import { describe, expect, it } from 'vitest';
import {
  REVOCATION_STRIKE_THRESHOLD,
  useRevocationFallback,
  type RevocationQueryState,
} from './useRevocationFallback.js';

/**
 * Spec 310 (#2725) T002 — the hook's contract in isolation (plan.md §1.2 /
 * §5 row 2), independent of any page. `useRevocationFallback` counts a
 * strike per settled, not-yet-counted `requestId`; resets on any settled
 * non-403 and on a subject change; and reports `refused` only once the
 * count reaches `REVOCATION_STRIKE_THRESHOLD`.
 */

function settled(status: number, requestId: string): RevocationQueryState {
  return { error: { status }, isFetching: false, requestId };
}

function ok(requestId: string): RevocationQueryState {
  return { error: undefined, isFetching: false, requestId };
}

function renderFallback(subject: string, query: RevocationQueryState) {
  return renderHook(({ subject: s, query: q }) => useRevocationFallback(s, q), {
    initialProps: { subject, query },
  });
}

describe('useRevocationFallback', () => {
  it('names the threshold as 3', () => {
    expect(REVOCATION_STRIKE_THRESHOLD).toBe(3);
  });

  it('stays false below the threshold — two consecutive 403s keep the record', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));
    expect(result.current).toBe(false);

    rerender({ subject: 'camera-c', query: settled(403, 'r2') });
    expect(result.current).toBe(false);
  });

  it('is true once three consecutive settled responses for the same subject are 403', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));

    rerender({ subject: 'camera-c', query: settled(403, 'r2') });
    rerender({ subject: 'camera-c', query: settled(403, 'r3') });

    expect(result.current).toBe(true);
  });

  it('a non-403 settled response between two refusals resets the count to zero', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));

    rerender({ subject: 'camera-c', query: settled(403, 'r2') });
    rerender({ subject: 'camera-c', query: settled(503, 'r3') });
    rerender({ subject: 'camera-c', query: settled(403, 'r4') });

    expect(result.current).toBe(false);
  });

  it('a success once the refusal surface is already showing restores false', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));

    rerender({ subject: 'camera-c', query: settled(403, 'r2') });
    rerender({ subject: 'camera-c', query: settled(403, 'r3') });
    expect(result.current).toBe(true);

    rerender({ subject: 'camera-c', query: ok('r4') });
    expect(result.current).toBe(false);
  });

  /**
   * Edge case named in spec.md: "a re-render that observes the same settled
   * request must not count it again". A re-render carrying the identical
   * `requestId` — e.g. a re-render provoked by something unrelated to this
   * query — must not inflate the count past what the underlying requests
   * actually warrant.
   */
  it('the same requestId observed across repeated renders counts only once', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));

    rerender({ subject: 'camera-c', query: settled(403, 'r1') });
    rerender({ subject: 'camera-c', query: settled(403, 'r1') });

    expect(result.current).toBe(false);
  });

  it('a request still in flight is not a response and never counts', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));
    rerender({ subject: 'camera-c', query: settled(403, 'r2') });

    // r3 carries the same 403 RTK leaves on the cache entry while a
    // background refetch is pending — it must not be counted while in flight.
    rerender({ subject: 'camera-c', query: { error: { status: 403 }, isFetching: true, requestId: 'r3' } });

    expect(result.current).toBe(false);
  });

  /**
   * Spec.md edge case: "a test double without `requestId` never counts" —
   * so every existing page test that mocks a query hook without supplying
   * one renders exactly as before.
   */
  it('a settled response with no requestId never counts', () => {
    const noRequestId: RevocationQueryState = { error: { status: 403 }, isFetching: false, requestId: undefined };
    const { result, rerender } = renderFallback('camera-c', noRequestId);

    rerender({ subject: 'camera-c', query: noRequestId });
    rerender({ subject: 'camera-c', query: noRequestId });

    expect(result.current).toBe(false);
  });

  it('counts refusals per subject — opening a different subject does not inherit its strikes', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1'));
    rerender({ subject: 'camera-c', query: settled(403, 'r2') });
    expect(result.current).toBe(false);

    // The operator opens camera D. One 403 for D must not be camera C's
    // third strike.
    rerender({ subject: 'camera-d', query: settled(403, 'd1') });
    expect(result.current).toBe(false);
  });

  /**
   * N2 (phase 6). The hook's docblock claims StrictMode safety — state is
   * adjusted during render, not in an effect, specifically so React's
   * intentional double-render/double-effect in `StrictMode` cannot double-
   * count one response. Nothing exercised that claim until now.
   */
  describe('under React.StrictMode', () => {
    function renderFallbackStrict(subject: string, query: RevocationQueryState) {
      return renderHook(({ subject: s, query: q }) => useRevocationFallback(s, q), {
        initialProps: { subject, query },
        wrapper: StrictMode,
      });
    }

    it('reaches true from three settled 403s, each counted once despite the double render', () => {
      const { result, rerender } = renderFallbackStrict('camera-c', settled(403, 'r1'));
      expect(result.current).toBe(false);

      rerender({ subject: 'camera-c', query: settled(403, 'r2') });
      expect(result.current).toBe(false);

      rerender({ subject: 'camera-c', query: settled(403, 'r3') });
      expect(result.current).toBe(true);
    });

    it('does not double-count a single settled 403 that StrictMode renders twice', () => {
      const { result, rerender } = renderFallbackStrict('camera-c', settled(403, 'r1'));
      expect(result.current).toBe(false);

      // Re-rendering with the SAME requestId — the shape of StrictMode's own
      // double render of an unchanged commit — must not add a second strike.
      rerender({ subject: 'camera-c', query: settled(403, 'r1') });
      expect(result.current).toBe(false);

      rerender({ subject: 'camera-c', query: settled(403, 'r2') });
      expect(result.current).toBe(false);

      rerender({ subject: 'camera-c', query: settled(403, 'r3') });
      expect(result.current).toBe(true);
    });
  });
});
