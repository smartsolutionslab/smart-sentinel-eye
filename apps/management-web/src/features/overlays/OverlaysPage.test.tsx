import {
  listOverlaysMock,
  publishMock,
  archiveMock,
  branchMock,
  editDraftMock,
  chain,
  response,
  renderPage,
  mutationState,
  OverlaysPage,
} from '../../test/overlaysPageHarness.js';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { store } from '../../app/store.js';

describe('OverlaysPage', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    publishMock.mockClear();
    archiveMock.mockClear();
    editDraftMock.mockClear();
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
  });

  it('Shows an empty-state message when no overlays exist', () => {
    listOverlaysMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText(/no overlays to show/i)).toBeInTheDocument();
  });

  it('Groups the state filters under the name Filter by state', () => {
    listOverlaysMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    const group = screen.getByRole('group', { name: 'Filter by state' });
    expect(
      within(group)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['All', 'Draft', 'Published', 'Archived']);
  });

  it('Renders one card per chain showing the label preview text', () => {
    listOverlaysMock.mockReturnValue({
      data: response([chain({ name: 'Line-1 Title' })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Line-1 Title')).toBeInTheDocument();
    expect(screen.getByText('Production Line 1')).toBeInTheDocument();
  });

  /**
   * Spec 300 (#2349) phase-6 S5: `firstTextOrShapesOnly` replaced
   * `summarised.labels[0]!.text`, which would have shown the Box's own
   * fields (or crashed) rather than the set's one Text element.
   */
  it("Shows the Text element's text for a mixed [Box, Text] revision", () => {
    listOverlaysMock.mockReturnValue({
      data: response([
        chain({
          name: 'Mixed overlay',
          revisions: [
            {
              revisionIdentifier: '33333333-3333-3333-3333-333333333333',
              revisionNumber: 1,
              state: 'Draft',
              elements: [
                {
                  kind: 'Box',
                  color: '#D32F2FFF',
                  normalizedX: 0.05,
                  normalizedY: 0.05,
                  normalizedWidth: 0.2,
                  normalizedHeight: 0.2,
                },
                {
                  kind: 'Text',
                  color: '#9F9F9FD9',
                  text: 'Production Line 1',
                  normalizedX: 0.1,
                  normalizedY: 0.1,
                  normalizedWidth: 0.3,
                  normalizedHeight: 0.08,
                  fontSizePx: 32,
                },
              ],
              createdAt: '2026-05-27T10:00:00Z',
              createdBy: '22222222-2222-2222-2222-222222222222',
              publishedAt: null,
              archivedAt: null,
            },
          ],
        }),
      ]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Production Line 1')).toBeInTheDocument();
    expect(screen.queryByText('Shapes only')).not.toBeInTheDocument();
  });

  /**
   * Spec 300 (#2349) phase-6 S5: a shapes-only revision carries no Text
   * element at all — `summarised.labels[0]!.text` would have crashed here.
   */
  it('Shows "Shapes only" for a revision with no Text element', () => {
    listOverlaysMock.mockReturnValue({
      data: response([
        chain({
          name: 'Shapes-only overlay',
          revisions: [
            {
              revisionIdentifier: '33333333-3333-3333-3333-333333333333',
              revisionNumber: 1,
              state: 'Draft',
              elements: [
                {
                  kind: 'Box',
                  color: '#D32F2FFF',
                  normalizedX: 0.05,
                  normalizedY: 0.05,
                  normalizedWidth: 0.2,
                  normalizedHeight: 0.2,
                },
              ],
              createdAt: '2026-05-27T10:00:00Z',
              createdBy: '22222222-2222-2222-2222-222222222222',
              publishedAt: null,
              archivedAt: null,
            },
          ],
        }),
      ]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Shapes only')).toBeInTheDocument();
  });

  it('Clicking Publish on a Draft fires the publish mutation', async () => {
    const user = userEvent.setup();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /^publish$/i }));
    expect(publishMock).toHaveBeenCalledWith({
      overlayIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 1,
      version: 0,
    });
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listOverlaysMock.mockReturnValue({
      data: undefined,
      isLoading: false,
      isFetching: false,
      error: { status: 500 },
      refetch,
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /retry/i }));
    expect(refetch).toHaveBeenCalledOnce();
  });
});

/**
 * Spec 152 T004, US1 — new behaviour, RED (ADR-0139). `OverlaysPage` offers no
 * `Edit draft` button today, so every test below fails on a missing control,
 * not on an import or a compile error. This is the issue's literal scenario:
 * `{D}` — the row a freshly-created overlay is on — offers Publish and
 * Discard draft and nothing else (see spec.md "The correction that changes
 * the design"), and copying `LayoutsPage`'s `Edit (new draft)` gate
 * (`live !== undefined || fullyArchived`) would not fix it, because that gate
 * is false on `{D}` too.
 */
describe('OverlaysPage — editing a draft in place (spec 152 US1)', () => {
  function draftRevision(revisionNumber: number, text: string) {
    return {
      revisionIdentifier: `r${revisionNumber}`,
      revisionNumber,
      state: 'Draft' as const,
      elements: [
        {
          kind: 'Text' as const,
          color: '#9F9F9FD9',
          text,
          normalizedX: 0.1,
          normalizedY: 0.1,
          normalizedWidth: 0.3,
          normalizedHeight: 0.08,
          fontSizePx: 32,
        },
      ],
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '22222222-2222-2222-2222-222222222222',
      publishedAt: null,
      archivedAt: null,
    };
  }

  beforeEach(() => {
    editDraftMock.mockClear();
    branchMock.mockClear();
  });

  /** FR-001: offered on the exact shape the issue reports — a lone open draft. */
  it('Offers Edit draft on a chain whose only revision is an open draft', () => {
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByRole('button', { name: /^edit draft$/i })).toBeInTheDocument();
  });

  /** FR-002: no branch, no PATCH, no optimistic update — only the dialog opens. */
  /**
   * Rewritten for spec 305 (#2350) T011/T007: `Edit draft` navigates
   * directly to that draft's own edit URL rather than opening a dialog;
   * FR-002's "sends nothing" half is unchanged — still no branch, no PATCH.
   */
  it("Navigates to that draft's own edit URL and sends no request", async () => {
    const user = userEvent.setup();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    const { router } = renderPage();

    await user.click(screen.getByRole('button', { name: /^edit draft$/i }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe('/overlays/11111111-1111-1111-1111-111111111111/revisions/1/edit'),
    );
    expect(branchMock).not.toHaveBeenCalled();
    expect(editDraftMock).not.toHaveBeenCalled();
  });

  /**
   * Decision 1: the draft acted on is `chainView(...).draft` — the newest
   * Draft, not "the" draft. Consistent with what Publish and Discard already
   * target on the same row.
   */
  /**
   * Rewritten for spec 305 (#2350): which draft the navigation targets is
   * now proven by the URL itself (revision 3, the newer one), rather than by
   * a dialog's seeded content — the content itself is `OverlayEditPage`'s
   * own seeding, covered by its own suite.
   */
  it('Navigates to the newest draft on a chain holding two open drafts', async () => {
    const user = userEvent.setup();
    listOverlaysMock.mockReturnValue({
      data: response([chain({ revisions: [draftRevision(2, 'Older draft'), draftRevision(3, 'Newer draft')] })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    const { router } = renderPage();

    await user.click(screen.getByRole('button', { name: /^edit draft$/i }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe('/overlays/11111111-1111-1111-1111-111111111111/revisions/3/edit'),
    );
  });
});

/**
 * Spec 305 (#2350) FR-002, new behaviour, RED (ADR-0139/0144). `OverlaysPage`
 * still opens `OverlayEditorDialog` for every one of these actions today —
 * `setDialogOpen`/`setEditTarget`, never a navigation — so every assertion
 * below fails on the URL never changing, not on a missing control (the
 * dialog-opening tests above already cover the controls existing and being
 * called correctly; this is additive, per plan.md's two-colour split, and
 * leaves every test above unmodified).
 *
 * Rendered under a real router (`createMemoryRouter` + `RouterProvider`,
 * mirroring `CameraDetailPageNavigation.test.tsx`) with stub destinations for
 * the two new routes, so a navigation is observed by the URL and the
 * destination's own rendered content actually changing — not by a mocked
 * `useNavigate`.
 */
describe('OverlaysPage — navigating instead of opening a dialog (spec 305, #2350)', () => {
  beforeEach(() => {
    editDraftMock.mockClear();
    branchMock.mockClear();
    mutationState.publish = { isLoading: false };
    mutationState.archive = { isLoading: false };
    mutationState.branch = { isLoading: false };
    mutationState.revert = { isLoading: false };
    listOverlaysMock.mockReset();
  });

  function StubNewOverlayPage() {
    return <h1>New overlay (stub)</h1>;
  }

  function StubEditOverlayPage() {
    const { overlayIdentifier, revisionNumber } = useParams();
    return (
      <h1>
        Editing {overlayIdentifier} revision {revisionNumber} (stub)
      </h1>
    );
  }

  function renderPageWithRouter(initial = '/overlays') {
    const router = createMemoryRouter(
      [
        { path: '/overlays', element: <OverlaysPage /> },
        { path: '/overlays/new', element: <StubNewOverlayPage /> },
        { path: '/overlays/:overlayIdentifier/revisions/:revisionNumber/edit', element: <StubEditOverlayPage /> },
      ],
      { initialEntries: [initial] },
    );
    render(
      <Provider store={store}>
        <RouterProvider router={router} />
      </Provider>,
    );
    return router;
  }

  it('New overlay navigates to /overlays/new', async () => {
    const user = userEvent.setup();
    listOverlaysMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    const router = renderPageWithRouter();

    await user.click(screen.getByRole('button', { name: /^new overlay$/i }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/overlays/new'));
  });

  it('Edit draft navigates directly to the edit URL for that revision, with no branch/POST request first', async () => {
    const user = userEvent.setup();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    const router = renderPageWithRouter();

    await user.click(screen.getByRole('button', { name: /^edit draft$/i }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe('/overlays/11111111-1111-1111-1111-111111111111/revisions/1/edit'),
    );
    expect(branchMock).not.toHaveBeenCalled();
    expect(editDraftMock).not.toHaveBeenCalled();
  });

  it('Edit (new draft) branches first, then navigates to the branched revision', async () => {
    const user = userEvent.setup();
    branchMock.mockResolvedValueOnce({ data: 9 });
    listOverlaysMock.mockReturnValue({
      data: response([
        chain({
          revisions: [
            {
              revisionIdentifier: 'r1',
              revisionNumber: 1,
              state: 'Published',
              elements: chain().revisions[0]!.elements,
              createdAt: '2026-05-27T10:00:00Z',
              createdBy: '22222222-2222-2222-2222-222222222222',
              publishedAt: '2026-05-28T10:00:00Z',
              archivedAt: null,
            },
          ],
        }),
      ]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    const router = renderPageWithRouter();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));

    await waitFor(() =>
      expect(router.state.location.pathname).toBe('/overlays/11111111-1111-1111-1111-111111111111/revisions/9/edit'),
    );
  });

  it('A refused branch does not navigate; the page stays on /overlays', async () => {
    const user = userEvent.setup();
    branchMock.mockResolvedValueOnce({ error: { status: 409, data: { title: 'OVERLAY_REVISION_STALE' } } });
    listOverlaysMock.mockReturnValue({
      data: response([
        chain({
          revisions: [
            {
              revisionIdentifier: 'r1',
              revisionNumber: 1,
              state: 'Published',
              elements: chain().revisions[0]!.elements,
              createdAt: '2026-05-27T10:00:00Z',
              createdBy: '22222222-2222-2222-2222-222222222222',
              publishedAt: '2026-05-28T10:00:00Z',
              archivedAt: null,
            },
          ],
        }),
      ]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
    const router = renderPageWithRouter();

    await user.click(screen.getByRole('button', { name: /edit \(new draft\)/i }));
    await act(async () => {
      await Promise.resolve();
    });

    expect(router.state.location.pathname).toBe('/overlays');
  });
});

/**
 * Spec 427 (issue #2668) US3, R10, phase 4a RED: the state filters are ToggleChips, so the current
 * filter is announced as pressed and the pressed state moves when another is chosen. Today the
 * filters are raw buttons with no aria-pressed, so no button is ever pressed.
 */
describe('OverlaysPage — the state filters announce which one is on (spec 427 R10)', () => {
  beforeEach(() => {
    listOverlaysMock.mockReset();
    listOverlaysMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });
  });

  function pressedFilterNames() {
    return screen.queryAllByRole('button', { pressed: true }).map((button) => button.textContent);
  }

  it('Presses All on first render and no other filter', () => {
    renderPage();

    expect(screen.getByRole('button', { name: 'All', pressed: true })).toBeInTheDocument();
    expect(pressedFilterNames()).toEqual(['All']);
  });

  it('Moves the pressed state to the chosen filter', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'Archived' }));

    expect(screen.getByRole('button', { name: 'Archived', pressed: true })).toBeInTheDocument();
    expect(pressedFilterNames()).toEqual(['Archived']);
  });
});
