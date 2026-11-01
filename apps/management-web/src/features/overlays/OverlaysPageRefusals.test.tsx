import {
  listOverlaysMock,
  publishMock,
  archiveMock,
  branchMock,
  refusal,
  chain,
  response,
  renderPage,
  mutationState,
  OverlaysPage,
} from '../../test/overlaysPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { store } from '../../app/store.js';

/**
 * #1952, found on RulesPage and identical here. All four mutations discarded
 * their result, so a refused publish, archive, branch or revert looked exactly
 * like a successful one. With optimistic concurrency live (ADR-0113) a refusal
 * is routine, not exceptional.
 *
 * Asserted on the rendered alert, not on the mutation state: capturing the
 * error and not rendering it is exactly as silent as before.
 */
describe('OverlaysPage — a refused mutation is surfaced', () => {
  beforeEach(() => {
    publishMock.mockClear();
    archiveMock.mockClear();
    branchMock.mockClear();
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
    listOverlaysMock.mockReset();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  it('Shows the server’s reason when a publish is refused as stale', () => {
    mutationState.publish = {
      isLoading: false,
      error: refusal(
        409,
        'OVERLAY_REVISION_STALE',
        'Overlay has changed since version 0 (now 1). Re-read it and reapply the change.',
      ),
    };
    renderPage();

    expect(screen.getByRole('alert')).toHaveTextContent(/has changed since version 0/i);
  });

  it('Surfaces a refusal from any of the four mutations, not only publish', () => {
    mutationState.archive = { isLoading: false, error: refusal(409, 'OVERLAY_REVISION_STALE', 'archive refused') };
    const { unmount } = renderPage();
    expect(screen.getByRole('alert')).toHaveTextContent('archive refused');
    unmount();

    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false, error: refusal(409, 'OVERLAY_REVISION_STALE', 'branch refused') };
    renderPage();
    expect(screen.getByRole('alert')).toHaveTextContent('branch refused');
  });

  /**
   * A 409 without a detail is the case the wording matters most for: with
   * nothing from the server the page has to say it itself, and "try again" is
   * the one thing it must not say — resubmitting unchanged replays the stale
   * intent over whoever wrote in between (ADR-0113).
   */
  it('Tells a stale operator to reload rather than to try again', async () => {
    const refetch = vi.fn();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch,
    });
    mutationState.revert = { isLoading: false, error: refusal(409, 'OVERLAY_REVISION_STALE') };

    const user = userEvent.setup();
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/reload/i);
    expect(alert).not.toHaveTextContent(/try again/i);

    await user.click(screen.getByRole('button', { name: /reload/i }));
    expect(refetch).toHaveBeenCalled();
  });

  it('Offers no reload for a refusal that is not a conflict', () => {
    mutationState.publish = { isLoading: false, error: refusal(400, 'OVERLAY_INVALID', 'The label is empty.') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/label is empty/i);
    expect(alert).not.toHaveTextContent(/someone else/i);
    expect(screen.queryByRole('button', { name: /reload/i })).not.toBeInTheDocument();
  });

  it('Shows no alert while every mutation is clean', () => {
    renderPage();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  /**
   * Spec 298 (issue #2693) US1 — new behaviour, RED (ADR-0139/0144). The
   * refusal box still paints the call-site alpha blend
   * (`bg-accent-fault/10`), not the opaque `FaultNotice` tint.
   */
  it('Shows a refused change on the shared fault notice, not call-site alpha', () => {
    mutationState.publish = { isLoading: false, error: refusal(409, 'OVERLAY_NAME_TAKEN') };
    renderPage();

    const alert = screen.getByRole('alert');
    expect(alert).toHaveClass('bg-accent-fault-subtle', 'border-accent-fault-border', 'text-accent-fault');
    expect([...alert.classList].some((c) => /\/\d+$/.test(c))).toBe(false);
  });
});

/**
 * Spec 310 (#2725) T004, plan.md §5 row 4. Three consecutive 403 refreshes of
 * the overlays list must drop the stale rows — the page renders exactly as a
 * first-load refusal does (FR-004). RED today: `OverlaysPage.tsx` wires no
 * revocation fallback, so `data` keeps the last successful page regardless
 * of how many 403s accumulate.
 *
 * Rendered under a plain `MemoryRouter`, not `renderPage`'s data router —
 * `RouterProvider` wraps its matched route in `React.memo` keyed on
 * `router.state`/`router.routes` identity, and `rerender()` with the same
 * `router` instance never changes either, so the memo bails and
 * `OverlaysPage` never re-invokes `useListOverlaysQuery` a second time: the
 * `rerender()` calls below were silently inert under the data router (no
 * second or third call ever reached the mock). `OverlaysPage` only calls
 * `useNavigate()` here, which works under any router context, and this
 * suite asserts on rendered content, not on the URL — so `MemoryRouter`
 * carries no cost, and it's the same pattern `CamerasPage.test.tsx`'s own
 * revocation-fallback tests already use successfully.
 */
describe('OverlaysPage — revocation fallback, three consecutive 403s (spec 310 #2725)', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    // Independent of the mutation describes above — reset explicitly rather
    // than rely on execution order.
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
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

  function renderWithMemoryRouter() {
    return render(
      <Provider store={store}>
        <MemoryRouter>
          <OverlaysPage />
        </MemoryRouter>
      </Provider>,
    );
  }

  it('Keeps the stale rows after only two consecutive 403s', () => {
    listOverlaysMock.mockReturnValue(forbiddenRefresh('overlays-r1'));
    const { rerender } = renderWithMemoryRouter();

    listOverlaysMock.mockReturnValue(forbiddenRefresh('overlays-r2'));
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <OverlaysPage />
        </MemoryRouter>
      </Provider>,
    );

    expect(screen.getByText('Line-1 Title')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load overlays/i);
  });

  it('Shows no stale row and the existing failure banner, exactly as a first-load refusal, after a third consecutive 403', () => {
    listOverlaysMock.mockReturnValue(forbiddenRefresh('overlays-r1'));
    const { rerender, container } = renderWithMemoryRouter();

    listOverlaysMock.mockReturnValue(forbiddenRefresh('overlays-r2'));
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <OverlaysPage />
        </MemoryRouter>
      </Provider>,
    );

    listOverlaysMock.mockReturnValue(forbiddenRefresh('overlays-r3'));
    rerender(
      <Provider store={store}>
        <MemoryRouter>
          <OverlaysPage />
        </MemoryRouter>
      </Provider>,
    );

    expect(screen.queryByText('Line-1 Title')).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent(/could not load overlays/i);

    // FR-004: byte-for-byte the same render as a first-load failure.
    listOverlaysMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch: vi.fn(),
    });
    const { container: firstLoadRefusal } = renderWithMemoryRouter();

    expect(container.innerHTML).toBe(firstLoadRefusal.innerHTML);
  });
});
