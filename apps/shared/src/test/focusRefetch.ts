import { act } from '@testing-library/react';
import { setupListeners } from '@reduxjs/toolkit/query';
import { afterEach } from 'vitest';

/**
 * Spec 317 (#2751) T002, plan.md §4 row 1. RTK Query's `setupListeners` keeps
 * a MODULE-GLOBAL `initialized` flag (read from source, RTK 2.12.0): once one
 * call has run, a later call — even against a different store — silently
 * no-ops until the first is unsubscribed (plan.md §4's own warning: "a red
 * that looks like a missing feature").
 *
 * The cleanup is ONE `afterEach`, registered here at module load — not one
 * registered freshly inside every call to `installFocusListeners` (i.e. from
 * inside each test). Proved by a counterfactual: an `afterEach` callback
 * registered from WITHIN a running `it()` is never invoked between that test
 * and the next one (only between the FOLLOWING pair onward, and never for a
 * file with exactly one test) — Vitest only wires a describe block's hooks
 * during its synchronous collection pass, before any test body runs. A
 * version of this helper that called `afterEach` from inside
 * `installFocusListeners` left every test after the first in a multi-test
 * file with RTK's `initialized` flag still true from the previous test, so
 * `setupListeners` silently no-opped and the window event never reached that
 * test's own store — a red indistinguishable from a missing feature, except
 * that implementing the feature would never turn it green. One top-level
 * `afterEach` — the same pattern `@testing-library/react` recommends for its
 * own `cleanup` — runs after every test in whichever file imports this
 * module, regardless of how many of its tests call `installFocusListeners`.
 */
let activeStopListening: (() => void) | undefined;

afterEach(() => {
  activeStopListening?.();
  activeStopListening = undefined;
});

export function installFocusListeners(store: { dispatch: Parameters<typeof setupListeners>[0] }): void {
  activeStopListening = setupListeners(store.dispatch);
}

/**
 * Fires the real `window` "focus" event `setupListeners` listens for.
 * `window.Event`, not the bare global — this file's eslint config has no
 * `Event` global declared (unlike `window`/`document`), and adding one
 * widens a shared config for a single call site.
 */
export function fireWindowFocus(): void {
  window.dispatchEvent(new window.Event('focus'));
}

/**
 * Fires the real `window` "visibilitychange" event. `setupListeners`
 * attaches this listener to `window`, not `document` (plan.md §4), so it is
 * dispatched there; `document.visibilityState` is set explicitly first
 * rather than relying on jsdom's default.
 */
export function fireVisible(): void {
  Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
  window.dispatchEvent(new window.Event('visibilitychange'));
}

/**
 * Awaits every query thunk currently running in `store`, per ADR-0150 — a
 * store condition (RTK Query's own `getRunningQueriesThunk`), never a fixed
 * tick count or timer.
 */
export async function settleRunningQueries(
  store: { dispatch: (action: unknown) => unknown },
  api: { util: { getRunningQueriesThunk: () => unknown } },
): Promise<void> {
  await act(async () => {
    const running = store.dispatch(api.util.getRunningQueriesThunk()) as Promise<unknown>[];
    await Promise.all(running);
  });
}

/** How many of `fetchMock`'s calls were a request whose URL contains `pathFragment`. */
export function countRequests(fetchMock: { mock: { calls: unknown[][] } }, pathFragment: string): number {
  return fetchMock.mock.calls.filter((call) => (call[0] as Request).url.includes(pathFragment)).length;
}
