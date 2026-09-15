import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DEBOUNCE_MS } from '@smart-sentinel-eye/shared/hooks';

// gateway.ts resolves the origin at module load (systemVariables.api.test.ts
// does the same); stub it before the dynamic imports below so
// `fetchBaseQuery` builds absolute URLs — Node's `Request` rejects relative
// ones, unlike a browser.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');
const { store } = await import('../../app/store.js');
const { systemVariablesApi } = await import('@smart-sentinel-eye/shared/api/systemVariables.api');

/**
 * Spec 148 T014/T018 — this wiring (`OverlayEditorDialog` owning
 * `useDebouncedValue` + `useResolveOverlayTextQuery` and passing the result
 * into `OverlayEditor`) has no test handed down from phase 4a, unlike
 * `placeholderAdvisories.test.ts`, `PlaceholderPreviewPanel.test.tsx` and
 * `systemVariables.api.test.ts`. Written red-first myself per the brief:
 * observed failing against the pre-wiring dialog (no query, no
 * `resolvedPreview` prop reaching `OverlayEditor`), then made to pass by the
 * wiring in `OverlayEditorDialog.tsx` / `OverlayEditor.tsx`.
 *
 * <p><b>Phase 6 additions.</b> The tests below marked "phase 6" cover the two
 * blockers the frontend review found in that wiring: RTK Query's `data`
 * retains the last successful result across `skip` and arg changes, so (2)
 * clearing the field kept showing the previous resolve, and the dialog
 * reopened for a different overlay would too, and (3) `PlaceholderPreviewPanel`
 * diffed the live, still-being-typed text against a response for an earlier
 * version of it, flagging a well-formed placeholder as malformed for the
 * whole debounce-plus-round-trip window. Fixed by switching to
 * `currentData` (which resets on both) and withholding the preview until
 * `settledLabelText === labelText` (`OverlayEditorDialog.tsx`).</p>
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

function failedResolveResponse(): Response {
  return new Response('{}', { status: 500, headers: { 'Content-Type': 'application/json' } });
}

describe('OverlayEditorDialog resolve-preview wiring (spec 148 T014/T018)', () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createDraftMock.mockClear();
    // The store is the app's real singleton (`OverlayEditorDialog` is not
    // given a store of its own to mount into) — reset its RTK Query cache
    // between tests, or an earlier test's `{{temperature}}` response would
    // be served from cache here instead of hitting `fetchMock`.
    store.dispatch(systemVariablesApi.util.resetApiState());
    useListAllCameraChoicesQueryMock.mockReturnValue({
      data: { items: [], count: 0, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    });
    useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });
    fetchMock = vi.fn(async () => resolveResponse());
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('Issues no resolve request for text with no {{ (spec 148 US1 scenario 14)', async () => {
    // Fake timers with an explicit advance, not a real 400ms sleep: a
    // margin against the 250ms debounce that real time can erase under CI
    // load, and the assertion is meant to prove the `skip` guard — not the
    // clock (phase 6 should-fix 7).
    vi.useFakeTimers();
    try {
      renderDialog();

      fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: 'Line 1 static text' } });

      await act(async () => {
        await vi.advanceTimersByTimeAsync(DEBOUNCE_MS + 150);
      });

      expect(fetchMock).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
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

  /**
   * Phase 6 blocker 2. Before the `currentData` fix, RTK Query's `data`
   * kept the last successful result across the `skip` transition, so the
   * canvas box and the panel's resolved row both kept showing `23.4` after
   * the placeholder was cleared out of the text — the exact defect the
   * reviewer's probe demonstrated live.
   */
  it('Falls back to the raw text once the placeholder is cleared, not the previous resolve (phase 6 blocker 2)', async () => {
    renderDialog();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{temperature}}' } });
    await waitFor(() => {
      expect(screen.getByTestId('overlay-editor-preview').textContent).toBe('23.4');
    });

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: 'Line 1 static text' } });

    await waitFor(() => {
      expect(screen.getByTestId('overlay-editor-preview').textContent).toBe('Line 1 static text');
    });
    expect(screen.queryByTestId('placeholder-advisory-resolved')).toBeNull();
  });

  /**
   * Phase 6 blocker 3. Before the `settled` gate, `PlaceholderPreviewPanel`
   * received the live, still-being-typed text alongside a response for an
   * earlier version of it, so a second well-formed placeholder was flagged
   * "not a placeholder and will render literally" for the whole
   * debounce-plus-round-trip window — the reviewer's PROBE B.
   */
  it('Never flags a second, still-settling placeholder as malformed while the response is for the first one (phase 6 blocker 3)', async () => {
    renderDialog();

    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{temperature}}' } });
    // Real time to let the first resolve settle — fetch's own promise chain
    // and RTK Query's dispatch are native microtasks, not fake-timer
    // callbacks, so flushing them exactly under fake timers is fiddly. Fake
    // timers come in only for the part that needs exact control: the second
    // edit's mid-debounce window below.
    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(screen.getByTestId('placeholder-advisory-resolved')).not.toBeNull();
    });

    vi.useFakeTimers();
    try {
      // Mid-debounce: the text has moved on to a second well-formed
      // placeholder, but the settled value — and so the query's args and
      // its `currentData` — still name the first one.
      fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{pressure}}' } });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(DEBOUNCE_MS - 50);
      });

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(screen.queryByTestId('placeholder-advisory-malformed')).toBeNull();
      expect(screen.queryByTestId('placeholder-advisory-resolved')).toBeNull();
    } finally {
      vi.useRealTimers();
    }
  });

  /**
   * Should-fix 5. The panel's only claim is "advisory, never blocking"
   * (spec 148 decision 3, ADR-0115) — nothing about a failed or unresolved
   * reference may gate the submit button or alter what gets saved.
   */
  it('Never blocks submission on a failed resolve: Save as draft stays enabled and the draft is created with the text exactly as typed (should-fix 5, US1 scenario 15)', async () => {
    fetchMock = vi.fn(async () => failedResolveResponse());
    vi.stubGlobal('fetch', fetchMock);
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/name/i), 'Line-1 Title');
    fireEvent.change(screen.getByTestId('overlay-editor-text'), { target: { value: '{{bogus}}' } });

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(screen.getByTestId('placeholder-preview-error')).not.toBeNull();
    });

    const saveButton = screen.getByRole('button', { name: /save as draft/i });
    // `aria-disabled`, not native `disabled` (spec 160 FR-001); paired with
    // the click/call-count assertion below (already this test's own claim).
    expect(saveButton).not.toHaveAttribute('aria-disabled', 'true');

    await user.click(saveButton);

    expect(createDraftMock).toHaveBeenCalledTimes(1);
    const payload = (
      createDraftMock.mock.calls[0] as unknown as ReadonlyArray<{ name: string; label: { text: string } }>
    )[0]!;
    expect(payload.label.text).toBe('{{bogus}}');
  });
});
