import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { store } from '../../app/store.js';

/**
 * Spec 305 (#2350) US2, plan.md §"Create page". `OverlayCreatePage` does not
 * exist yet (T010), so every test below fails on a missing module — a real
 * RED (ADR-0139/0144) — not a wrong assertion against code that compiles.
 */

const createDraftMock = vi.fn(async () => ({ data: 'noop' }));
let createError: unknown = undefined;

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return {
    ...actual,
    useCreateOverlayDraftMutation: () => [
      createDraftMock,
      { isLoading: false, error: createError, reset: vi.fn(() => (createError = undefined)) },
    ],
    // The form's chain query stays `skipToken` outside edit mode — supplied
    // so the real hook is never reached by a component test (cameras/streams
    // mocks below do the same for the rest of OverlayEditor's wiring).
    useGetOverlayQuery: () => ({ currentData: undefined, isFetching: false, isError: false, refetch: vi.fn() }),
    useEditDraftOverlayRevisionMutation: () => [vi.fn(async () => ({ data: 1 })), { isLoading: false, error: undefined, reset: vi.fn() }],
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

beforeEach(() => {
  useListAllCameraChoicesQueryMock.mockReturnValue({
    data: { items: [], count: 0, complete: true },
    isLoading: false,
    isFetching: false,
    isError: false,
  });
  useGetStreamQueryMock.mockReturnValue({ data: undefined, isLoading: false, error: undefined });
  createDraftMock.mockClear();
  createError = undefined;
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const { OverlayCreatePage } = await import('./OverlayCreatePage.js');

function renderCreatePage() {
  const router = createMemoryRouter(
    [
      { path: '/overlays/new', element: <OverlayCreatePage /> },
      { path: '/overlays', element: <h1>Overlays list (stub)</h1> },
    ],
    { initialEntries: ['/overlays/new'] },
  );
  const result = render(
    <Provider store={store}>
      <RouterProvider router={router} />
    </Provider>,
  );
  return { router, ...result };
}

describe('OverlayCreatePage (spec 305, #2350, US2)', () => {
  it('Focuses the Name field on mount', () => {
    renderCreatePage();

    expect(screen.getByLabelText(/name/i)).toHaveFocus();
  });

  it('Navigates to /overlays after a successful Save as draft', async () => {
    const user = userEvent.setup();
    const { router } = renderCreatePage();

    await user.type(screen.getByLabelText(/name/i), 'Line-9 Title');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/overlays'));
    expect(createDraftMock).toHaveBeenCalledTimes(1);
  });

  it('Keeps the operator on /overlays/new with the existing message on an OVERLAY_NAME_TAKEN conflict', async () => {
    createError = { status: 409, data: { title: 'OVERLAY_NAME_TAKEN' } };
    const user = userEvent.setup();
    const { router } = renderCreatePage();

    await user.type(screen.getByLabelText(/name/i), 'Line-9 Title');
    await user.click(screen.getByRole('button', { name: /save as draft/i }));

    expect(await screen.findByTestId('chain-recovery-alert')).toHaveTextContent(/already taken/i);
    expect(router.state.location.pathname).toBe('/overlays/new');
  });

  it('Cancel navigates to /overlays and sends no POST', async () => {
    const user = userEvent.setup();
    const { router } = renderCreatePage();

    await user.click(screen.getByRole('button', { name: /cancel/i }));

    expect(createDraftMock).not.toHaveBeenCalled();
    await waitFor(() => expect(router.state.location.pathname).toBe('/overlays'));
  });
});
