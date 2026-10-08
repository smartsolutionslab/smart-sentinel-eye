// @vitest-environment jsdom
import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { createRevocationHarness, HarnessProvider } from '../test/revocationHarness.js';

/**
 * Spec 314 (#2762) T003 — the race itself (spec.md §0, §1 US1). An
 * `autoBatchEnhancer({ type: 'raf' })` store (the RTK default every app
 * store gets — plan.md §1.1) defers subscriber notification past the next
 * dispatched action. `useRevocationFallback` today counts strikes only from
 * what it renders, so a settlement whose notification is coalesced with the
 * next request's is never counted — and, symmetrically, never resets the
 * count. `revocationHarness` makes that coalescing a held, releasable state
 * instead of a timing accident (FR-008).
 *
 * Every "dropped" scenario below asserts its render-log precondition FIRST —
 * that the in-between settlement genuinely never rendered — so a failure on
 * the outcome assertion that follows is evidence the hook is wrong, not
 * evidence the harness missed the race. The precondition assertions pass
 * today; only the outcome assertions are expected to fail before T006.
 */

function subjectArgs(key: string) {
  return { key };
}

describe('useRevocationFallback — settlements coalesced by autobatch (spec 314)', () => {
  it('a 403 whose notification is coalesced with the next request still counts (the dropped strike)', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    render(
      <HarnessProvider harness={harness}>
        <harness.Probe subject="camera-x" args={args} />
      </HarnessProvider>,
    );

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    const r1 = harness.request(403, args, { forceRefetch: true });
    await r1.settled;
    harness.flush(); // r1 rendered settled

    const r2 = harness.request(403, args, { forceRefetch: true });
    await r2.settled; // r2 settles in the store — notification still held
    const r3 = harness.request(403, args, { forceRefetch: true }); // r3 starts before release
    harness.flush(); // releases the held notification — renders r3 pending

    // Precondition: r2's settled state was never rendered (must pass today).
    expect(harness.renderLog.some((entry) => entry.requestId === r2.requestId && !entry.isFetching)).toBe(false);

    await r3.settled;
    harness.flush();

    // Outcome: three settled 403s for one subject must refuse (spec 310).
    expect(harness.refusedByRenderRef.current).toBe(true);
  });

  it('a 200 coalesced between refusals still resets the count (the dropped reset)', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    render(
      <HarnessProvider harness={harness}>
        <harness.Probe subject="camera-x" args={args} />
      </HarnessProvider>,
    );

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    const r1 = harness.request(403, args, { forceRefetch: true });
    await r1.settled;
    harness.flush(); // r1 rendered settled — one strike

    const r2 = harness.request('ok', args, { forceRefetch: true });
    await r2.settled; // r2 (the 200) settles in the store — notification still held
    const r3 = harness.request(403, args, { forceRefetch: true }); // r3 starts before release
    harness.flush(); // releases the held notification — renders r3 pending

    // Precondition: r2's settled (reset) state was never rendered.
    expect(harness.renderLog.some((entry) => entry.requestId === r2.requestId && !entry.isFetching)).toBe(false);

    await r3.settled;
    harness.flush(); // r3 rendered settled

    const r4 = harness.request(403, args, { forceRefetch: true });
    await r4.settled;
    harness.flush(); // r4 rendered settled

    // Outcome: r1, [r2 reset], r3, r4 is a run of two 403s, not three — must not refuse.
    expect(harness.refusedByRenderRef.current).toBe(false);
  });

  it('a request that starts and settles entirely inside one held window still counts (the one-frame case)', async () => {
    const harness = createRevocationHarness();
    const args = subjectArgs('camera-x');
    render(
      <HarnessProvider harness={harness}>
        <harness.Probe subject="camera-x" args={args} />
      </HarnessProvider>,
    );

    const load = harness.request('ok', args);
    await load.settled;
    harness.flush();

    const r1 = harness.request(403, args, { forceRefetch: true });
    await r1.settled;
    harness.flush(); // r1 rendered settled

    const r2 = harness.request(403, args, { forceRefetch: true });
    await r2.settled; // r2 starts and settles entirely before any release
    const r3 = harness.request(403, args, { forceRefetch: true });
    await r3.settled; // r3 also settles before any release
    harness.flush(); // a single render jumps straight from r1 to r3 settled

    // Precondition: r2 was never rendered at all — not pending, not settled.
    expect(harness.renderLog.some((entry) => entry.requestId === r2.requestId)).toBe(false);

    // Outcome: r1, r2, r3 are three settled 403s — must refuse.
    expect(harness.refusedByRenderRef.current).toBe(true);
  });

  it('without the listener middleware in the store, rendering throws naming the missing middleware', () => {
    const harness = createRevocationHarness({ withListenerMiddleware: false });
    const args = subjectArgs('camera-x');

    expect(() =>
      render(
        <HarnessProvider harness={harness}>
          <harness.Probe subject="camera-x" args={args} />
        </HarnessProvider>,
      ),
    ).toThrow(/listener middleware/i);
  });
});
