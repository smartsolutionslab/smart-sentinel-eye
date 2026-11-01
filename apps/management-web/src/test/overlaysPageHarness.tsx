import { vi } from 'vitest';
import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import type { ListOverlaysResponse, Overlay } from '@smart-sentinel-eye/shared/api/overlays.api';
import { store } from '../app/store.js';

export const listOverlaysMock = vi.fn();
export const publishMock = vi.fn(async () => ({ data: 1 }));
export const archiveMock = vi.fn(async () => ({ data: 1 }));
export const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
// Spec 037: hoisted from an inline anonymous vi.fn. It satisfied the hook and
// could not be asserted on, so nothing could check that recovering an archived
// overlay actually branches.
export const branchMock = vi.fn(async (): Promise<{ data: number } | { error: unknown }> => ({ data: 2 }));
// Spec 152 T004/T006. Asserted-on so "Edit draft" (US1) and "Edit (new draft)"
// (US2) can be told apart by what they send: US1 sends nothing at all, US2
// sends a branch and, only on success, opens the same dialog.
export const editDraftMock = vi.fn(async () => ({ data: 1 }));

// The mutation *state* the page reads back. Mutable module state, because a
// vi.mock factory is hoisted above every test; the tests that care set these
// in-place and beforeEach clears them.
export const mutationState: Record<
  'publish' | 'archive' | 'branch' | 'revert',
  { isLoading: boolean; error?: unknown }
> = {
  publish: { isLoading: false },
  archive: { isLoading: false },
  branch: { isLoading: false },
  revert: { isLoading: false },
};

/** An RTK Query error in the shape the gateway's RFC-7807 body arrives in. */
export function refusal(status: number, title: string, detail?: string) {
  return { status, data: { title, status, ...(detail === undefined ? {} : { detail }) } };
}

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: (...args: unknown[]) => listOverlaysMock(...args),
    usePublishOverlayRevisionMutation: () => [publishMock, mutationState.publish],
    useArchiveOverlayRevisionMutation: () => [archiveMock, mutationState.archive],
    useBranchDraftOverlayRevisionMutation: () => [branchMock, mutationState.branch],
    useRevertOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), mutationState.revert],
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    // Spec 152. `OverlayEditorDialog` is rendered for real by this page (not
    // mocked), and its `vi.mock` factory above spreads `...actual` — so an
    // unmocked new hook here would reach the real RTK Query and fire a
    // request from a component test the moment the dialog mounts.
    useEditDraftOverlayRevisionMutation: () => [editDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    // Both `data` and `currentData` are supplied — see the fuller comment in
    // OverlayEditorDialog.test.tsx's own mock of this hook: this page-level
    // mock is arg-independent and so cannot itself discriminate the two
    // (OverlayEditorDialogChainRetention.test.tsx is the file that can, with
    // the real hook), but a mock that only ever supplied one would hide a
    // regression in the field this page's test suite doesn't read.
    useGetOverlayQuery: () => ({
      data: { overlayIdentifier: '11111111-1111-1111-1111-111111111111', version: 0 },
      currentData: { overlayIdentifier: '11111111-1111-1111-1111-111111111111', version: 0 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
  };
});

export const { OverlaysPage } = await import('../features/overlays/OverlaysPage.js');

export function chain(overrides: Partial<Overlay> = {}): Overlay {
  return {
    overlayIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    name: 'Line-1 Title',
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Draft',
        elements: [
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
    ...overrides,
  };
}

export function response(chains: Overlay[]): ListOverlaysResponse {
  return { chains, published: [] };
}

/**
 * Spec 305 (#2350), T011: `OverlaysPage` now calls `useNavigate()`
 * unconditionally (`New overlay`, `Edit draft`, `Edit (new draft)`), which
 * throws outside a Router context — so every call site in this file needs
 * one, not only the tests that actually assert on navigation. Stub
 * destinations for the two new routes, mirroring the local
 * `renderPageWithRouter` the "navigating instead of opening a dialog"
 * describe block below already uses; `router` is additive on the return
 * value, so every existing `renderPage();` / `const { unmount } =
 * renderPage();` call site keeps working unchanged.
 */
export function renderPage(initial = '/overlays') {
  const router = createMemoryRouter(
    [
      { path: '/overlays', element: <OverlaysPage /> },
      { path: '/overlays/new', element: <h1>New overlay (stub)</h1> },
      { path: '/overlays/:overlayIdentifier/revisions/:revisionNumber/edit', element: <h1>Edit overlay (stub)</h1> },
    ],
    { initialEntries: [initial] },
  );
  const result = render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { router, ...result };
}
