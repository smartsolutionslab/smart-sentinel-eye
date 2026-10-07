import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider } from 'react-redux';
import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import type { LayoutHubCallbacks } from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { store } from '../../app/store.js';

/**
 * Spec 301 (#2720) US2, T013 — the premise test.
 *
 * <p>
 * US1 (`LayoutGridLabelPairing.test.tsx`) proved the mis-join that comes from
 * `useLabelDelay` holding a text array behind its geometry. This file proves
 * the *other* timing source (spec.md "(b) It comes from a different
 * context"): even with no hold in play at all (the tile never reports a
 * frame age), the SystemVariables snapshot itself is a positional list that
 * was indexed against the PREVIOUS element order. A republished reorder
 * changes which element is at which index; the snapshot does not move until
 * SystemVariables re-indexes it — which can lag arbitrarily, not just for a
 * frame's age.
 * </p>
 *
 * <p>
 * <b>Observed red first, on today's positional lookup.</b> `Tile`'s inline
 * `liveTextFor` (`LayoutGrid.tsx:446`) reads
 * `snapshot?.resolvedTexts[index]` — after a reorder, index 0 is a different
 * element than it was when the snapshot was built, so the text and the
 * geometry it is painted on no longer agree. After spec 301 US2 lands, the
 * kiosk looks the text up by the element's own raw template instead, so a
 * pure reorder is correct even while the snapshot is stale.
 * </p>
 *
 * <p>
 * Per the brief: after T014, only this file's fake snapshot's shape changes
 * (`resolvedTexts: string[]` → `texts: { template, resolved }[]`); the
 * `expect` lines below must not move.
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
    useGetOverlaySnapshotQuery: (...args: unknown[]) => getSnapshotMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/observability/kioskLatency', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/observability/kioskLatency')>();
  return {
    ...actual,
    reportKioskLatency: vi.fn(),
    measureOverlayDraw: vi.fn(),
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
  }: {
    cameraIdentifier: string;
    overlays?: readonly MockOverlayElement[];
  }) => (
    <div data-testid="camera-viewer" data-overlay-elements={JSON.stringify(overlays ?? [])}>
      {cameraIdentifier}
    </div>
  ),
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

function textElement(overrides: Partial<MockOverlayElement> = {}): MockOverlayElement {
  return {
    kind: 'Text',
    color: '#FFFFFFD9',
    text: 'L1 {{t1}}',
    normalizedX: 0.1,
    normalizedY: 0.05,
    normalizedWidth: 0.3,
    normalizedHeight: 0.08,
    ...overrides,
  };
}

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

/** Forces a re-render that is not itself a label-set change (no lag report — this
 * suite never reports a frame age, so no hold is ever in play; see the file
 * doc comment). */
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

describe('LayoutGrid Tile — a reorder republished while the snapshot is stale (#2720, US2 premise)', () => {
  beforeEach(() => {
    getLayoutMock.mockReset();
    getOverlayMock.mockReset();
    getSnapshotMock.mockReset();
    navigateMock.mockReset();
    capturedCallbacks = undefined;
  });

  it("Pairs each caption with its own template's value after a reorder, even though the snapshot was never re-fetched", () => {
    // The snapshot SystemVariables built BEFORE the reorder: index 0 was the
    // 'L1 {{t1}}' element (at x 0.1), index 1 the 'L2 {{t2}}' element (at
    // x 0.7). T014 replaces this fixture's shape with
    // `texts: [{ template: 'L1 {{t1}}', resolved: 'L1 21' }, { template:
    // 'L2 {{t2}}', resolved: 'L2 35' }]` — the assertions below do not move.
    getSnapshotMock.mockReturnValue({
      data: {
        overlayIdentifier: 'ovl-x',
        texts: [
          { template: 'L1 {{t1}}', resolved: 'L1 21' },
          { template: 'L2 {{t2}}', resolved: 'L2 35' },
        ],
        version: 1,
      },
      isLoading: false,
    });

    const firstSet = [
      textElement({ text: 'L1 {{t1}}', normalizedX: 0.1 }),
      textElement({ text: 'L2 {{t2}}', normalizedX: 0.7 }),
    ];
    getOverlayMock.mockReturnValue(publishedOverlayWith(firstSet));
    renderTile();
    expect(elementAt(0.1)?.text).toBe('L1 21');
    expect(elementAt(0.7)?.text).toBe('L2 35');

    // The revision republishes reordered — same two elements, opposite
    // order, same geometry each keeps. SystemVariables has NOT re-indexed
    // it: `getSnapshotMock` keeps returning the exact same (now stale)
    // snapshot above. No `reportLag` call happens anywhere in this test, so
    // no hold is ever scheduled — this is deliberately isolated from US1's
    // mechanism.
    const reordered = [
      textElement({ text: 'L2 {{t2}}', normalizedX: 0.7 }),
      textElement({ text: 'L1 {{t1}}', normalizedX: 0.1 }),
    ];
    getOverlayMock.mockReturnValue(publishedOverlayWith(reordered));
    rerenderTiles();

    // Each caption must show ITS OWN template's resolved value, never the
    // other one's — regardless of the snapshot's index having gone stale.
    // On today's positional lookup (`snapshot.resolvedTexts[index]`), index
    // 0 is now the L2 element but still reads the snapshot's old index-0
    // value ('L1 21'), and vice versa — this is the mis-join spec.md's US2
    // acceptance scenario describes.
    expect(elementAt(0.7)?.text, "L2's own template, after the reorder").toBe('L2 35');
    expect(elementAt(0.1)?.text, "L1's own template, after the reorder").toBe('L1 21');
  });

  /**
   * Spec.md's "A genuinely new template is not given another template's
   * text (US2)" scenario, and T014's companion red test. A single-element
   * overlay whose template CHANGES (not reordered — there is only ever one
   * element): the stale snapshot still carries the OLD template's resolved
   * value at the only index there is, so today's positional lookup
   * (`resolvedTexts[0]`) hands the new element a value that was never its
   * own.
   */
  it("Shows a brand-new template's own raw text, never the stale value a previous template resolved to", () => {
    // Built for the OLD template 'Temp: {{t}}'. T014 replaces this fixture's
    // shape with `texts: [{ template: 'Temp: {{t}}', resolved: 'Temp: 21' }]`
    // — the assertions below do not move.
    getSnapshotMock.mockReturnValue({
      data: {
        overlayIdentifier: 'ovl-x',
        texts: [{ template: 'Temp: {{t}}', resolved: 'Temp: 21' }],
        version: 1,
      },
      isLoading: false,
    });

    getOverlayMock.mockReturnValue(publishedOverlayWith([textElement({ text: 'Temp: {{t}}', normalizedX: 0.1 })]));
    renderTile();
    expect(elementAt(0.1)?.text).toBe('Temp: 21');

    // The overlay is republished with its one element's template CHANGED.
    // SystemVariables has not yet resolved it, so the snapshot above is
    // untouched — same single stale entry, same index 0.
    getOverlayMock.mockReturnValue(
      publishedOverlayWith([textElement({ text: 'Temperature: {{t}}', normalizedX: 0.1 })]),
    );
    rerenderTiles();

    // Must show its OWN raw template (FR-007), never the previous
    // template's stale resolved value. On today's positional lookup,
    // `resolvedTexts[0]` is truthy ('Temp: 21'), so `?? el.text` never even
    // triggers — the caption keeps showing the old reading under a
    // template it no longer belongs to.
    expect(
      elementAt(0.1)?.text,
      'a template with no entry shows itself, never a different template’s stale value',
    ).toBe('Temperature: {{t}}');
  });
});
