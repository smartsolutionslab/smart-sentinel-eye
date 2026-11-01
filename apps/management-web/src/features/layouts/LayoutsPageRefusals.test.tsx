import {
  listLayoutsMock,
  refusal,
  chain,
  response,
  renderPage,
  mutationState,
  LayoutsPage,
} from '../../test/layoutsPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { CONFLICT_FALLBACK } from '@smart-sentinel-eye/shared/api/problemDetail';
import { store } from '../../app/store.js';

/**
 * Spec 231 (#2433) US1 — new behaviour, RED. `LayoutsPage` passes a plain
 * fallback string to `problemDetail` instead of
 * `isStaleConflict(mutationError) ? CONFLICT_FALLBACK : 'Could not apply that
 * change.'`, the pattern OverlaysPage.tsx and RulesPage.tsx already use. Every
 * test here injects an error through `publishState`, since `mutationError` on
 * this page reads `publishState.error ?? archiveState.error ?? branchState.error
 * ?? revertState.error` and publish is first in that chain.
 */
describe('LayoutsPage — the stale-conflict fallback (spec 231 US1)', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    listLayoutsMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Shows the conflict fallback, not the generic sentence, for a stale refusal without a detail', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'LAYOUT_REVISION_STALE') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(CONFLICT_FALLBACK);
    expect(alert).not.toHaveTextContent('Could not apply that change.');
    expect(screen.getByRole('button', { name: /reload/i })).toBeInTheDocument();
  });

  it('Keeps the generic fallback for a non-stale 409 without a detail', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'LAYOUT_NAME_TAKEN') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not apply that change.');
    expect(alert).not.toHaveTextContent(/someone else/i);
  });

  it('Shows the server’s own detail verbatim when a stale refusal carries one', () => {
    mutationState.publish = {
      isLoading: false,
      error: refusal(409, 'LAYOUT_REVISION_STALE', 'Layout has changed since version 0 (now 1).'),
    };
    renderPage();

    expect(screen.getByRole('alert')).toHaveTextContent('Layout has changed since version 0 (now 1).');
  });

  /**
   * Spec 298 (issue #2693) US1 — new behaviour, RED (ADR-0139/0144). The
   * refusal box still paints the call-site alpha blend
   * (`bg-accent-fault/10`), not the opaque `FaultNotice` tint.
   */
  it('Shows a refused change on the shared fault notice, not call-site alpha', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'LAYOUT_NAME_TAKEN') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveClass('bg-accent-fault-subtle', 'border-accent-fault-border', 'text-accent-fault');
    expect([...alert.classList].some((c) => /\/\d+$/.test(c))).toBe(false);
  });
});

/**
 * Spec 310 (#2725) T004, plan.md §5 row 4. Three consecutive 403 refreshes of
 * the layouts list must drop the stale rows — the page renders exactly as a
 * first-load refusal does (FR-004). RED today: `LayoutsPage.tsx` wires no
 * revocation fallback, so `data` keeps the last successful page regardless
 * of how many 403s accumulate.
 */
describe('LayoutsPage — revocation fallback, three consecutive 403s (spec 310 #2725)', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    // Independent of the mutation describes above — reset explicitly rather
    // than rely on execution order, since a stray `error` here would show
    // the mutation FaultNotice and confuse this describe's own assertions.
    mutationState.publish = { isLoading: false };
  });

  function forbiddenRefresh(requestId: string) {
    return {
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: { status: 403 },
      requestId,
      refetch: vi.fn(),
    };
  }

  it('Keeps the stale rows after only two consecutive 403s', () => {
    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r1'));
    const { rerender } = renderPage();

    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r2'));
    rerender(
      <Provider store={store}>
        <LayoutsPage />
      </Provider>,
    );

    expect(screen.getByText('Line-1')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load layouts/i);
  });

  it('Shows no stale row and the existing failure banner, exactly as a first-load refusal, after a third consecutive 403', () => {
    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r1'));
    const { rerender, container } = renderPage();

    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r2'));
    rerender(
      <Provider store={store}>
        <LayoutsPage />
      </Provider>,
    );

    listLayoutsMock.mockReturnValue(forbiddenRefresh('layouts-r3'));
    rerender(
      <Provider store={store}>
        <LayoutsPage />
      </Provider>,
    );

    expect(screen.queryByText('Line-1')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load layouts/i);

    // FR-004: byte-for-byte the same render as a first-load failure.
    listLayoutsMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch: vi.fn(),
    });
    const { container: firstLoadRefusal } = renderPage();

    expect(container.innerHTML).toBe(firstLoadRefusal.innerHTML);
  });
});
