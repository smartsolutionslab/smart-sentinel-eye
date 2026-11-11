import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle';
import { store } from '../../app/store';
import { boxEl, layoutOf, overlayOf, textEl, tileAt, type ElementFixture } from './layoutGridGapFixtures';

/**
 * Spec 480 (SC-006): the identity and count figures of the wall's render path,
 * pinned before the grid is split. Behaviour-preserving, so every test here is
 * green on the unsplit file and must stay green, unmodified, after each hook
 * moves.
 *
 * <p>
 * What is pinned: the draw measurement fires ONCE per distinct painted set and
 * once more per change of `highlighted` (never per render); `getToken` keeps
 * its identity across a token renew; each layout fault is logged once per
 * mounted page; an equal re-render hands the tile the same `overlays` array.
 * </p>
 */

const h = vi.hoisted(() => ({
  layoutQuery: vi.fn(),
  overlayQuery: vi.fn(),
  snapshotQuery: vi.fn(),
  measureOverlayDraw: vi.fn(),
  reportKioskLatency: vi.fn(),
  logResilienceEvent: vi.fn(),
  tileProps: [] as Array<Record<string, unknown>>,
  lifecycle: undefined as UseLayoutLifecycleOptions | undefined,
  auth: { isAuthenticated: true, user: { access_token: 'token-1' } },
  frameAge: null as number | null,
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
    measureOverlayDraw: (...args: unknown[]) => h.measureOverlayDraw(...args),
    reportKioskLatency: (...args: unknown[]) => h.reportKioskLatency(...args),
  };
});
vi.mock('@smart-sentinel-eye/shared/observability/resilienceLog', () => ({
  logResilienceEvent: (...args: unknown[]) => h.logResilienceEvent(...args),
}));
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
    useWallAlignment: () => ({
      reportLag: () => undefined,
      targetFor: () => null,
      frameAgeFor: () => h.frameAge,
      released,
    }),
  };
});
vi.mock('./CameraTile', () => ({
  CameraTile: (props: Record<string, unknown>) => {
    h.tileProps.push(props);
    return <div data-testid="camera-tile" />;
  },
}));

const { LayoutGrid } = await import('./LayoutGrid');

const refetch = vi.fn();

function page() {
  return (
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier="cam-1" />
      </MemoryRouter>
    </Provider>
  );
}

/** Every call builds a fresh, equal object graph — the way a re-render of a real query result is only equal, not identical. */
function serveLayout(overrides: { fab?: string; overlayIdentifier?: string | null } = {}) {
  h.layoutQuery.mockImplementation(() => ({
    data: layoutOf([tileAt(0, 0, { overlayIdentifier: overrides.overlayIdentifier ?? 'ovl-x' })], {
      fab: overrides.fab ?? 'munich',
    }),
    isLoading: false,
    error: undefined,
    refetch,
  }));
}

function serveElements(elements: () => ElementFixture[]) {
  h.overlayQuery.mockImplementation(() => overlayOf(elements()));
}

function lastOverlays(): unknown {
  return h.tileProps.at(-1)?.overlays;
}

function pushHighlight() {
  act(() => {
    h.lifecycle?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 60_000 });
  });
}

describe('LayoutGrid — the draw measurement fires once per distinct painted set', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
    serveLayout();
  });

  it('measures once on mount and not again on a re-render with an equal element set', () => {
    serveElements(() => [boxEl(), textEl()]);
    const view = render(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);
    expect(h.measureOverlayDraw).toHaveBeenCalledWith('cam-0-0', expect.any(Function));

    view.rerender(page());
    view.rerender(page());

    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);
  });

  const changes: Array<[string, (set: ElementFixture[]) => ElementFixture[]]> = [
    ['kind', ([box, text]) => [{ ...box!, kind: 'Ellipse' }, text!]],
    ['colour', ([box, text]) => [{ ...box!, color: '#00FF00FF' }, text!]],
    ['x', ([box, text]) => [{ ...box!, normalizedX: 0.6 }, text!]],
    ['y', ([box, text]) => [{ ...box!, normalizedY: 0.6 }, text!]],
    ['width', ([box, text]) => [{ ...box!, normalizedWidth: 0.25 }, text!]],
    ['height', ([box, text]) => [{ ...box!, normalizedHeight: 0.15 }, text!]],
    ['font size', ([box, text]) => [box!, { ...text!, fontSizePx: 20 }]],
    ['text', ([box, text]) => [box!, { ...text!, text: 'Another label' }]],
  ];

  it.each(changes)('measures once more when only the %s of one element changes', (_field, change) => {
    let current = [boxEl(), textEl()];
    serveElements(() => current.map((element) => ({ ...element })));
    const view = render(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    current = change(current);
    view.rerender(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(2);

    view.rerender(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(2);
  });

  it('measures once more when `highlighted` changes, and not for an unrelated re-render', () => {
    serveElements(() => [boxEl(), textEl()]);
    const view = render(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    pushHighlight();
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(2);

    view.rerender(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(2);
  });

  it('does not measure while the overlay has not resolved a Published revision', () => {
    h.overlayQuery.mockReturnValue({ data: undefined });
    const view = render(page());
    view.rerender(page());

    expect(h.measureOverlayDraw).not.toHaveBeenCalled();
  });

  it('does not measure when the bound overlay becomes unavailable (nothing is painted)', () => {
    serveElements(() => [boxEl(), textEl()]);
    render(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    act(() => {
      h.lifecycle?.onOverlayArchived?.({ overlay: 'ovl-x', fab: 'munich' } as never);
    });

    expect(lastOverlays()).toBeUndefined();
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);
  });
});

describe('LayoutGrid — a token renew does not rebuild the tiles effects', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
    serveLayout();
    serveElements(() => [boxEl(), textEl()]);
  });

  it("keeps getToken's identity and does not re-fire the measurement, yet getToken yields the new token", async () => {
    const view = render(page());
    const before = h.tileProps.at(-1)?.getToken;
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    h.auth = { isAuthenticated: true, user: { access_token: 'token-2' } };
    view.rerender(page());

    const after = h.tileProps.at(-1)?.getToken as () => Promise<string | null>;
    expect(after).toBe(before);
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);
    await expect(after()).resolves.toBe('token-2');
  });
});

