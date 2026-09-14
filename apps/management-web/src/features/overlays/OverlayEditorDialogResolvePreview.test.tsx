import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// gateway.ts resolves the origin at module load (systemVariables.api.test.ts
// does the same); stub it before the dynamic imports below so
// `fetchBaseQuery` builds absolute URLs — Node's `Request` rejects relative
// ones, unlike a browser.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { store } = await import('../../app/store.js');

/**
 * Spec 148 T014/T018 — this wiring (`OverlayEditorDialog` owning
 * `useDebouncedValue` + `useResolveOverlayTextQuery` and passing the result
 * into `OverlayEditor`) has no test handed down from phase 4a, unlike
 * `placeholderAdvisories.test.ts`, `PlaceholderPreviewPanel.test.tsx` and
 * `systemVariables.api.test.ts`. Written red-first myself per the brief:
 * observed failing against the pre-wiring dialog (no query, no
 * `resolvedPreview` prop reaching `OverlayEditor`), then made to pass by the
 * wiring in `OverlayEditorDialog.tsx` / `OverlayEditor.tsx`.
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [createDraftMock, { isLoading: false, error: undefined, reset: vi.fn() }],
  };
});

const useListAllCameraChoicesQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return {
    ...actual,
    useListAllCameraChoicesQuery: (...args: unknown[]) => useListAllCameraChoicesQueryMock(...args),
  };
});

const useGetStreamQueryMock = vi.fn();
vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
  };
});

const { OverlayEditorDialog } = await import('./OverlayEditorDialog.js');

function renderDialog() {
  return render(
    <Provider store={store}>
      <OverlayEditorDialog open={true} onOpenChange={() => {}} />
    </Provider>,
  );
}

function resolveResponse(): Response {
  return new Response(
    JSON.stringify({
      resolvedText: '23.4',
      placeholders: [{ name: 'temperature', outcome: 'Resolved', fab: 'munich', renderedValue: '23.4' }],
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

describe('OverlayEditorDialog resolve-preview wiring (spec 148 T014/T018)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createDraftMock.mockClear();
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    });
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });
    fetchMock = vi.fn(async () => resolveResponse());
    globalThis.fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('Issues no resolve request for text with no {{ (spec 148 US1 scenario 14)', async () => {
    renderDialog();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: 'Line 1 static text' } });

    // Real time: DEBOUNCE_MS (250) plus slack, long enough that a request
    // would have fired if the `{{` guard were absent.
    await new Promise((resolve) => setTimeout(resolve, 400));

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('Resolves a typed placeholder and passes it down: the canvas shows the resolved value and the panel names it (spec 148 US1 scenario 1, US3 scenario 1)', async () => {
    renderDialog();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{temperature}}' } });

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
    const requested = fetchMock.mock.calls[0]?.[0];
    const requestedUrl = new URL(requested instanceof Request ? requested.url : (requested as string));
    expect(requestedUrl.pathname.endsWith('/resolve')).toBe(true);
    expect(requestedUrl.searchParams.get('text')).toBe('{{temperature}}');

    // The input keeps the raw text; the canvas box shows the resolved value.
    expect(screen.getByTestId('overlay-editor-text')).toHaveValue('{{temperature}}');
    await waitFor(() => {
      expect(screen.getByTestId('overlay-editor-preview').textContent).toBe('23.4');
    });
    expect(screen.getByTestId('placeholder-advisory-resolved').textContent).toBe('temperature — 23.4 (munich)');
  });
});
