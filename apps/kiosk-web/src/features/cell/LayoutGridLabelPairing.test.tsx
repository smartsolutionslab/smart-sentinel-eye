import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutHubCallbacks } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { store } from '../../app/store.js';

/**
 * Spec 301 (#2348) US1, T005 — the premise test.
 *
 * <p>
 * Proves, on today's unmodified `LayoutGrid.tsx`, that the wall's hold
 * (`useLabelDelay`, ADR-0129) re-joins a held text with a *current* element
 * by raw array index. A reorder of the element array during the hold window
 * mis-joins a caption's text onto the wrong geometry, because `elements` is
 * read fresh every render while `resolvedTexts` stays behind until the hold
 * elapses — two arrays, two different moments, joined positionally
 * (`LayoutGrid.tsx:471-494`).
 * </p>
 *
 * <p>
 * Self-contained: a dedicated file rather than an addition to
 * `CellPage.test.tsx`, so this red suite is disjoint from the characterisation
 * baseline T001 records and can be deleted or rewritten independently of it.
 * Renders `LayoutGrid` directly (not `CellPage`) with `layoutIdentifier`
 * passed as a prop, which is why `useParams` needs no mock here, unlike
 * `CellPage.test.tsx`.
 * </p>
 */

const getLayoutMock = vi.fn();
const getOverlayMock = vi.fn();
const getSnapshotMock = vi.fn();
const navigateMock = vi.fn();

let capturedCallbacks: LayoutHubCallbacks | undefined;

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useGetLayoutQuery: (...args: unknown[]) => getLayoutMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useGetOverlayQuery: (...args: unknown[]) => getOverlayMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    // No overlay in this file uses a `{{placeholder}}`, so every tile skips
    // this query (`hasPlaceholder` is false) — the fallback path
    // (`element.kind === 'Text' ? element.text : ''`) is exactly the one
    // US1 is about, and keeping it unmocked-but-unreached is deliberate.
    useGetOverlaySnapshotQuery: (...args: unknown[]) => getSnapshotMock(...args),
  };
});

/** Spied, not left real: isolated to this file's own module registry, so no other spec is affected. */
const reportKioskLatencyMock = vi.fn();
const measureOverlayDrawMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/observability/kioskLatency', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/observability/kioskLatency')>();
  return {
    ...actual,
    reportKioskLatency: (...args: unknown[]) => reportKioskLatencyMock(...args),
    measureOverlayDraw: (...args: unknown[]) => measureOverlayDrawMock(...args),
  };
});

vi.mock('react-oidc-context', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    user: { access_token: 'fake-token' },
  }),
}));

vi.mock('@smart-sentinel-eye/shared/realtime/layoutHub', () => ({
  createLayoutHubClient: (_config: unknown, callbacks: LayoutHubCallbacks) => {
    capturedCallbacks = callbacks;
    return {
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
      state: () => 'Connected',
    };
  },
}));

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

/**
 * Captures the full overlay set `LayoutGrid` hands `CameraViewer`, as JSON —
 * enough to find an element by its own geometry and read the text paired
 * with it, which is the whole question this suite asks. Mirrors
 * `CellPage.test.tsx`'s own `CameraViewer` double (`data-overlay-elements`).
 */
let reportLag: ((camera: string, lag: number, buffer: number) => void) | undefined;

interface MockOverlayElement {
  kind: 'Text' | 'Box' | 'Ellipse';
  color: string;
  text?: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
}

vi.mock('@smart-sentinel-eye/shared/ui/composites/CameraViewer', () => ({
  CameraViewer: ({
    cameraIdentifier,
    overlays,
    onLagMeasured,
  }: {
    cameraIdentifier: string;
    overlays?: readonly MockOverlayElement[];
    onLagMeasured?: (camera: string, lag: number, buffer: number) => void;
  }) => {
    if (onLagMeasured) {
      reportLag = onLagMeasured;
    }
    return (
      <div data-testid="camera-viewer" data-overlay-elements={JSON.stringify(overlays ?? [])}>
        {cameraIdentifier}
      </div>
    );
  },
}));

