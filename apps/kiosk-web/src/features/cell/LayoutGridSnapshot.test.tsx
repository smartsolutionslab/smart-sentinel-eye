import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import { DEFAULT_OVERLAY_COLOR } from '@smart-sentinel-eye/shared/api/overlays.schema';
import type { UseLayoutLifecycleOptions } from '../revocation/useLayoutLifecycle';
import type { UseTemplateMissRefetchOptions } from './useTemplateMissRefetch';
import { store } from '../../app/store';
import { boxEl, layoutOf, overlayOf, textEl, tileAt } from './layoutGridGapFixtures';

/**
 * Spec 480: how one tile turns its overlay and resolved-text snapshot into the
 * painted set — colour default, shape mapping, the template-miss refetch's
 * inputs, the placeholder verdict reported to the page, and the unavailable
 * bypass. Behaviour-preserving characterisation, green on the unsplit grid.
 */

const h = vi.hoisted(() => ({
  layoutQuery: vi.fn(),
  overlayQuery: vi.fn(),
  snapshotQuery: vi.fn(),
  missRefetch: vi.fn(),
  logResilienceEvent: vi.fn(),
  tileProps: [] as Array<Record<string, unknown>>,
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
vi.mock('@smart-sentinel-eye/shared/observability/kioskLatency', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/observability/kioskLatency')>();
  return { ...actual, measureOverlayDraw: vi.fn(), reportKioskLatency: vi.fn() };
});
vi.mock('@smart-sentinel-eye/shared/observability/resilienceLog', () => ({
  logResilienceEvent: (...args: unknown[]) => h.logResilienceEvent(...args),
}));
vi.mock('react-oidc-context', () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: 'token-1' } }),
}));
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
vi.mock('./useTemplateMissRefetch', () => ({
  useTemplateMissRefetch: (options: UseTemplateMissRefetchOptions) => h.missRefetch(options),
}));
vi.mock('./CameraTile', () => ({
  CameraTile: (props: Record<string, unknown>) => {
    h.tileProps.push(props);
    return <div data-testid="camera-tile" />;
  },
}));

const { LayoutGrid } = await import('./LayoutGrid');

function page() {
  return (
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier="cam-1" />
      </MemoryRouter>
    </Provider>
  );
}

function serveLayout(fab = 'munich') {
  h.layoutQuery.mockReturnValue({
    data: layoutOf([tileAt(0, 0)], { fab }),
    isLoading: false,
    error: undefined,
    refetch: vi.fn(),
  });
}

function serveSnapshot(texts: Array<{ template: string; resolved: string }> | undefined) {
  h.snapshotQuery.mockReturnValue({
    data: texts === undefined ? undefined : { overlayIdentifier: 'ovl-x', texts, version: 1 },
    refetch: vi.fn(),
  });
}

function lastOverlays(): unknown {
  return h.tileProps.at(-1)?.overlays;
}

function lastMissOptions(): UseTemplateMissRefetchOptions {
  const options = h.missRefetch.mock.calls.at(-1)?.[0] as UseTemplateMissRefetchOptions | undefined;
  if (options === undefined) throw new Error('useTemplateMissRefetch was never called');
  return options;
}

describe('LayoutGrid Tile — turning an overlay and its snapshot into the painted set', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    serveLayout();
    serveSnapshot(undefined);
  });

  it('paints a Text element with no colour in the default overlay colour', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ color: undefined as unknown as string })]));
    render(page());

    expect(lastOverlays()).toEqual([expect.objectContaining({ kind: 'Text', color: DEFAULT_OVERLAY_COLOR })]);
  });

  it('paints a shape with its own colour and geometry, and a Text element with its own text and font size', () => {
    h.overlayQuery.mockReturnValue(
      overlayOf([
        boxEl({ kind: 'Ellipse', normalizedX: 0.3, normalizedY: 0.4, normalizedWidth: 0.2, normalizedHeight: 0.1 }),
        textEl({ text: 'Plain', fontSizePx: 18 }),
      ]),
    );
    render(page());

    expect(lastOverlays()).toEqual([
      {
        kind: 'Ellipse',
        color: '#FF0000FF',
        normalizedX: 0.3,
        normalizedY: 0.4,
        normalizedWidth: 0.2,
        normalizedHeight: 0.1,
      },
      {
        kind: 'Text',
        color: '#FFFFFFD9',
        text: 'Plain',
        normalizedX: 0.1,
        normalizedY: 0.05,
        normalizedWidth: 0.3,
        normalizedHeight: 0.08,
        fontSizePx: 18,
      },
    ]);
  });
});

