import { render, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse } from '@smart-sentinel-eye/shared/test/responses';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

// gateway.ts resolves the API origin at module load: stub it before any dynamic import touches layouts.api, so
// fetchBaseQuery builds absolute URLs (same reason as LayoutEditorDialogChainRetention.test.tsx).
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

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

const { layoutsApi } = await import('@smart-sentinel-eye/shared/api/layouts.api');
const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

const LAYOUT_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const CAMERA_ID = '11111111-1111-1111-1111-111111111111';

function chainOf(layoutIdentifier: string, version: number) {
  return {
    layoutIdentifier,
    version,
    fab: 'munich',
    name: 'Layout',
    createdAt: '2026-05-27T10:00:00Z',
    createdBy: '22222222-2222-2222-2222-222222222222',
    revisions: [],
  };
}

const TARGET_A = {
  layoutIdentifier: LAYOUT_A,
  revisionNumber: 1,
  name: 'Layout',
  grid: { rows: 1, cols: 1 },
  tiles: [{ cameraIdentifier: CAMERA_ID, overlayIdentifier: null, row: 0, col: 0, rowSpan: 1, colSpan: 1 }],
};

function ui(store: ReturnType<typeof createApiStore>, open: boolean, editTarget: typeof TARGET_A | undefined) {
  return (
    <Provider store={store}>
      <LayoutEditorDialog open={open} onOpenChange={() => {}} editTarget={editTarget} />
    </Provider>
  );
}

/**
 * Spec 476 T004. `refetchOnMountOrArgChange` on the dialog's chain read is what makes "a reopen within the 60 s cache
 * window always asks the server again" true on its own terms. The existing reopen test does not pin it: it invalidates the
 * tag while the dialog is unsubscribed, so the entry is evicted and the second read happens whatever the option says.
 * This one reopens with nothing invalidating anything. Green on the unsplit dialog by design; it must stay green after.
 *
 * What is deliberately NOT here: `currentData` against `data`. Reading `data` changes nothing a test can see while the Save
 * gate's `chainFetching` and `chainFailed` terms exist, because each independently blocks Save in the moments the two
 * fields differ; only breaking one of those terms together with the field makes the family fail (recorded in verification.md).
 */
describe('LayoutEditorDialog — the chain read asks again on reopen (spec 153)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('Reads the chain again when the same layout is reopened inside the cache window, with nothing invalidating it', async () => {
    let layoutACalls = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((request: Request) => {
        if (request.method === 'GET' && request.url.includes(LAYOUT_A)) {
          layoutACalls += 1;
          return Promise.resolve(jsonResponse(chainOf(LAYOUT_A, 7)));
        }
        throw new Error(`unexpected fetch: ${request.method} ${request.url}`);
      }),
    );
    const store = createApiStore([layoutsApi]);

    const { rerender } = render(ui(store, true, TARGET_A));
    await waitFor(() => {
      expect(layoutsApi.endpoints.getLayout.select(LAYOUT_A)(store.getState()).data?.version).toBe(7);
    });
    expect(layoutACalls).toBe(1);

    rerender(ui(store, false, undefined));
    rerender(ui(store, true, TARGET_A));

    await waitFor(() => expect(layoutACalls).toBe(2), { timeout: 2000 });
  });
});