const { LayoutGrid } = await import('./LayoutGrid.js');

function tile(overrides: Partial<LayoutTile> = {}): LayoutTile {
  return {
    cameraIdentifier: 'cam-a',
    overlayIdentifier: 'ovl-x',
    row: 0,
    col: 0,
    rowSpan: 1,
    colSpan: 1,
    ...overrides,
  };
}

function publishedRevision(tiles: LayoutTile[]): Layout {
  return {
    layoutIdentifier: 'cam-1',
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
        gridCols: 1,
        tiles,
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
  };
}

function mockLayout(layout: Layout) {
  getLayoutMock.mockReturnValue({
    data: layout,
    isLoading: false,
    error: undefined,
    refetch: vi.fn(),
  });
}

function textElement(overrides: Partial<MockOverlayElement> = {}) {
  return {
    kind: 'Text' as const,
    color: '#FFFFFFD9',
    text: 'L1',
    normalizedX: 0.1,
    normalizedY: 0.05,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    fontSizePx: 48,
    ...overrides,
  };
}

function boxElement(overrides: Partial<MockOverlayElement> = {}) {
  return {
    kind: 'Box' as const,
    color: '#D32F2FFF',
    normalizedX: 0.6,
    normalizedY: 0.6,
    normalizedWidth: 0.2,
    normalizedHeight: 0.2,
    ...overrides,
  };
}

