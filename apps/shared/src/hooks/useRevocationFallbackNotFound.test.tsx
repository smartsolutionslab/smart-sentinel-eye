// @vitest-environment jsdom
import { renderHook } from '@testing-library/react';
import { configureStore, createListenerMiddleware } from '@reduxjs/toolkit';
import type { ReactElement, ReactNode } from 'react';
import { Provider } from 'react-redux';
import { describe, expect, it } from 'vitest';
import { useRevocationFallback, type RevocationQueryState, type RevocationSource } from './useRevocationFallback.js';

/**
 * Spec 313 (#2750) T002 — the hook's new `notFoundRevokes` field in
 * isolation (plan.md §1.2 / §5 row 2), independent of any page. A 404 only
 * strikes when the caller opts in by passing `notFoundRevokes: true`; a
 * caller that omits it, or passes `false`, must behave exactly as spec 310
 * — a 404 resets the count, same as any other non-403 settled response.
 *
 * Spec 314 (#2762) harness-only update: the hook now needs a Provider (it
 * dispatches via `useDispatch`) and a listener middleware in the store (it
 * throws without one). Every case here drives the hook purely through its
 * render-path argument, exactly as before, so the source passed is inert —
 * nothing is ever dispatched against this store, so it never contributes a
 * settlement of its own. No assertion or case below is changed.
 */

function settled(status: number, requestId: string, notFoundRevokes?: boolean): RevocationQueryState {
  return { error: { status }, isFetching: false, requestId, notFoundRevokes };
}

function ok(requestId: string): RevocationQueryState {
  return { error: undefined, isFetching: false, requestId };
}

/** Never matches — this suite drives the hook only through its render-path argument (spec 314). */
const inertSource: RevocationSource<undefined> = {
  endpoint: {
    select: () => () => ({ requestId: undefined, error: undefined }),
    matchFulfilled: (_action: unknown): _action is never => false,
    matchRejected: (_action: unknown): _action is never => false,
  },
  args: undefined,
};

function createTestStore() {
  const listenerMiddleware = createListenerMiddleware();
  return configureStore({
    reducer: { _unused: (state: number = 0) => state },
    middleware: (getDefaultMiddleware) => getDefaultMiddleware().prepend(listenerMiddleware.middleware),
  });
}

/** A fresh store per render — each `renderFallback` call is fully isolated, as before. */
function ProviderWrapper({ children }: { children: ReactNode }): ReactElement {
  return <Provider store={createTestStore()}>{children}</Provider>;
}

function renderFallback(subject: string, query: RevocationQueryState) {
  return renderHook(({ subject: s, query: q }) => useRevocationFallback(s, q, inertSource), {
    initialProps: { subject, query },
    wrapper: ProviderWrapper,
  });
}

describe('useRevocationFallback — notFoundRevokes (spec 313)', () => {
  it('three consecutive 404s with notFoundRevokes: true trip the fallback, same as three 403s', () => {
    const { result, rerender } = renderFallback('camera-c', settled(404, 'r1', true));

    rerender({ subject: 'camera-c', query: settled(404, 'r2', true) });
    rerender({ subject: 'camera-c', query: settled(404, 'r3', true) });

    expect(result.current).toBe(true);
  });

  it('two consecutive 404s with notFoundRevokes: true stay below the threshold', () => {
    const { result, rerender } = renderFallback('camera-c', settled(404, 'r1', true));

    rerender({ subject: 'camera-c', query: settled(404, 'r2', true) });

    expect(result.current).toBe(false);
  });

  it('403 and qualifying 404 strikes accumulate toward the same threshold', () => {
    const { result, rerender } = renderFallback('camera-c', settled(403, 'r1', true));

    rerender({ subject: 'camera-c', query: settled(404, 'r2', true) });
    rerender({ subject: 'camera-c', query: settled(403, 'r3', true) });

    expect(result.current).toBe(true);
  });

  it('a non-refusal between qualifying 404s resets the count to zero', () => {
    const { result, rerender } = renderFallback('camera-c', settled(404, 'r1', true));

    rerender({ subject: 'camera-c', query: settled(404, 'r2', true) });
    rerender({ subject: 'camera-c', query: settled(503, 'r3', true) });
    rerender({ subject: 'camera-c', query: settled(404, 'r4', true) });

    expect(result.current).toBe(false);
  });

  it('three consecutive 404s with notFoundRevokes: false never count — identical to spec 310', () => {
    const { result, rerender } = renderFallback('camera-c', settled(404, 'r1', false));

    rerender({ subject: 'camera-c', query: settled(404, 'r2', false) });
    rerender({ subject: 'camera-c', query: settled(404, 'r3', false) });

    expect(result.current).toBe(false);
  });

  // The fence every list page relies on (FR-004): omitting the field — what
  // every list page's query state does today — must not newly start
  // counting 404s against it.
  it('three consecutive 404s with notFoundRevokes omitted never count — the list-page contract', () => {
    const { result, rerender } = renderFallback('camera-c', settled(404, 'r1'));

    rerender({ subject: 'camera-c', query: settled(404, 'r2') });
    rerender({ subject: 'camera-c', query: settled(404, 'r3') });

    expect(result.current).toBe(false);
  });

  it('a success after qualifying 404 strikes restores false', () => {
    const { result, rerender } = renderFallback('camera-c', settled(404, 'r1', true));

    rerender({ subject: 'camera-c', query: settled(404, 'r2', true) });
    rerender({ subject: 'camera-c', query: settled(404, 'r3', true) });
    expect(result.current).toBe(true);

    rerender({ subject: 'camera-c', query: ok('r4') });
    expect(result.current).toBe(false);
  });
});
