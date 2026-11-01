import { vi } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Provider, useSelector } from 'react-redux';
import type { Layout, LayoutTile } from '@smart-sentinel-eye/shared/api/layouts.api';
import type {
  LayoutHubCallbacks,
  ResolvedOverlayTextChangedMessage,
} from '@smart-sentinel-eye/shared/realtime/layoutHub';
import { systemVariablesApi } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import type { OverlaySnapshotInput } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { store } from '../app/store.js';

export const getLayoutMock = vi.fn();
export const navigateMock = vi.fn();

export const getOverlayMock = vi.fn();

// Capture the callbacks the lifecycle hook hands to the (long-lived) hub so
// a test can fire a synthetic OverlayHighlightChanged frame post-render.
export let capturedCallbacks: LayoutHubCallbacks | undefined;

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

export const getSnapshotMock = vi.fn();

/**
 * Spec 150 (#2345) T020. Spies on the real `measureOverlayDraw` — everything
 * else in the module (`reportKioskLatency`, used by `useLabelDelay`'s own
 * report callback) stays real — so a test can assert it fires once per real
 * label-set change, not once per render (the #1888/#1889 defect class,
 * ADR-0123).
 */
export const measureOverlayDrawMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/observability/kioskLatency', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/observability/kioskLatency')>();
  return {
    ...actual,
    measureOverlayDraw: (...args: unknown[]) => measureOverlayDrawMock(...args),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/systemVariables.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/systemVariables.api')>();
  return {
    ...actual,
    // Default: no resolved snapshot. Specific tests re-mock to assert the
    // resolved-text rendering path — which the fixed arrow this replaced
    // promised in a comment but made impossible.
    useGetOverlaySnapshotQuery: (...args: unknown[]) => getSnapshotMock(...args),
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
    useParams: () => ({ layoutIdentifier: 'cam-1' }),
    useNavigate: () => navigateMock,
  };
});

/**
 * The viewer's props are captured so a test can drive the one thing jsdom
 * cannot produce: a measured frame age. Without a lag report every tile's age
 * is null, the label hold never engages, and a test of the hold passes with the
 * wiring cut — the vacuous-test trap spec 045's review found five of.
 */
export let reportLag: ((camera: string, lag: number, buffer: number) => void) | undefined;

/**
 * Spec 204 T002: the single `reportLag` above is overwritten by whichever
 * tile mounts (renders) last, so it cannot drive a multi-tile wall's tiles
 * independently. Extended, not replaced: every existing single-tile test
 * keeps using `reportLag`, and a two-tile test reaches its own tile through
 * this map instead.
 */
export const reportLagByCamera = new Map<string, (camera: string, lag: number, buffer: number) => void>();

/**
 * Spec 204 T002: counts each camera's own re-renders, so the render-count
 * assertion (SC-1) reads directly off how many times the double's function
 * body ran rather than inferring it from a DOM diff.
 */
export const renderCountByCamera = new Map<string, number>();

/** The shape this file's `CameraViewer` double captures per overlay element — enough to check kind, colour, text and geometry without rendering the real composite (which has its own coverage, `CameraViewerMixedElementsCharacterisation.test.tsx`). */
export interface MockOverlayElement {
  kind: 'Text' | 'Box' | 'Ellipse';
  color: string;
  text?: string;
  normalizedX: number;
  normalizedY: number;
  normalizedWidth: number;
  normalizedHeight: number;
}