describe('LayoutGrid — each layout fault is logged once per mounted page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
    h.overlayQuery.mockReturnValue({ data: undefined });
  });

  function faultLines() {
    return h.logResilienceEvent.mock.calls.filter(
      ([, event]) => event === 'tile-without-overlay-identifier' || event === 'layout-without-fab',
    );
  }

  it('logs nothing while the layout is still loading', () => {
    h.layoutQuery.mockReturnValue({ data: undefined, isLoading: true, error: undefined, refetch });
    const view = render(page());
    view.rerender(page());

    expect(faultLines()).toEqual([]);
  });

  it('logs the tile fault and the fab fault once each across re-renders and a refetch', () => {
    h.layoutQuery.mockReturnValue({ data: undefined, isLoading: true, error: undefined, refetch });
    const view = render(page());
    expect(faultLines()).toEqual([]);

    serveLayout({ fab: '', overlayIdentifier: '' });
    view.rerender(page());
    expect(faultLines()).toEqual([
      ['hub', 'tile-without-overlay-identifier', { layout: 'cam-1', tiles: 1 }],
      ['hub', 'layout-without-fab', { layout: 'cam-1' }],
    ]);

    // two further renders, then a refetch that hands back an equal-but-new layout
    view.rerender(page());
    view.rerender(page());
    serveLayout({ fab: '', overlayIdentifier: '' });
    view.rerender(page());

    expect(faultLines()).toHaveLength(2);
  });

  it('reports the fab fault when only the fab changes under an unchanged published revision', () => {
    // RTK Query's structural sharing keeps untouched revisions referentially
    // equal, so a refetch that changes only `fab` leaves `published` as it was.
    const layout = layoutOf([tileAt(0, 0)], { fab: 'munich' });
    h.layoutQuery.mockReturnValue({ data: layout, isLoading: false, error: undefined, refetch });
    const view = render(page());
    expect(faultLines()).toEqual([]);

    h.layoutQuery.mockReturnValue({
      data: { ...layout, fab: '' },
      isLoading: false,
      error: undefined,
      refetch,
    });
    view.rerender(page());

    expect(faultLines()).toEqual([['hub', 'layout-without-fab', { layout: 'cam-1' }]]);
  });
});

describe('LayoutGrid — the held element reference reaches the tile', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
    serveLayout();
  });

  it('hands CameraTile the same overlays array after an equal re-render, and a new one after a change', () => {
    let current = [boxEl(), textEl()];
    serveElements(() => current.map((element) => ({ ...element })));
    const view = render(page());
    const first = lastOverlays();
    expect(first).toHaveLength(2);

    view.rerender(page());
    expect(lastOverlays()).toBe(first);

    current = [boxEl(), textEl({ text: 'Another label' })];
    view.rerender(page());
    expect(lastOverlays()).not.toBe(first);
  });
});

describe('LayoutGrid — the measurement follows what is painted, not what arrived, while a label is held', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    h.auth = { isAuthenticated: true, user: { access_token: 'token-1' } };
    h.frameAge = 100;
    h.snapshotQuery.mockReturnValue({ data: undefined, refetch: vi.fn() });
    serveLayout();
  });

  afterEach(() => {
    vi.useRealTimers();
    h.frameAge = null;
  });

  function driveClock(milliseconds: number) {
    act(() => {
      vi.advanceTimersByTime(milliseconds);
    });
  }

  it('measures a held change when it is shown, not when it arrives', () => {
    let current = [textEl({ text: 'A' })];
    serveElements(() => current.map((element) => ({ ...element })));
    const view = render(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    current = [textEl({ text: 'B' })];
    view.rerender(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    driveClock(100);
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(2);
  });

  it('does not re-measure when the held set flaps back to what is already painted', () => {
    let current = [textEl({ text: 'A' })];
    serveElements(() => current.map((element) => ({ ...element })));
    const view = render(page());
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);

    current = [textEl({ text: 'B' })];
    view.rerender(page());
    current = [textEl({ text: 'A' })];
    view.rerender(page());
    driveClock(100);

    // the picture still carries 'A', rebuilt as a new array: nothing changed on screen
    expect(h.measureOverlayDraw).toHaveBeenCalledTimes(1);
  });
});
