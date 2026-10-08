// @vitest-environment jsdom
import { render } from '@testing-library/react';
import { setupListeners } from '@reduxjs/toolkit/query';
import { afterEach, describe, expect, it } from 'vitest';
import { createRevocationHarness, HarnessProvider } from '../test/revocationHarness.js';

/**
 * Spec 317 (#2751) T003, plan.md §4 row 2. Composition with spec 314/#2762:
 * a focus-triggered refetch is counted by `useRevocationFallback`'s existing
 * listener path exactly as a Retry-click refetch is, with no change to the
 * hook (plan.md §2). These are GREEN FENCES, not the red this spec adds —
 * RTK Query's own `setupListeners`/`refetchOnFocus` and the post-#2762
 * listener-based strike counting already compose; nothing here is new
 * production behaviour. The red is plan.md §4 rows 3-9.
 *
 * `harness.Subscribe` (new, T003) holds a REAL subscription — `Probe`'s
 * `useQueryState` does not subscribe, so it is invisible to RTK Query's
 * focus refetch (`windowEventHandling.ts` only ever refetches a cache entry
 * that has at least one subscription).
 *
 * `stopListening` is reassigned per test and torn down by ONE describe-level
 * `afterEach` — RTK's `setupListeners` keeps a module-global `initialized`
 * flag, so registering a fresh `afterEach` callback inside every `it` (each
 * closing over that test's own `stopListening`) left the flag cleared only
 * sometimes, and a later test's `setupListeners` call silently no-opped:
 * its `window` events then reached a PREVIOUS test's now-unmounted store,
 * not its own, and nothing it asserted could ever observe a request —
 * proved by running the suspect test alone, where it passed every time.
 */

function subjectArgs(key: string) {
  return { key };
}

describe('useRevocationFallback composes with RTK Query focus refetch (spec 317, #2751)', () => {
  let stopListening: () => void = () => {};
  afterEach(() => {
    stopListening();
  });

  it('three focus refreshes answered 403 while notifications stay held throughout are refused once released, with no in-between settlement ever rendered', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    stopListening = setupListeners(harness.store.dispatch);

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    render(
      <HarnessProvider harness={harness}>
        <harness.Subscribe args={args} refetchOnFocus={true} />
        <harness.Probe subject="camera-x" args={args} />
      </HarnessProvider>,
    );

    // Three focus refreshes, each answered 403. `settleBare` (not `settle`)
    // awaits each settlement WITHOUT Testing Library's `act()` — `act()`
    // itself forces React to re-check the external store's snapshot, which
    // would render the held state early regardless of this harness's own
    // `queueNotification` override (proved by running it both ways).
    for (const outcome of [403, 403, 403] as const) {
      harness.responses.push(outcome);
      window.dispatchEvent(new window.Event('focus'));
      await harness.settleBare();
    }

    // Precondition: none of the three settlements above was ever rendered —
    // a render reflecting the in-flight request (`isFetching: true`) can
    // still happen without `act()`, but no render may show one of them
    // SETTLED (`isFetching: false`) before the notification is released.
    expect(harness.renderLog.some((entry) => entry.requestId !== load.requestId && !entry.isFetching)).toBe(false);
    expect(harness.refusedByRenderRef.current).toBe(false);

    // Releasing the held notification jumps straight from "loaded" to
    // "refused" in one render — the listener path (#2762) counted all three
    // settlements at dispatch time, before this render ever happened.
    harness.flush();

    expect(harness.refusedByRenderRef.current).toBe(true);
  });

  it('a focus and a visibility signal arriving back to back cause exactly one queryFn call', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    stopListening = setupListeners(harness.store.dispatch);

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    render(
      <HarnessProvider harness={harness}>
        <harness.Subscribe args={args} refetchOnFocus={true} />
        <harness.Probe subject="camera-x" args={args} />
      </HarnessProvider>,
    );

    const callsBeforeFocus = harness.queryFnCallCount();
    harness.responses.push('ok');

    // Back to back, synchronously — the tab-switch case plan.md §2.3
    // describes: RTK Query's own in-flight condition aborts the second.
    window.dispatchEvent(new window.Event('focus'));
    Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
    window.dispatchEvent(new window.Event('visibilitychange'));

    await harness.settle();
    harness.flush();

    expect(harness.queryFnCallCount() - callsBeforeFocus).toBe(1);
  });

  it('403, 403, 200, 403, 403 via focus is not refused (the reset in the middle breaks the run)', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    stopListening = setupListeners(harness.store.dispatch);

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    render(
      <HarnessProvider harness={harness}>
        <harness.Subscribe args={args} refetchOnFocus={true} />
        <harness.Probe subject="camera-x" args={args} />
      </HarnessProvider>,
    );

    for (const outcome of [403, 403, 'ok', 403, 403] as const) {
      harness.responses.push(outcome);
      window.dispatchEvent(new window.Event('focus'));
      await harness.settle();
      harness.flush();
    }

    // All 5 pushed outcomes must actually have been consumed (shifted by the
    // queryFn) -- otherwise this passes vacuously if focus never triggers a
    // refetch at all (no settlements, refusedByRenderRef trivially stays false).
    expect(harness.responses).toHaveLength(0);
    expect(harness.refusedByRenderRef.current).toBe(false);
  });

  it('a subscription without refetchOnFocus is not refetched on focus', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    stopListening = setupListeners(harness.store.dispatch);

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    render(
      <HarnessProvider harness={harness}>
        <harness.Subscribe args={args} />
      </HarnessProvider>,
    );

    const callsBeforeFocus = harness.queryFnCallCount();

    window.dispatchEvent(new window.Event('focus'));
    await harness.settleBare();

    expect(harness.queryFnCallCount()).toBe(callsBeforeFocus);
  });
});
