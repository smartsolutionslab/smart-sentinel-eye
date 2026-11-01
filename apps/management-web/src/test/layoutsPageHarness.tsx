import { vi } from 'vitest';
import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import type { Layout, ListLayoutsResponse } from '@smart-sentinel-eye/shared/api/layouts.api';
import { store } from '../app/store.js';

// Import this module FIRST in a test file: its `vi.mock` calls only reach modules loaded after it, and
// `store` below loads the real API slices.
export const listLayoutsMock = vi.fn();
export const publishMock = vi.fn(async () => ({ data: 1 }));
export const archiveMock = vi.fn(async () => ({ data: 1 }));
export const branchMock = vi.fn(async () => ({ data: 2 }));
export const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
export const editDraftMock = vi.fn(async () => ({ data: 2 }));
// Spec 273 (#2632) — hoisted so L3's guard test can assert on it. It used to
// be an inline anonymous `vi.fn()` returned fresh on every render, which
// satisfied the hook but could never be asserted against.
export const revertMock = vi.fn(async () => ({ data: 1 }));

// Spec 231 T001: mutable module state, mirroring OverlaysPage.test.tsx. The
// mutation *state* the page reads back — a vi.mock factory is hoisted above
// every test, so the fixed-literal `{ isLoading: false }` this used to return
// gave no test a way to inject a mutation error; the tests that care set this
// in-place and beforeEach clears it.
export const mutationState: { publish: { isLoading: boolean; error?: unknown } } = {
  publish: { isLoading: false },
};

/** An RTK Query error in the shape the gateway's RFC-7807 body arrives in. */
export function refusal(status: number, title: string, detail?: string) {
  return { status, data: { title, status, ...(detail === undefined ? {} : { detail }) } };
}

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => listLayoutsMock(...args),
    // Both `data` and `currentData` are supplied, set to the SAME value —
    // this mock cannot distinguish the two fields, so it cannot catch a
    // regression back to `data`. That discrimination lives in
    // LayoutEditorDialogChainRetention.test.tsx, which drives the real
    // useGetLayoutQuery instead of mocking it. `currentData` is supplied here
    // only because the dialog reads it; without it every edit-mode assertion
    // in this file would see `currentChain === undefined` and hit FR-002's
    // gate.
    useGetLayoutQuery: () => ({
      data: chain(),
      currentData: chain(),
      isLoading: false,
      isError: false,
      isFetching: false,
    }),
    usePublishRevisionMutation: () => [publishMock, mutationState.publish],
    useArchiveRevisionMutation: () => [archiveMock, { isLoading: false }],
    useBranchDraftRevisionMutation: () => [branchMock, { isLoading: false }],
    useRevertRevisionMutation: () => [revertMock, { isLoading: false }],
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [editDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: { items: [], count: 0, offset: 0, limit: 50 },
      isLoading: false,
    }),
  };
});

export const { LayoutsPage } = await import('../features/layouts/LayoutsPage.js');

export function chain(overrides: Partial<Layout> = {}): Layout {
  return {
    layoutIdentifier: '11111111-1111-1111-1111-111111111111',
    version: 0,
    fab: 'munich',
    name: 'Line-1',
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Draft',
        gridRows: 1,
        gridCols: 1,
        tiles: [
          {
            cameraIdentifier: '44444444-4444-4444-4444-444444444444',
            overlayIdentifier: null,
            row: 0,
            col: 0,
            rowSpan: 1,
            colSpan: 1,
          },
        ],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

export function response(chains: Layout[]): ListLayoutsResponse {
  return { chains, published: [] };
}

export function renderPage() {
  return render(
    <Provider store={store}>
      <LayoutsPage />
    </Provider>,
  );
}