/** An overlay whose published revision carries exactly the given elements, in that order. */
function publishedOverlayWith(elements: unknown[]) {
  return {
    data: {
      overlayIdentifier: 'ovl-x',
      name: 'Bound overlay',
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      revisions: [
        {
          revisionIdentifier: 'or1',
          revisionNumber: 1,
          state: 'Published',
          elements,
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '00000000-0000-0000-0000-000000000001',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    },
  };
}

function renderTile() {
  mockLayout(publishedRevision([tile()]));
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <LayoutGrid layoutIdentifier="cam-1" />
      </MemoryRouter>
    </Provider>,
  );
}

/**
 * Forces a re-render that is not itself a label-set change, exactly as
 * `CellPage.test.tsx`'s own `rerenderTiles` does — a highlight push is used
 * because `reportLag` writes a ref (`useWallAlignment`), not state, so it
 * causes no render of its own, and because changing a `vi.fn()` mock's
 * return value needs a render to be picked up at all.
 */
function rerenderTiles() {
  act(() => {
    capturedCallbacks?.onOverlayHighlightChanged?.({ overlay: 'ovl-x', fab: 'munich', durationMs: 1_000 });
  });
}

function renderedElements(): MockOverlayElement[] {
  const raw = screen.getByTestId('camera-viewer').getAttribute('data-overlay-elements') ?? '[]';
  return JSON.parse(raw) as MockOverlayElement[];
}

function elementAt(normalizedX: number): MockOverlayElement | undefined {
  return renderedElements().find((element) => element.normalizedX === normalizedX);
}

describe('LayoutGrid Tile — the held set paired with a current element by index (#2348, US1 premise)', () => {
  beforeEach(() => {
    getLayoutMock.mockReset();
    getOverlayMock.mockReset();
    getSnapshotMock.mockReset();
    getSnapshotMock.mockReturnValue({ data: undefined, isLoading: false });
    navigateMock.mockReset();
    reportKioskLatencyMock.mockReset();
    measureOverlayDrawMock.mockReset();
    capturedCallbacks = undefined;
    reportLag = undefined;
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("Does not pair a reordered caption's text with the other caption's geometry during the hold", () => {
    const firstSet = [textElement({ text: 'L1', normalizedX: 0.1 }), textElement({ text: 'L2', normalizedX: 0.7 })];
    const reordered = [textElement({ text: 'L2', normalizedX: 0.7 }), textElement({ text: 'L1', normalizedX: 0.1 })];

    getOverlayMock.mockReturnValue(publishedOverlayWith(firstSet));
    renderTile();
    expect(elementAt(0.1)?.text).toBe('L1');
    expect(elementAt(0.7)?.text).toBe('L2');

    // The tile reports a 120 ms-old picture, so the set-wide hold engages.
    act(() => {
      reportLag?.('cam-a', 120, 40);
    });

    getOverlayMock.mockReturnValue(publishedOverlayWith(reordered));
    rerenderTiles();

    // Still inside the hold window: neither geometry may show the OTHER
    // caption's text. On today's code, `elements` is read fresh (current)
    // while `resolvedTexts` is still the pre-reorder array, joined by index —
    // so L1's geometry (0.1) shows L2's stale-by-index text and vice versa.
    expect(elementAt(0.1)?.text, "L1's own geometry, during the hold").toBe('L1');
    expect(elementAt(0.7)?.text, "L2's own geometry, during the hold").toBe('L2');
  });

  it('Does not paint an empty caption when a Box/Text republish swaps their order', () => {
    const boxThenText = [boxElement({ normalizedX: 0.6 }), textElement({ text: 'Zone A', normalizedX: 0.1 })];
    const textThenBox = [textElement({ text: 'Zone A', normalizedX: 0.1 }), boxElement({ normalizedX: 0.6 })];

    getOverlayMock.mockReturnValue(publishedOverlayWith(boxThenText));
    renderTile();
    expect(elementAt(0.1)?.text).toBe('Zone A');

    act(() => {
      reportLag?.('cam-a', 120, 40);
    });

    getOverlayMock.mockReturnValue(publishedOverlayWith(textThenBox));
    rerenderTiles();

    // On today's code the Text element's stale-by-index resolved text is ''
    // (the Box's own slot before the swap), and `'' ?? el.text` stays '' —
    // the caption goes blank instead of holding 'Zone A'.
    expect(elementAt(0.1)?.text, 'the Text element, during the hold').toBe('Zone A');
  });

  it('Holds a geometry-only republish for the same duration as a text change, instead of painting it at once', () => {
    const before = [textElement({ text: 'L1', normalizedX: 0.1 })];
    const movedOnly = [textElement({ text: 'L1', normalizedX: 0.4 })];

    getOverlayMock.mockReturnValue(publishedOverlayWith(before));
    renderTile();
    expect(elementAt(0.1)).toBeDefined();

    act(() => {
      reportLag?.('cam-a', 120, 40);
    });

    getOverlayMock.mockReturnValue(publishedOverlayWith(movedOnly));
    rerenderTiles();

    // Still inside the hold: the picture has not caught up yet, so the
    // geometry should not have moved either (ADR-0129, "the set ages as a
    // unit"). Today's code reads geometry straight from `elements` every
    // render with no hold at all, so it has already jumped to 0.4.
    expect(elementAt(0.1), 'geometry held at its pre-republish position').toBeDefined();
    expect(elementAt(0.4), 'geometry not yet released').toBeUndefined();

    act(() => {
      vi.advanceTimersByTime(120);
    });

    // After the hold elapses, the move is painted — proving the assertion
    // above was about timing, not about the move never happening at all.
    expect(elementAt(0.4), 'geometry released once the hold elapses').toBeDefined();
  });

  it('Reports exactly one hold for one reorder, not one per re-render', () => {
    const firstSet = [textElement({ text: 'L1', normalizedX: 0.1 }), textElement({ text: 'L2', normalizedX: 0.7 })];
    const reordered = [textElement({ text: 'L2', normalizedX: 0.7 }), textElement({ text: 'L1', normalizedX: 0.1 })];

    getOverlayMock.mockReturnValue(publishedOverlayWith(firstSet));
    renderTile();

    act(() => {
      reportLag?.('cam-a', 120, 40);
    });

    getOverlayMock.mockReturnValue(publishedOverlayWith(reordered));
    rerenderTiles();
    // A second re-render that changes nothing about the paired set — must not
    // add a second hold.
    rerenderTiles();

    act(() => {
      vi.advanceTimersByTime(120);
    });

    const labelDelayReports = reportKioskLatencyMock.mock.calls.filter((call) => call[0] === 'label_delay');
    expect(labelDelayReports).toHaveLength(1);
  });
});
