import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle.js';
import { store } from '../../app/store.js';
import { layoutOf, tileAt } from './layoutGridGapFixtures.js';

/**
 * Spec 480: what `LayoutGrid` does when its layout becomes unavailable (archived
 * by a pushed frame, or loaded with no Published revision), and how the layout
 * query is addressed. Behaviour-preserving characterisation, green on the
 * unsplit grid.
 */

const h = vi.hoisted(() => ({
  layoutQuery: vi.fn(),
  overlayQuery: vi.fn(),
  snapshotQuery: vi.fn(),
  navigate: vi.fn(),
  lifecycle: undefined as UseLayoutLifecycleOptions | undefined,
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
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: 'token-1' } }),
}));
vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock('../revocation/useLayoutLifecycle.js', () => ({
  useLayoutLifecycle: (options: UseLayoutLifecycleOptions) => {
    h.lifecycle = options;
    return { degraded: false };
  },
}));
vi.mock('./CameraTile.js', () => ({ CameraTile: () => <div data-testid="camera-tile" /> }));

const { LayoutGrid } = await import('./LayoutGrid.js');

const refetch = vi.fn();

function page(props: { layoutIdentifier?: string; onUnavailable?: () => void } = {}) {
  return (
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier={props.layoutIdentifier ?? 'cam-1'} onUnavailable={props.onUnavailable} />
      </MemoryRouter>
    </Provider>
  );
}

function serve(result: { data?: unknown; isLoading?: boolean; error?: unknown }) {
  h.layoutQuery.mockReturnValue({ data: undefined, isLoading: false, error: undefined, refetch, ...result });
}

const published = () => layoutOf([tileAt(0, 0)]);
const archived = () => layoutOf([tileAt(0, 0)], { state: 'Archived' });

describe('LayoutGrid — leaving an unavailable layout', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.lifecycle = undefined;
    h.overlayQuery.mockReturnValue({ data: undefined });
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
  });

  it('replaces the history entry with the picker when a loaded layout has no Published revision', () => {
    serve({ data: archived() });
    render(page());

    expect(h.navigate).toHaveBeenCalledTimes(1);
    expect(h.navigate).toHaveBeenCalledWith('/', { replace: true });
  });

  it('calls onUnavailable instead of navigating when the caller supplies one', () => {
    const onUnavailable = vi.fn();
    serve({ data: archived() });
    render(page({ onUnavailable }));

    expect(onUnavailable).toHaveBeenCalledTimes(1);
    expect(h.navigate).not.toHaveBeenCalled();
  });

  it('does the same when a pushed archive frame names this layout', () => {
    const onUnavailable = vi.fn();
    serve({ data: published() });
    render(page({ onUnavailable }));
    expect(onUnavailable).not.toHaveBeenCalled();

    act(() => {
      h.lifecycle?.onArchived?.({ layout: 'cam-1' } as never);
    });

    expect(onUnavailable).toHaveBeenCalledTimes(1);
    expect(h.navigate).not.toHaveBeenCalled();
  });

  it('replaces the history entry with the picker when a pushed archive frame names this layout and no callback is given', () => {
    serve({ data: published() });
    render(page());

    act(() => {
      h.lifecycle?.onArchived?.({ layout: 'cam-1' } as never);
    });

    expect(h.navigate).toHaveBeenCalledTimes(1);
    expect(h.navigate).toHaveBeenCalledWith('/', { replace: true });
  });

  it('does not leave while a Published revision exists', () => {
    const onUnavailable = vi.fn();
    serve({ data: published() });
    const view = render(page({ onUnavailable }));
    view.rerender(page({ onUnavailable }));

    expect(onUnavailable).not.toHaveBeenCalled();
    expect(h.navigate).not.toHaveBeenCalled();
  });

  it('does not leave before the layout has arrived', () => {
    const onUnavailable = vi.fn();
    serve({ isLoading: true });
    render(page({ onUnavailable }));

    expect(onUnavailable).not.toHaveBeenCalled();
  });

  it('does not leave a layout it has data for when the query is in error', () => {
    const onUnavailable = vi.fn();
    serve({ data: archived(), error: { status: 500 } });
    render(page({ onUnavailable }));

    expect(onUnavailable).not.toHaveBeenCalled();
  });

  it('does not leave a second time when a refetch of an archived layout then fails', () => {
    const onUnavailable = vi.fn();
    serve({ data: archived() });
    const view = render(page({ onUnavailable }));
    expect(onUnavailable).toHaveBeenCalledTimes(1);

    serve({ data: archived(), error: { status: 500 } });
    view.rerender(page({ onUnavailable }));

    expect(onUnavailable).toHaveBeenCalledTimes(1);
  });

  it('tells a replaced callback about a layout that is already unavailable', () => {
    const first = vi.fn();
    const second = vi.fn();
    serve({ data: archived() });
    const view = render(page({ onUnavailable: first }));
    expect(first).toHaveBeenCalledTimes(1);

    view.rerender(page({ onUnavailable: second }));

    expect(second).toHaveBeenCalledTimes(1);
  });

  it('routes a later archive frame to the callback it holds now, not the one it mounted with', () => {
    const first = vi.fn();
    const second = vi.fn();
    serve({ data: published() });
    const view = render(page({ onUnavailable: first }));
    view.rerender(page({ onUnavailable: second }));

    act(() => {
      h.lifecycle?.onArchived?.({ layout: 'cam-1' } as never);
    });

    expect(second).toHaveBeenCalledTimes(1);
    expect(first).not.toHaveBeenCalled();
  });
});

describe('LayoutGrid — addressing the layout query', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.overlayQuery.mockReturnValue({ data: undefined });
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
  });

  it('asks for the layout it was given', () => {
    serve({ data: published() });
    render(page({ layoutIdentifier: 'cam-1' }));

    expect(h.layoutQuery).toHaveBeenCalledWith('cam-1', { skip: false });
  });

  it('does not ask for anything when it has no layout identifier', () => {
    serve({});
    render(page({ layoutIdentifier: '' }));

    expect(h.layoutQuery).toHaveBeenCalledWith('', { skip: true });
    expect(h.layoutQuery).not.toHaveBeenCalledWith('', { skip: false });
  });
});
