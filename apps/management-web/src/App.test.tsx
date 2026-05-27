import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListCamerasQuery: () => ({
      data: { items: [], count: 0, offset: 0, limit: 50 },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useRegisterCameraMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, isLoading: false, error: undefined }),
    useListStreamsQuery: () => ({ data: [], isLoading: false, error: undefined }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useListLayoutsQuery: () => ({
      data: { chains: [], published: [] },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useCreateLayoutDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined }],
    usePublishRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useArchiveRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useBranchDraftRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false }],
    useRevertRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useListOverlaysQuery: () => ({
      data: { chains: [], published: [] },
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useCreateOverlayDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined }],
    usePublishOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useArchiveOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
    useBranchDraftOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false }],
    useRevertOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false }],
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    useListVariablesQuery: () => ({
      data: [],
      isLoading: false,
      isFetching: false,
      error: undefined,
      refetch: vi.fn(),
    }),
    useGetVariableQuery: () => ({ data: undefined, isLoading: false }),
    useGetOverlaySnapshotQuery: () => ({ data: undefined, isLoading: false }),
    useDefineVariableMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, error: undefined }],
    useSetVariableValueMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false }],
  };
});

const { App } = await import('./App.js');
const { store } = await import('./app/store.js');

describe('App shell', () => {
  it('Renders the Cameras page heading and the Register button', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('heading', { name: /cameras/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /register camera/i })).toBeInTheDocument();
  });

  it('Has navigation to the Layouts page', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('button', { name: /^layouts$/i })).toBeInTheDocument();
  });
});
