import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

/** What `useAuth()` returns, set per test; `undefined` is "no AuthProvider mounted". */
const auth = vi.hoisted(() => ({
  current: undefined as undefined | { user?: { access_token?: string; scope?: string } },
}));

/** The props the dialog most recently handed `TileView`. */
const handed = vi.hoisted(() => ({
  current: undefined as undefined | { getToken: () => Promise<string | null>; canEditOverlays: boolean },
}));

vi.mock('react-oidc-context', () => ({ useAuth: () => auth.current }));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false, reset: vi.fn() }],
    useGetLayoutQuery: () => ({
      data: undefined,
      currentData: undefined,
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: () => ({
      data: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return { ...actual, useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false }) };
});

// The view itself is covered elsewhere; here only what the dialog hands it.
vi.mock('./tileView/TileView.js', () => ({
  TileView: (props: { getToken: () => Promise<string | null>; canEditOverlays: boolean }) => {
    handed.current = { getToken: props.getToken, canEditOverlays: props.canEditOverlays };
    return null;
  },
}));

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

function ui() {
  return (
    <Provider store={store}>
      <LayoutEditorDialog open onOpenChange={() => {}} />
    </Provider>
  );
}

/**
 * Spec 476 T004. The dialog hands `TileView` a token callback that must read the **latest** access token, not the one
 * from the render that created it (the one place the dialog sets a ref during render, behind the `react-hooks/refs`
 * suppression judged in #2118), and whether the session may edit overlays. The existing tile-view tests stub `TileView`
 * and ignore both props. Green on the unsplit dialog by design; it must stay green after.
 */
describe('LayoutEditorDialog — what it hands the tile view', () => {
  beforeEach(() => {
    handed.current = undefined;
    auth.current = undefined;
  });

  it('Resolves the latest access token, not the one from the render that created the callback', async () => {
    auth.current = { user: { access_token: 'token-1' } };
    const { rerender } = render(ui());
    const getToken = handed.current!.getToken;
    expect(await getToken()).toBe('token-1');

    auth.current = { user: { access_token: 'token-2' } };
    rerender(ui());

    expect(await getToken()).toBe('token-2');
  });

  it('Resolves no token when there is no auth provider', async () => {
    auth.current = undefined;

    render(ui());

    expect(await handed.current!.getToken()).toBeNull();
  });

  it('Says the session may edit overlays only when its scope grants it', () => {
    auth.current = { user: { access_token: 't', scope: 'openid sse.overlays.write' } };
    const { rerender } = render(ui());
    expect(handed.current!.canEditOverlays).toBe(true);

    auth.current = { user: { access_token: 't', scope: 'openid sse.overlays.read' } };
    rerender(ui());

    expect(handed.current!.canEditOverlays).toBe(false);
  });
});