// CameraTile (the per-tile wrapper, #2846/ADR-0169) makes this call for real
// now that CameraViewer no longer does — this file mocks CameraViewer itself
// wholesale and never needed to mock the stream hook before. Stubbed here
// only so CameraTile's hook call does not fire a real, unmocked network
// request (and its 5 s poll does not leak a timer past unmount); none of
// this file's assertions read the stream the real CameraViewer would have
// used it for.
vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({ data: undefined, currentData: undefined, isLoading: false, error: undefined }),
  };
});

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
      reportLagByCamera.set(cameraIdentifier, onLagMeasured);
    }
    renderCountByCamera.set(cameraIdentifier, (renderCountByCamera.get(cameraIdentifier) ?? 0) + 1);
    // This file's fixtures are overwhelmingly single-label (multi-label
    // rendering is T019/T023's own coverage) — the first label's text stands
    // in for the whole set, matching every existing assertion here unchanged.
    // `data-overlay-elements` is the FULL set (spec 300, #2349, S2): a mixed
    // Box/Text overlay cannot be checked through `data-overlay-text` alone,
    // since index 0 there may not be the Text element at all.
    return (
      <div
        data-testid="camera-viewer"
        data-overlay-text={overlays?.[0]?.text ?? ''}
        data-overlay-elements={JSON.stringify(overlays ?? [])}
      >
        {cameraIdentifier}
      </div>
    );
  },
}));

const { CellPage } = await import('../features/cell/CellPage.js');

/**
 * A snapshot hook reading the same RTK cache the page dispatches into.
 *
 * <p>
 * The one substitution that would make a push test vacuous is a snapshot hook
 * that returns a canned value: it would assert the mock, not the dispatch.
 * </p>
 *
 * <p>
 * Shared by the #2012 push test and the #2069 fab tests deliberately: the
 * argument below <b>is</b> the RTK Query cache key, and two copies of it could
 * drift apart while both suites stayed green (plan.md, Risk 1).
 * </p>
 */
export function useSnapshotFromTheRealCache(input: OverlaySnapshotInput, options?: { skip?: boolean }) {
  // Hoisted above the branch: a conditional hook call would be a second
  // bug, not a fix. `skip` is honoured because the production call site
  // passes it, and a fake that ignores it cannot tell a skipped query from
  // a fetched one — which is what made the placeholder comment below a
  // claim about production rather than about this test.
  //
  // `input` is passed through untouched: it is the cache key the page chose,
  // and reconstructing it here would be a second copy free to drift.
  const cached = useSelector(systemVariablesApi.endpoints.getOverlaySnapshot.select(input));
  return options?.skip === true ? { data: undefined, isLoading: false } : cached;
}

export function tile(overrides: Partial<LayoutTile> = {}): LayoutTile {
  return {
    cameraIdentifier: 'cam-99',
    overlayIdentifier: null,
    row: 0,
    col: 0,
    // Spec 262 (#2607): `LayoutTile` gains these as required fields — the
    // backend always sends them (plan.md §4.1).
    rowSpan: 1,
    colSpan: 1,
    ...overrides,
  };
}

export function chain(overrides: Partial<Layout> = {}): Layout {
  return {
    layoutIdentifier: 'cam-1',
    version: 0,
    name: 'Line-1',
    // The wall's fab, which it derives rather than chooses (ADR-0145). Every
    // test in this file renders a munich wall, so 'dresden' below always means
    // "somebody else's plant".
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
        tiles: [tile()],
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
    ...overrides,
  };
}

export function publishedRevision(gridRows: number, gridCols: number, tiles: LayoutTile[]): Layout {
  return chain({
    revisions: [
      {
        revisionIdentifier: 'r1',
        revisionNumber: 1,
        state: 'Published',
        gridRows,
        gridCols,
        tiles,
        createdAt: '2026-05-26T10:00:00Z',
        createdBy: '00000000-0000-0000-0000-000000000001',
        publishedAt: '2026-05-26T10:00:00Z',
        archivedAt: null,
      },
    ],
  });
}

export function mockLayout(layout: Layout) {
  getLayoutMock.mockReturnValue({
    data: layout,
    isLoading: false,
    error: undefined,
    refetch: vi.fn(),
  });
}

export function renderPage() {
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <CellPage />
      </MemoryRouter>
    </Provider>,
  );
}

