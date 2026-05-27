import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';
import type {
  Layout,
  ListLayoutsResponse,
} from '@smart-sentinel-eye/shared/api/layouts.api';

const listLayoutsMock = vi.fn();
const publishMock = vi.fn(async () => ({ data: 1 }));
const archiveMock = vi.fn(async () => ({ data: 1 }));
const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: (...args: unknown[]) => listLayoutsMock(...args),
    usePublishRevisionMutation: () => [publishMock, { isLoading: false }],
    useArchiveRevisionMutation: () => [archiveMock, { isLoading: false }],
    useBranchDraftRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false }],
    useRevertRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useCreateLayoutDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined }],
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

const { LayoutsPage } = await import('./LayoutsPage.js');

function chain(overrides: Partial<Layout> = {}): Layout {
  return {
    layoutIdentifier: '11111111-1111-1111-1111-111111111111',
    name: 'Line-1',
    createdAt: '2026-05-26T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [
      {
        revisionIdentifier: '33333333-3333-3333-3333-333333333333',
        revisionNumber: 1,
        state: 'Draft',
        cameraIdentifier: '44444444-4444-4444-4444-444444444444',
        overlayIdentifier: null,
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '22222222-2222-2222-2222-222222222222',
        publishedAt: null,
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

function response(chains: Layout[]): ListLayoutsResponse {
  return { chains, published: [] };
}

function renderPage() {
  return render(
    <Provider store={store}>
      <LayoutsPage />
    </Provider>,
  );
}

describe('LayoutsPage', () => {
  beforeEach(() => {
    listLayoutsMock.mockReset();
    publishMock.mockClear();
    archiveMock.mockClear();
  });

  it('Shows an empty-state message when no layouts exist', () => {
    listLayoutsMock.mockReturnValue({
      data: response([]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText(/no layouts to show/i)).toBeInTheDocument();
  });

  it('Renders one card per chain with its newest revision summary', () => {
    listLayoutsMock.mockReturnValue({
      data: response([chain({ name: 'Line-1' }), chain({ name: 'Line-2', layoutIdentifier: 'aa' })]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    expect(screen.getByText('Line-1')).toBeInTheDocument();
    expect(screen.getByText('Line-2')).toBeInTheDocument();
  });

  it('Clicking Publish on a Draft fires the publish mutation', async () => {
    const user = userEvent.setup();
    listLayoutsMock.mockReturnValue({
      data: response([chain()]),
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    });

    renderPage();

    await user.click(screen.getByRole('button', { name: /^publish$/i }));
    expect(publishMock).toHaveBeenCalledWith({
      layoutIdentifier: '11111111-1111-1111-1111-111111111111',
      revisionNumber: 1,
    });
  });

  it('Shows a retry control when the list query fails', async () => {
    const user = userEvent.setup();
    const refetch = vi.fn();
    listLayoutsMock.mockReturnValue({
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
