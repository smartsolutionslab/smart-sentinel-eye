import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle.js';
import { store } from '../../app/store.js';
import { layoutOf, overlayOf, textEl, tileAt } from './layoutGridGapFixtures.js';

/**
 * Spec 480: the `label_delay` report a held label makes when it settles names the
 * camera the tile shows NOW, and uses the page's stable `getToken`. Behaviour-
 * preserving characterisation, green on the unsplit and split grid alike.
 */

const h = vi.hoisted(() => ({
  layoutQuery: vi.fn(),
  overlayQuery: vi.fn(),
  snapshotQuery: vi.fn(),
  reportKioskLatency: vi.fn(),
  tileProps: [] as Array<Record<string, unknown>>,
  auth: { isAuthenticated: true, user: { access_token: 'token-1' } },
}));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return { ...actual, useGetLayoutQuery: (...args: unknown[]) => h.layoutQuery(...args) };
});
vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return { ...actual, useGetOverlayQuery: (...args: unknown[]) => h.overlayQuery(...args) };
});
vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return { ...actual, useGetOverlaySnapshotQuery: (...args: unknown[]) => h.snapshotQuery(...args) };
});
vi.mock('@smart-sentinel-eye/shared/observability/kioskLatency', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/observability/kioskLatency')>();
  return {
    ...actual,
    measureOverlayDraw: vi.fn(),
    reportKioskLatency: (...args: unknown[]) => h.reportKioskLatency(...args),
  };
});
vi.mock('react-oidc-context', () => ({ useAuth: () => h.auth }));
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn() };
});
vi.mock('../revocation/useLayoutLifecycle.js', () => ({
  useLayoutLifecycle: (_options: UseLayoutLifecycleOptions) => ({ degraded: false }),
}));
vi.mock('./useWallAlignment.js', () => {
  const released = new Set<string>();
  return {
    useWallAlignment: () => ({
      reportLag: () => undefined,
      targetFor: () => null,
      frameAgeFor: () => 100,
      released,
    }),
  };
});
vi.mock('./CameraTile.js', () => ({
  CameraTile: (props: Record<string, unknown>) => {
    h.tileProps.push(props);
    return <div data-testid="camera-tile" />;
  },
}));

const { LayoutGrid } = await import('./LayoutGrid.js');

function page() {
  return (
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier="cam-1" />
      </MemoryRouter>
    </Provider>
  );
}

function serve(camera: string, text: string) {
  h.layoutQuery.mockReturnValue({
    data: layoutOf([tileAt(0, 0, { cameraIdentifier: camera })]),
    isLoading: false,
    error: undefined,
    refetch: vi.fn(),
  });
  h.overlayQuery.mockReturnValue(overlayOf([textEl({ text })]));
}

describe('LayoutGrid Tile — the label_delay report of a held label', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    h.tileProps.length = 0;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('names the camera the tile shows now when the camera changed in place during the hold', () => {
    serve('cam-A', 'first');
    const view = render(page());

    serve('cam-B', 'second');
    view.rerender(page());
    expect(h.reportKioskLatency).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(100);
    });

    expect(h.reportKioskLatency).toHaveBeenCalledTimes(1);
    expect(h.reportKioskLatency).toHaveBeenCalledWith('label_delay', 'cam-B', expect.any(Number), expect.any(Function));
  });

  it("reports with the page's stable getToken, which a token renew does not replace", () => {
    serve('cam-A', 'first');
    const view = render(page());
    const getToken = h.tileProps.at(-1)?.getToken;

    h.auth = { isAuthenticated: true, user: { access_token: 'token-2' } };
    serve('cam-A', 'second');
    view.rerender(page());
    act(() => {
      vi.advanceTimersByTime(100);
    });

    expect(h.reportKioskLatency).toHaveBeenCalledTimes(1);
    expect(h.reportKioskLatency.mock.calls[0]?.[3]).toBe(getToken);
  });
});