/** An overlay whose published revision carries a single-element `labels` set (spec 150). */
export function publishedOverlay(text: string) {
  return {
    data: {
      overlayIdentifier: 'ovl-x',
      name: 'Bound label',
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      revisions: [
        {
          revisionIdentifier: 'or1',
          revisionNumber: 1,
          state: 'Published',
          elements: [
            {
              kind: 'Text',
              color: '#FFFFFFD9',
              text,
              normalizedX: 0.5,
              normalizedY: 0.05,
              normalizedWidth: 0.3,
              normalizedHeight: 0.08,
              fontSizePx: 48,
            },
          ],
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '00000000-0000-0000-0000-000000000001',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    },
  };
}

/**
 * An overlay whose published revision carries several labels, in order
 * (spec 150, #2345) — each at its own position so the fixture does not rely
 * on FR-004's "duplicate geometry is allowed" to stay readable.
 */
export function publishedOverlayWithLabels(texts: readonly string[]) {
  return {
    data: {
      overlayIdentifier: 'ovl-x',
      name: 'Bound label',
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      revisions: [
        {
          revisionIdentifier: 'or1',
          revisionNumber: 1,
          state: 'Published',
          elements: texts.map((text, ordinal) => ({
            kind: 'Text',
            color: '#FFFFFFD9',
            text,
            normalizedX: 0.1,
            normalizedY: 0.05 + ordinal * 0.1,
            normalizedWidth: 0.3,
            normalizedHeight: 0.08,
            fontSizePx: 48,
          })),
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '00000000-0000-0000-0000-000000000001',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    },
  };
}

/**
 * A `[Box, Text]` overlay (spec 300, #2349, ADR-0165) — a shape at index 0,
 * a placeholder-bearing Text element at index 1. Used to prove spec 301
 * FR-005/FR-007: the SystemVariables snapshot's `texts` pairs are keyed by
 * the Text element's own template, not by array position, and the Box gets
 * no entry at all (shapes contribute nothing — the old index-aligned `""`
 * padding this comment used to describe is retired with V2).
 */
export function publishedOverlayWithBoxAndText(text: string) {
  return {
    data: {
      overlayIdentifier: 'ovl-mixed',
      name: 'Mixed overlay',
      createdAt: '2026-05-27T10:00:00Z',
      createdBy: '00000000-0000-0000-0000-000000000001',
      revisions: [
        {
          revisionIdentifier: 'or1',
          revisionNumber: 1,
          state: 'Published',
          elements: [
            {
              kind: 'Box',
              color: '#D32F2FFF',
              normalizedX: 0.6,
              normalizedY: 0.6,
              normalizedWidth: 0.2,
              normalizedHeight: 0.2,
            },
            {
              kind: 'Text',
              color: '#FFFFFFD9',
              text,
              normalizedX: 0.1,
              normalizedY: 0.05,
              normalizedWidth: 0.3,
              normalizedHeight: 0.08,
              fontSizePx: 48,
            },
          ],
          createdAt: '2026-05-27T10:00:00Z',
          createdBy: '00000000-0000-0000-0000-000000000001',
          publishedAt: '2026-05-27T10:00:00Z',
          archivedAt: null,
        },
      ],
    },
  };
}

/**
 * The double `Tile` queries an overlay through, extended for spec 141 site 1
 * to honour `skip` and to answer the real server's shape for the collection
 * URL a caller reaches with `''`.
 *
 * <p>
 * Every existing case either overrides this outright
 * (`mockImplementation`/`mockReturnValue` for a specific overlay) or renders
 * a tile bound to a non-empty identifier, so this default only changes what
 * the `''` cache key answers — the same lesson `useSnapshotFromTheRealCache`
 * already states above for the snapshot hook (R2: "a fake that ignores
 * [skip] cannot tell a skipped query from a fetched one").
 * </p>
 */
export function overlayDoubleDefault(overlayIdentifier: string, options?: { skip?: boolean }) {
  if (options?.skip === true) return { data: undefined };
  if (overlayIdentifier === '') {
    // The real server's answer (spec 141 site 1), not a convenience:
    // `OverlayEndpoints.cs:59` maps `GET /overlays/` to `List` and declares
    // `.Produces<ListOverlaysResponse>(200)`; `GetOne`'s
    // `{overlayIdentifier:guid}` constraint cannot match an empty segment, so
    // there is no 404 to be had here; the gateway's
    // `/overlay-designer/{**catch-all}` + `PathRemovePrefix` forwards the
    // request verbatim; and both `kiosk-web` and `kiosk-wall` carry
    // `sse.overlays.read`, so it is not a 403 either. The only possible
    // answer is 200 with the *collection* payload.
    return { data: { chains: [], published: [] } };
  }
  return { data: undefined };
}

/**
 * The `[resilience]` payloads logged under `transition`. Filtering by
 * transition rather than counting `console.info` calls keeps these cases
 * independent of every other line the page emits.
 */
export function resilienceLines(calls: unknown[][], transition: string): Record<string, unknown>[] {
  const lines: Record<string, unknown>[] = [];
  for (const [prefix, payload] of calls) {
    if (prefix !== '[resilience]' || typeof payload !== 'object' || payload === null) continue;
    const line = payload as Record<string, unknown>;
    if (line.transition === transition) lines.push(line);
  }
  return lines;
}

export function spyOnConsoleInfo() {
  return vi.spyOn(console, 'info').mockImplementation(() => undefined);
}

/**
 * Spec 301 (#2720) US2 — `ResolvedOverlayTextChangedMessage` is template-keyed
 * (`texts: { template, resolved }[]`) rather than the old positional
 * `resolvedTexts: string[]` this file used to construct. Every push fixture
 * below now builds the real wire type directly; no cast needed, since a
 * well-formed frame already matches it. `textFrameWithoutFab` below is the
 * one deliberate exception — it reproduces a fab-less, skewed-server frame,
 * which is supposed to not type-check cleanly against this happy-path type.
 */

/**
 * Fires one resolved-text frame and lets the cache write and the re-render it
 * causes settle — `upsertQueryData` is dispatched without being awaited, so a
 * fixed count of microtask turns is not enough (#2069, #2084).
 */
export async function pushText(message: ResolvedOverlayTextChangedMessage) {
  await act(async () => {
    capturedCallbacks?.onResolvedOverlayTextChanged?.(message);
    await new Promise((resolve) => setTimeout(resolve, 100));
  });
}

/**
 * The frame an older LayoutComposition puts on the wire: no `fab` field at
 * all. `ResolvedOverlayTextChangedMessage` declares `fab: string`, and that
 * declaration is precisely what a version-skewed server does not honour — so
 * the cast reproduces the fault rather than dodging the type. Module-scoped
 * (rather than local to one describe block) so both the #2084 and the spec
 * 141 site 2 blocks below can share this one cast instead of each growing
 * their own.
 */
export function textFrameWithoutFab(
  overlay: string,
  resolvedText: string,
  version: number,
): ResolvedOverlayTextChangedMessage {
  return {
    overlay,
    texts: [{ template: 'OEE {{oeeline1}}', resolved: resolvedText }],
    version,
  } as unknown as ResolvedOverlayTextChangedMessage;
}

/**
 * Spec 141 sites 2/3: a layout whose `fab` is not the `string` the type
 * declares. The cast models the drift itself, exactly as the omitted
 * `overlayIdentifier` fixtures do for site 1 — `Layout.fab` stays `string`
 * (spec FR-001/FR-004 are explicit it is not widened), so this is the only
 * way to construct the shape a field made optional would actually produce.
 */
export function layoutWithFab(fab: string | undefined, tiles: LayoutTile[]): Layout {
  return { ...publishedRevision(1, 1, tiles), fab: fab as unknown as string };
}
export { store };

export function resetCellPageMocks() {
  getLayoutMock.mockReset();
  getOverlayMock.mockReset();
  getOverlayMock.mockImplementation(overlayDoubleDefault);
  getSnapshotMock.mockReset();
  getSnapshotMock.mockReturnValue({ data: undefined, isLoading: false });
  navigateMock.mockReset();
  capturedCallbacks = undefined;
  reportLag = undefined;
  reportLagByCamera.clear();
  renderCountByCamera.clear();
}
