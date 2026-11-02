import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle.js';
import { store } from '../../app/store.js';
import { layoutOf, tileAt } from './layoutGridGapFixtures.js';

/**
 * Spec 481 T003. What the grid's `getToken` resolves when the session holds no
 * token. The existing identity test pins the identity and the renewed value;
 * nothing asked what a signed-out session resolves, so `null` could become `''`
 * or `undefined` unnoticed. A tile hands that value to its WHEP session as the
 * bearer, where `''` and `null` are different requests. Green on today's code.
 */

const h = vi.hoisted(() => ({
  tileProps: [] as Array<Record<string, unknown>>,
  auth: { isAuthenticated: false, user: undefined as undefined | { access_token?: string } },
}));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useGetLayoutQuery: () => ({
      data: layoutOf([tileAt(0, 0, { overlayIdentifier: null })], { fab: 'munich' }),
      isLoading: false,
      error: undefined,
      refetch: vi.fn(),
    }),
  };
});
vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return { ...actual, useGetOverlayQuery: () => ({ data: undefined, isLoading: false }) };
});
vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return { ...actual, useGetOverlaySnapshotQuery: () => ({ data: undefined, refetch: vi.fn() }) };
});
vi.mock('react-oidc-context', () => ({ useAuth: () => h.auth }));
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn() };
});
vi.mock('../revocation/useLayoutLifecycle.js', () => ({
  useLayoutLifecycle: (_options: UseLayoutLifecycleOptions) => ({ degraded: false }),
}));
vi.mock('./useWallAlignment.js', () => ({
  useWallAlignment: () => ({
    reportLag: () => undefined,
    targetFor: () => null,
    frameAgeFor: () => null,
    released: new Set<string>(),
  }),
}));
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

describe('LayoutGrid — getToken when the session holds no token', () => {
  beforeEach(() => {
    h.tileProps.length = 0;
    h.auth = { isAuthenticated: false, user: undefined };
  });

  it('resolves null, as a promise, not an empty string or undefined', async () => {
    render(page());

    const getToken = h.tileProps.at(-1)?.getToken as () => Promise<string | null>;
    const result = getToken();

    expect(result).toBeInstanceOf(Promise);
    await expect(result).resolves.toBeNull();
  });

  it('resolves null for a user who holds no access token either', async () => {
    h.auth = { isAuthenticated: false, user: {} };

    render(page());

    const getToken = h.tileProps.at(-1)?.getToken as () => Promise<string | null>;
    await expect(getToken()).resolves.toBeNull();
  });
});
