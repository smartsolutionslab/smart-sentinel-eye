import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import { store } from '../../app/store.js';

/**
 * Review of #2781 (items 3 and 6) — the wall-level behaviour, with the real
 * `LayoutGrid` and `useWallCredential` and only the edges doubled: the tile
 * viewer, the OIDC user, the layout read and the renewer.
 *
 * <p>
 * The App-level counterpart (a renewal the provider refuses with
 * `invalid_grant` ends on `NotAuthorizedScreen`, with no wall notice) cannot
 * be rendered here — that screen belongs to `App`, above the router — and is
 * pinned in `App.test.tsx` as the renewer's classification only.
 * </p>
 */

const getLayoutMock = vi.fn();
const renewSessionMock = vi.hoisted(() => vi.fn<() => Promise<string | undefined>>());
const authToken = vi.hoisted(() => ({ current: 'old-token' }));

vi.mock('@smart-sentinel-eye/shared/api/gateway', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/gateway')>();
  return { ...actual, renewSession: () => renewSessionMock() };
});

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return { ...actual, useGetLayoutQuery: (...args: unknown[]) => getLayoutMock(...args) };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return { ...actual, useGetOverlayQuery: () => ({ data: undefined, isLoading: false }) };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return { ...actual, useGetOverlaySnapshotQuery: () => ({ data: undefined, isLoading: false }) };
});

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, currentData: undefined, isLoading: false, error: undefined }),
  };
});

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: authToken.current } }),
}));

vi.mock('@smart-sentinel-eye/shared/realtime/layoutHub', () => ({
  createLayoutHubClient: () => ({
    start: () => Promise.resolve(),
    stop: () => Promise.resolve(),
    state: () => 'Connected',
  }),
}));

interface ViewerProbe {
  mounts: number;
  props: {
    getToken: () => Promise<string | null>;
    onRefused?: (kind: 'unauthorized' | 'forbidden') => void;
    onLive?: () => void;
    onRefusalLifted?: () => void;
  };
}
const viewers = new Map<string, ViewerProbe>();

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', async () => {
  const { useEffect } = await import('react');
  return {
    CameraViewer: (props: { cameraIdentifier: string } & ViewerProbe['props']) => {
      const probe = viewers.get(props.cameraIdentifier) ?? { mounts: 0, props };
      probe.props = props;
      viewers.set(props.cameraIdentifier, probe);
      useEffect(() => {
        probe.mounts += 1;
      }, [probe]);
      return <div data-testid="camera-viewer">{props.cameraIdentifier}</div>;
    },
  };
});

const { LayoutGrid } = await import('./LayoutGrid.js');
const { recordRenewalAnswer } = await import('../../app/renewalAnswer.js');

const tile = (overrides: Partial<LayoutTile>): LayoutTile => ({
  cameraIdentifier: 'cam-a',
  overlayIdentifier: '',
  row: 0,
  col: 0,
  rowSpan: 1,
  colSpan: 1,
  ...overrides,
});

const twoTileLayout = (): Layout => ({
  layoutIdentifier: 'layout-1',
  version: 0,
  name: 'Line-1',
  fab: 'munich',
  createdAt: '2026-05-26T10:00:00Z',
  createdBy: '00000000-0000-0000-0000-000000000001',
  revisions: [
    {
      revisionIdentifier: 'r1',
      revisionNumber: 1,
      state: 'Published',
      gridRows: 1,
      gridCols: 2,
      tiles: [tile({ cameraIdentifier: 'cam-a', col: 0 }), tile({ cameraIdentifier: 'cam-b', col: 1 })],
      createdAt: '2026-05-26T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      publishedAt: '2026-05-26T10:00:00Z',
      archivedAt: null,
    },
  ],
});

const wall = () => (
  <Provider store={store}>
    <MemoryRouter>
      <LayoutGrid layoutIdentifier="layout-1" />
    </MemoryRouter>
  </Provider>
);

const viewer = (camera: string) => viewers.get(camera)!;

const refuseBoth = () => {
  act(() => {
    viewer('cam-a').props.onRefused?.('unauthorized');
    viewer('cam-b').props.onRefused?.('unauthorized');
  });
};

let resolveRenewal: (token: string | undefined) => void = () => undefined;
let pendingRenewal: Promise<string | undefined> = Promise.resolve(undefined);

const completeRenewal = async (token: string | undefined) => {
  await act(async () => {
    resolveRenewal(token);
    await pendingRenewal;
  });
};

describe('LayoutGrid wall credential state (review of #2781)', () => {
  beforeEach(() => {
    viewers.clear();
    authToken.current = 'old-token';
    getLayoutMock.mockReset();
    getLayoutMock.mockReturnValue({ data: twoTileLayout(), isLoading: false, error: undefined, refetch: vi.fn() });
    recordRenewalAnswer(false);
    renewSessionMock.mockReset();
    renewSessionMock.mockImplementation(() => {
      pendingRenewal = new Promise<string | undefined>((resolve) => {
        resolveRenewal = resolve;
      });
      return pendingRenewal;
    });
    vi.spyOn(console, 'info').mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('Asks the renewer once and shows the first notice when both tiles are refused 401', () => {
    render(wall());

    refuseBoth();

    expect(renewSessionMock).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('wall-credential-state')).toHaveTextContent(/reconnecting with a new credential/i);
  });

  it('Hands a remounted tile the renewed bearer, not the one that was just refused', async () => {
    const view = render(wall());
    refuseBoth();
    await expect(viewer('cam-a').props.getToken()).resolves.toBe('old-token');

    await completeRenewal('new-token');
    authToken.current = 'new-token';
    view.rerender(wall());

    expect(screen.queryByTestId('wall-credential-state')).not.toBeInTheDocument();
    expect(viewer('cam-a').mounts, 'the refused tile is remounted').toBe(2);
    await expect(viewer('cam-a').props.getToken()).resolves.toBe('new-token');
    await expect(viewer('cam-b').props.getToken()).resolves.toBe('new-token');
  });

  it('Keeps the first notice, with no re-enrol text, when the provider is unreachable', async () => {
    render(wall());
    refuseBoth();

    await completeRenewal(undefined);

    expect(screen.getByTestId('wall-credential-state')).toHaveTextContent(/reconnecting with a new credential/i);
    expect(screen.queryByText(/re-enrol/i)).not.toBeInTheDocument();
  });

  it('Shows the re-enrol notice only when the provider answered with a user holding no credential', async () => {
    render(wall());
    refuseBoth();
    recordRenewalAnswer(true);

    await completeRenewal(undefined);

    expect(screen.getByRole('alert')).toHaveTextContent(/re-enrol this display/i);
  });

  it('Stops counting a tile as refused once it is live, so a later partial refusal does not renew', () => {
    render(wall());
    act(() => viewer('cam-a').props.onRefused?.('unauthorized'));

    act(() => viewer('cam-a').props.onLive?.());
    act(() => viewer('cam-b').props.onRefused?.('unauthorized'));

    expect(renewSessionMock).not.toHaveBeenCalled();
  });
});
