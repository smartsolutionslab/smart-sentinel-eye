import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle.js';
import { store } from '../../app/store.js';
import { layoutOf, tileAt } from './layoutGridGapFixtures.js';

/**
 * Spec 480: the page JSX of `LayoutGrid` and what it hands each tile — the
 * loading and fallback screens, the Back rule, the grid template, the per-tile
 * props, and the tile's own attributes. Behaviour-preserving characterisation,
 * green on the unsplit grid.
 */

const h = vi.hoisted(() => ({
  layoutQuery: vi.fn(),
  overlayQuery: vi.fn(),
  snapshotQuery: vi.fn(),
  navigate: vi.fn(),
  credential: vi.fn(),
  reportLag: vi.fn(),
  reportRefusal: vi.fn(),
  reportLive: vi.fn(),
  clearRefusal: vi.fn(),
  targets: {} as Record<string, number | null>,
  released: new Set<string>(),
  tileProps: new Map<string, Record<string, unknown>>(),
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
vi.mock('./useWallAlignment.js', () => ({
  useWallAlignment: () => ({
    reportLag: (...args: unknown[]) => h.reportLag(...args),
    targetFor: (key: string) => h.targets[key] ?? null,
    frameAgeFor: () => null,
    released: h.released,
  }),
}));
vi.mock('./useWallCredential.js', () => ({
  useWallCredential: (...args: unknown[]) => {
    h.credential(...args);
    return {
      state: 'idle',
      credentialGeneration: 0,
      reportRefusal: (...inner: unknown[]) => h.reportRefusal(...inner),
      reportLive: (...inner: unknown[]) => h.reportLive(...inner),
      clearRefusal: (...inner: unknown[]) => h.clearRefusal(...inner),
    };
  },
}));
vi.mock('./CameraTile.js', () => ({
  CameraTile: (props: Record<string, unknown>) => {
    h.tileProps.set(props.cameraIdentifier as string, props);
    return <div data-testid="camera-tile" />;
  },
}));

const { LayoutGrid } = await import('./LayoutGrid.js');

const refetch = vi.fn();

function page(props: { headerTitle?: string } = {}) {
  return (
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier="cam-1" onUnavailable={() => undefined} headerTitle={props.headerTitle} />
      </MemoryRouter>
    </Provider>
  );
}

function serve(result: { data?: unknown; isLoading?: boolean }) {
  h.layoutQuery.mockReturnValue({ data: undefined, isLoading: false, error: undefined, refetch, ...result });
}

function tilePropsOf(camera: string): Record<string, unknown> {
  const props = h.tileProps.get(camera);
  if (props === undefined) throw new Error(`no tile rendered for ${camera}`);
  return props;
}

describe('LayoutGrid — the page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.targets = {};
    h.released = new Set();
    h.tileProps.clear();
    h.lifecycle = undefined;
    h.overlayQuery.mockReturnValue({ data: undefined });
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
  });

  describe('loading and fallback screens', () => {
    it('says it is loading while the layout has not arrived, keeping the wall header', () => {
      serve({ isLoading: true });
      render(page({ headerTitle: 'Wall A' }));

      expect(screen.getByText('Loading camera…')).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Wall A' })).toBeInTheDocument();
    });

    it('offers a way back to the picker on the unavailable screen of a route page', () => {
      serve({ data: layoutOf([tileAt(0, 0)], { state: 'Archived' }) });
      render(page());

      expect(screen.getByText('Layout is no longer available.')).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Back to picker' }));
      expect(h.navigate).toHaveBeenCalledWith('/');
    });

    it('offers no way back on the unavailable screen of a wall, which keeps its own header', () => {
      serve({ data: layoutOf([tileAt(0, 0)], { state: 'Archived' }) });
      render(page({ headerTitle: 'Wall A' }));

      expect(screen.getByText('Layout is no longer available.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Back to picker' })).not.toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'Wall A' })).toBeInTheDocument();
    });
  });

  describe('header and grid', () => {
    it('titles a route page with the layout name and offers Back, which returns to the picker', () => {
      serve({ data: layoutOf([tileAt(0, 0)], { name: 'Line-7' }) });
      render(page());

      expect(screen.getByRole('heading', { name: 'Line-7' })).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Back' }));
      expect(h.navigate).toHaveBeenCalledWith('/');
    });

    it('titles a wall with its own name and offers no Back', () => {
      serve({ data: layoutOf([tileAt(0, 0)], { name: 'Line-7' }) });
      render(page({ headerTitle: 'Wall A' }));

      expect(screen.getByRole('heading', { name: 'Wall A' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    });

    it('lays the grid out with the published revision columns and rows', () => {
      serve({ data: layoutOf([tileAt(0, 0)], { rows: 2, cols: 3 }) });
      render(page());

      const grid = screen.getByTestId('layout-grid');
      expect(grid.style.gridTemplateColumns).toBe('repeat(3, minmax(0, 1fr))');
      expect(grid.style.gridTemplateRows).toBe('repeat(2, minmax(0, 1fr))');
    });
  });

  describe('what each tile is handed', () => {
    it('hands each tile its own playout target', () => {
      serve({ data: layoutOf([tileAt(0, 0), tileAt(0, 1)], { cols: 2 }) });
      h.targets = { '0:0': 120, '0:1': null };
      render(page());

      expect(tilePropsOf('cam-0-0').playoutTargetMilliseconds).toBe(120);
      expect(tilePropsOf('cam-0-1').playoutTargetMilliseconds).toBeNull();
    });

    it('marks only the released tile as out of alignment, and reports lag under that tile key', () => {
      serve({ data: layoutOf([tileAt(0, 0), tileAt(0, 1)], { cols: 2 }) });
      h.released = new Set(['0:1']);
      render(page());

      const badges = screen.getAllByTestId('tile-out-of-alignment');
      expect(badges).toHaveLength(1);
      expect(badges[0]).toHaveAttribute('data-camera', 'cam-0-1');
      const released = screen.getAllByTestId('layout-tile')[1]!;
      expect(within(released).getByTestId('tile-out-of-alignment')).toBeInTheDocument();

      (tilePropsOf('cam-0-1').onLagMeasured as (camera: string, lag: number, buffer: number) => void)('cam-0-1', 5, 6);
      expect(h.reportLag).toHaveBeenCalledWith('0:1', 'cam-0-1', 5, 6);
    });

    it('lets the wall credential see the keys of the populated tiles only', () => {
      serve({ data: layoutOf([tileAt(0, 0), tileAt(1, 1)], { rows: 2, cols: 2 }) });
      render(page());

      expect(h.credential).toHaveBeenCalledWith(['0:0', '1:1'], 'token-1');
    });

    it('reports a tile refusal, going live, and a lifted refusal under that tile key', () => {
      serve({ data: layoutOf([tileAt(0, 0), tileAt(1, 1)], { rows: 2, cols: 2 }) });
      render(page());

      const props = tilePropsOf('cam-1-1');
      (props.onRefused as (kind: 'forbidden') => void)('forbidden');
      (props.onLive as () => void)();
      (props.onRefusalLifted as () => void)();

      expect(h.reportRefusal).toHaveBeenCalledWith('1:1', 'forbidden');
      expect(h.reportLive).toHaveBeenCalledWith('1:1');
      expect(h.clearRefusal).toHaveBeenCalledWith('1:1');
    });
  });

  describe('the tile element', () => {
    it('names its camera on the element', () => {
      serve({ data: layoutOf([tileAt(0, 0), tileAt(0, 1)], { cols: 2 }) });
      render(page());

      const tiles = screen.getAllByTestId('layout-tile');
      expect(tiles.map((tile) => tile.getAttribute('data-camera-identifier'))).toEqual(['cam-0-0', 'cam-0-1']);
    });

    it('carries the highlight class only while its overlay is highlighted', () => {
      serve({ data: layoutOf([tileAt(0, 0), tileAt(0, 1, { overlayIdentifier: 'ovl-y' })], { cols: 2 }) });
      render(page());
      const [lit, other] = screen.getAllByTestId('layout-tile') as [HTMLElement, HTMLElement];
      expect(lit).toHaveAttribute('data-highlighted', 'false');
      expect(lit).not.toHaveClass('ssE-overlay-highlight');

      act(() => {
        h.lifecycle?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 60_000 });
      });

      expect(lit).toHaveAttribute('data-highlighted', 'true');
      expect(lit).toHaveClass('ssE-overlay-highlight');
      expect(other).toHaveAttribute('data-highlighted', 'false');
      expect(other).not.toHaveClass('ssE-overlay-highlight');
    });
  });
});
