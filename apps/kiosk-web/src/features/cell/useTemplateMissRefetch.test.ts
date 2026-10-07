import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook } from '@testing-library/react';

/**
 * Spec 301 (#2720) US2, T015 — new hook, no implementation yet.
 *
 * <p>
 * Bounded retry for a placeholder `Text` element whose own template is
 * missing from the kiosk's resolved-text snapshot — a genuinely new
 * template SystemVariables has not indexed yet (spec.md FR-007). Refetches
 * at 1 s / 2 s / 4 s, stops early once the miss clears, logs
 * `resolved-text-template-miss` exactly once per publication key after the
 * third attempt, and must never call `refetch` while the query itself is
 * skipped (`LayoutGrid.tsx:427-430` skips when there is no overlay
 * identifier, no placeholder, or no named fab).
 * </p>
 *
 * <p>
 * <b>Red by construction.</b> `./useTemplateMissRefetch.js` does not exist
 * yet — this is the premise T015 names explicitly ("new hook, no existing
 * hook file to extend"), so the whole file fails to resolve the module
 * under today's code. Quoted verbatim in the PR, per the test-writer brief.
 * </p>
 */

const logResilienceEventMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/observability/resilienceLog', () => ({
  logResilienceEvent: (...args: unknown[]) => logResilienceEventMock(...args),
}));

const { useTemplateMissRefetch } = await import('./useTemplateMissRefetch.js');

interface Props {
  overlayIdentifier: string | null;
  publicationKey: string | undefined;
  hasMiss: boolean;
  skip: boolean;
  refetch: () => void;
}

function renderMissRefetch(initial: Props) {
  return renderHook((props: Props) => useTemplateMissRefetch(props), { initialProps: initial });
}

