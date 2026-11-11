import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle';
import { store } from '../../app/store';
import { layoutOf, tileAt } from './layoutGridGapFixtures';

/**
 * Spec 480: how `LayoutGrid` wires the live-update hub and the wall alignment
 * loop to its own state. Behaviour-preserving characterisation, green on the
 * unsplit grid.
 */

const h = vi.hoisted(() => ({
  layoutQuery: vi.fn(),
  overlayQuery: vi.fn(),
  snapshotQuery: vi.fn(),
  alignment: vi.fn(),
  lifecycle: undefined as UseLayoutLifecycleOptions | undefined,
  auth: { isAuthenticated: true, user: { access_token: 'token-1' } as { access_token: string } | undefined },
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
vi.mock('react-oidc-context', () => ({ useAuth: () => h.auth }));
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn() };
});
vi.mock('../revocation/useLayoutLifecycle', () => ({
  useLayoutLifecycle: (options: UseLayoutLifecycleOptions) => {
    h.lifecycle = options;
    return { degraded: false };
  },
}));
vi.mock('./useWallAlignment', () => {
  const released = new Set<string>();
  return {
    useWallAlignment: (...args: unknown[]) => {
      h.alignment(...args);
      return { reportLag: () => undefined, targetFor: () => null, frameAgeFor: () => null, released };
    },
  };
});
vi.mock('./CameraTile', () => ({ CameraTile: () => <div data-testid="camera-tile" /> }));

const { LayoutGrid } = await import('./LayoutGrid');

const refetch = vi.fn();

function page(onUnavailable?: () => void) {
  return (
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier="cam-1" onUnavailable={onUnavailable} />
      </MemoryRouter>
    </Provider>
  );
}

describe('LayoutGrid — live updates and alignment wiring', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.lifecycle = undefined;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.layoutQuery.mockReturnValue({
      data: layoutOf([tileAt(0, 0), tileAt(1, 1)], { rows: 2, cols: 2 }),
      isLoading: false,
      error: undefined,
      refetch,
    });
    h.overlayQuery.mockReturnValue({ data: undefined });
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
  });

  it('ignores an archive frame that names another layout, and acts on one that names this layout', () => {
    const onUnavailable = vi.fn();
    render(page(onUnavailable));

    act(() => {
      h.lifecycle?.onArchived?.({ layout: 'some-other-layout' } as never);
    });
    expect(onUnavailable).not.toHaveBeenCalled();

    act(() => {
      h.lifecycle?.onArchived?.({ layout: 'cam-1' } as never);
    });
    expect(onUnavailable).toHaveBeenCalledTimes(1);
  });

  it('refetches the layout after the hub reconnects, and only then', () => {
    render(page());
    expect(refetch).not.toHaveBeenCalled();

    act(() => {
      h.lifecycle?.onReconnected?.();
    });

    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('subscribes only once the user is authenticated, with the current access token', async () => {
    const view = render(page());
    expect(h.lifecycle?.enabled).toBe(true);
    await expect(Promise.resolve(h.lifecycle?.accessTokenFactory())).resolves.toBe('token-1');

    h.auth = { isAuthenticated: false, user: undefined };
    view.rerender(page());

    expect(h.lifecycle?.enabled).toBe(false);
    await expect(Promise.resolve(h.lifecycle?.accessTokenFactory())).resolves.toBe('');
  });

  it('feeds the alignment loop the populated tiles, not the grid cells', () => {
    render(page());

    expect(h.alignment).toHaveBeenCalled();
    for (const [tileCount] of h.alignment.mock.calls) {
      expect(tileCount).toBe(2);
    }
  });
});