describe('LayoutGrid Tile — the template-miss refetch is fed the right inputs', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    serveLayout();
    serveSnapshot(undefined);
  });

  it('is not told of a miss while the snapshot has not arrived', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })]));
    render(page());

    expect(lastMissOptions()).toMatchObject({ overlayIdentifier: 'ovl-x', hasMiss: false, skip: false });
  });

  it('is not told of a miss when the snapshot holds the template', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })]));
    serveSnapshot([{ template: 'Temp {{t}}', resolved: 'Temp 21' }]);
    render(page());

    expect(lastMissOptions().hasMiss).toBe(false);
  });

  it('is told of a miss when the snapshot lacks a placeholder template', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })]));
    serveSnapshot([{ template: 'Other {{u}}', resolved: 'Other 1' }]);
    render(page());

    expect(lastMissOptions().hasMiss).toBe(true);
  });

  it('is not told of a miss for a static text the snapshot does not list', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Plain' }), textEl({ text: 'Temp {{t}}' })]));
    serveSnapshot([{ template: 'Temp {{t}}', resolved: 'Temp 21' }]);
    render(page());

    expect(lastMissOptions().hasMiss).toBe(false);
  });

  it('keys the episode on the published overlay revision number', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })], 'Published', 7));
    render(page());

    expect(lastMissOptions().publicationKey).toBe('7');
  });

  it('has no episode key while the overlay has no Published revision', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })], 'Archived', 7));
    render(page());

    expect(lastMissOptions().publicationKey).toBeUndefined();
  });

  it('tells the refetch to stand down when the snapshot query is skipped for want of a fab', () => {
    serveLayout('  ');
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })]));
    render(page());

    expect(lastMissOptions().skip).toBe(true);
  });
});

describe('LayoutGrid Tile — the placeholder verdict follows the overlay', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    serveLayout();
    serveSnapshot(undefined);
  });

  function pushResolvedText() {
    act(() => {
      h.lifecycle?.onResolvedOverlayTextChanged?.({ overlay: 'ovl-x', fab: 'munich', version: 1, texts: [] } as never);
    });
  }

  function staticPushLines() {
    return h.logResilienceEvent.mock.calls.filter(([, event]) => event === 'resolved-text-for-static-label');
  }

  it('does not flag a push for a placeholder overlay as one for a static label', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })]));
    render(page());
    pushResolvedText();

    expect(staticPushLines()).toEqual([]);
  });

  it('flags a push as one for a static label once a republish drops the placeholder', () => {
    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp {{t}}' })]));
    const view = render(page());

    h.overlayQuery.mockReturnValue(overlayOf([textEl({ text: 'Temp' })], 'Published', 2));
    view.rerender(page());
    pushResolvedText();

    expect(staticPushLines()).toEqual([['hub', 'resolved-text-for-static-label', { overlay: 'ovl-x' }]]);
  });
});

describe('LayoutGrid Tile — an unavailable overlay is not painted', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    h.tileProps.length = 0;
    h.lifecycle = undefined;
    serveLayout();
    serveSnapshot(undefined);
  });

  it('paints nothing once a pushed archive frame flags the overlay, though its elements are still loaded', () => {
    h.overlayQuery.mockReturnValue(overlayOf([boxEl(), textEl()]));
    render(page());
    expect(lastOverlays()).toHaveLength(2);

    act(() => {
      h.lifecycle?.onOverlayArchived?.({ overlay: 'ovl-x', fab: 'munich' } as never);
    });

    expect(lastOverlays()).toBeUndefined();
  });
});