describe('useTemplateMissRefetch', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    logResilienceEventMock.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Refetches at 1 s, 2 s and 4 s while the miss persists, and not before', () => {
    const refetch = vi.fn();
    renderMissRefetch({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    expect(refetch, 'no refetch at mount — the first attempt is scheduled, not immediate').not.toHaveBeenCalled();

    vi.advanceTimersByTime(1_000);
    expect(refetch).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(2_000);
    expect(refetch).toHaveBeenCalledTimes(2);

    vi.advanceTimersByTime(4_000);
    expect(refetch).toHaveBeenCalledTimes(3);

    // No fourth attempt — bounded at three.
    vi.advanceTimersByTime(10_000);
    expect(refetch).toHaveBeenCalledTimes(3);
  });

  it('Stops early once the miss clears, scheduling no further attempt', () => {
    const refetch = vi.fn();
    const { rerender } = renderMissRefetch({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    vi.advanceTimersByTime(1_000);
    expect(refetch).toHaveBeenCalledTimes(1);

    // The snapshot arrived — the template is no longer missing.
    rerender({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: false,
      skip: false,
      refetch,
    });

    vi.advanceTimersByTime(10_000);
    expect(refetch, 'the miss cleared, so the 2 s/4 s attempts must never fire').toHaveBeenCalledTimes(1);
    expect(logResilienceEventMock, 'a cleared miss is a success, not a resilience event').not.toHaveBeenCalled();
  });

  it('Logs resolved-text-template-miss exactly once, after the third attempt, for one overlay', () => {
    const refetch = vi.fn();
    renderMissRefetch({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    vi.advanceTimersByTime(1_000 + 2_000 + 4_000);
    expect(refetch).toHaveBeenCalledTimes(3);

    expect(logResilienceEventMock).toHaveBeenCalledTimes(1);
    expect(logResilienceEventMock).toHaveBeenCalledWith('hub', 'resolved-text-template-miss', { overlay: 'ovl-1' });

    // Does not keep firing every tick once exhausted.
    vi.advanceTimersByTime(10_000);
    expect(logResilienceEventMock).toHaveBeenCalledTimes(1);
  });

  it('Resets the backoff when the publication key changes', () => {
    const refetch = vi.fn();
    const { rerender } = renderMissRefetch({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    vi.advanceTimersByTime(1_000 + 2_000 + 4_000);
    expect(refetch).toHaveBeenCalledTimes(3);
    expect(logResilienceEventMock).toHaveBeenCalledTimes(1);

    // A new publish, a different still-missing template — a fresh episode.
    rerender({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Pressure: {{p}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    expect(refetch, 'no refetch synchronously on the key changing').toHaveBeenCalledTimes(3);

    vi.advanceTimersByTime(1_000);
    expect(refetch, 'the 1 s attempt runs again for the new episode').toHaveBeenCalledTimes(4);

    vi.advanceTimersByTime(2_000 + 4_000);
    expect(logResilienceEventMock, 'a second, independent miss episode logs again').toHaveBeenCalledTimes(2);
  });

  it('Clears its timers on unmount', () => {
    const refetch = vi.fn();
    const { unmount } = renderMissRefetch({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    vi.advanceTimersByTime(500);
    unmount();
    vi.advanceTimersByTime(10_000);

    expect(refetch, 'no attempt fires after unmount').not.toHaveBeenCalled();
  });

  it('Never calls refetch while the query is skipped, even while a miss is reported', () => {
    const refetch = vi.fn();
    renderMissRefetch({
      overlayIdentifier: null,
      publicationKey: undefined,
      hasMiss: true,
      skip: true,
      refetch,
    });

    vi.advanceTimersByTime(10_000);

    expect(
      refetch,
      'LayoutGrid.tsx skips the query itself for this tile — the hook must respect that',
    ).not.toHaveBeenCalled();
    expect(logResilienceEventMock).not.toHaveBeenCalled();
  });
});

/**
 * ADVERSARIAL (test-adversary addendum, not one of T008–T015's required red
 * set — reported separately per the brief).
 *
 * <p>
 * The plan asserts "resets on a new publication key" and "stops early once
 * the miss clears" as two separate facts. Neither test above exercises them
 * TOGETHER, in one state transition, mid-backoff — which is exactly the
 * real race spec.md's acceptance scenarios describe: a legitimate new
 * publish can arrive between a scheduled retry firing and the next one
 * being due. If the hook keys its pending timer by a value it forgets to
 * clear on this combined transition, the old episode's leftover timer could
 * fire at its stale schedule alongside (or instead of) the new episode's
 * fresh one — either a spurious extra `refetch`, or the new episode's first
 * attempt arriving late.
 * </p>
 *
 * <p>
 * This is a genuine uncovered edge rather than a wrong expectation: nothing
 * in spec.md or plan.md says what should happen if the OLD episode's own
 * in-flight attempt is what resolves the race (its refetch happens to
 * return the very snapshot that clears the miss) at the same instant a new,
 * different miss starts. The assertion below only pins the shape that is
 * unambiguous from FR-007 — one clean 1 s/2 s/4 s cycle for the new key,
 * with no leftover call from the old one — and flags for product/design
 * review rather than blocking US2 if the real implementation needs a
 * different call sequence to get there.
 * </p>
 */
describe('useTemplateMissRefetch — a legitimate new publish mid-backoff (adversarial)', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    logResilienceEventMock.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Starts one clean 1 s/2 s/4 s cycle for a new miss that arrives between two scheduled attempts of the old one', () => {
    const refetch = vi.fn();
    const { rerender } = renderMissRefetch({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Temperature: {{t}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    // The old episode's first attempt fires at 1 s.
    vi.advanceTimersByTime(1_000);
    expect(refetch).toHaveBeenCalledTimes(1);

    // At 1.5 s — strictly between the old episode's 1 s attempt and its 2 s
    // one — a new publish lands: the old miss clears AND a different
    // template is immediately missing too, in the same rerender.
    vi.advanceTimersByTime(500);
    rerender({
      overlayIdentifier: 'ovl-1',
      publicationKey: 'Pressure: {{p}}',
      hasMiss: true,
      skip: false,
      refetch,
    });

    // The old episode's 2 s mark (now 0.5 s away) must not fire — it would
    // be a refetch for a miss that no longer exists under this key.
    vi.advanceTimersByTime(500);
    expect(refetch, "the old episode's stale 2 s attempt must not survive the key change").toHaveBeenCalledTimes(1);

    // The new episode's own 1 s attempt, timed from ITS OWN start (the
    // rerender above), not from the old episode's clock.
    vi.advanceTimersByTime(500);
    expect(refetch, "the new episode's first attempt, one full second after it started").toHaveBeenCalledTimes(2);

    vi.advanceTimersByTime(2_000);
    expect(refetch).toHaveBeenCalledTimes(3);
    vi.advanceTimersByTime(4_000);
    expect(refetch).toHaveBeenCalledTimes(4);

    // Exactly one miss log, for the new key's own exhausted cycle — not two,
    // and not zero.
    expect(logResilienceEventMock).toHaveBeenCalledTimes(1);
    expect(logResilienceEventMock).toHaveBeenCalledWith('hub', 'resolved-text-template-miss', { overlay: 'ovl-1' });
  });
});
