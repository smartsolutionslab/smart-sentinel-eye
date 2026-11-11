import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { store } from '../../app/store';

/**
 * Spec 481 T003. The identity of the `getToken` the dialog hands `TileView`.
 * `LayoutEditorDialogToken.test` pins that the callback yields the newest token
 * and `null` without a provider, but it holds on to the first callback and calls it:
 * a getter rebuilt on every render (or keyed on the token value) passes it. Its
 * identity is what keeps the tile view's WHEP session from reconnecting on every
 * render or silent renew (issue 1889). Green on today's code.
 */

const auth = vi.hoisted(() => ({
  current: undefined as undefined | { user?: { access_token?: string } },
}));

const renders = vi.hoisted(() => [] as Array<{ getToken: () => Promise<string | null> }>);

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

vi.mock('./tileView/TileView', () => ({
  TileView: (props: { getToken: () => Promise<string | null> }) => {
    renders.push({ getToken: props.getToken });
    return null;
  },
}));

const { LayoutEditorDialog } = await import('./LayoutEditorDialog');

function ui() {
  return (
    <Provider store={store}>
      <LayoutEditorDialog open onOpenChange={() => {}} />
    </Provider>
  );
}

describe('LayoutEditorDialog — the tile view keeps one getToken for its whole life', () => {
  beforeEach(() => {
    renders.length = 0;
    auth.current = { user: { access_token: 'token-1' } };
  });

  it('hands the same function after a plain re-render and after a silent token renewal', async () => {
    const { rerender } = render(ui());
    const first = renders.at(0)!.getToken;

    rerender(ui());
    expect(renders.length).toBeGreaterThan(1);
    expect(renders.at(-1)!.getToken).toBe(first);

    auth.current = { user: { access_token: 'token-2' } };
    rerender(ui());

    expect(renders.at(-1)!.getToken).toBe(first);
    await expect(first()).resolves.toBe('token-2');
  });

  it('returns a promise', () => {
    render(ui());

    expect(renders.at(0)!.getToken()).toBeInstanceOf(Promise);
  });
});
