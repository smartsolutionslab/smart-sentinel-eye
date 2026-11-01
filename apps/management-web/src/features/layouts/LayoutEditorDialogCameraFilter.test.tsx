import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const CAMERA_A = '11111111-1111-1111-1111-111111111111';

const CAMERA = {
  cameraIdentifier: CAMERA_A,
  name: 'Line 2 Furnace',
  rtspUrl: 'rtsp://10.0.5.12/h264',
  registeredAt: '2026-05-25T10:00:00Z',
};

/** What the camera-choices query answers, set per test. Held outside the mock factory so a test can change it. */
const cameraQuery = vi.hoisted(() => ({
  current: {} as {
    data?: { items: unknown[]; count: number; complete: boolean };
    isLoading: boolean;
    isFetching: boolean;
    isError: boolean;
  },
}));

vi.mock('@smart-sentinel-eye/shared/api/layouts.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/layouts.api')>();
  return {
    ...actual,
    useCreateLayoutDraftMutation: () => [vi.fn(async () => ({ data: 'noop' })), { isLoading: false, reset: vi.fn() }],
    useEditDraftRevisionMutation: () => [vi.fn(async () => ({ data: 2 })), { isLoading: false, reset: vi.fn() }],
    useGetLayoutQuery: () => ({
      data: undefined,
      currentData: undefined,
      isLoading: false,
      isError: false,
      isFetching: false,
      refetch: vi.fn(),
    }),
  };
});

vi.mock('@smart-sentinel-eye/shared/api/cameras.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/cameras.api')>();
  return { ...actual, useListAllCameraChoicesQuery: () => cameraQuery.current };
});

vi.mock('@smart-sentinel-eye/shared/api/overlays.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/overlays.api')>();
  return { ...actual, useListOverlaysQuery: () => ({ data: { chains: [], published: [] }, isLoading: false }) };
});

const { LayoutEditorDialog } = await import('./LayoutEditorDialog.js');

function ui(open: boolean) {
  return (
    <Provider store={store}>
      <LayoutEditorDialog open={open} onOpenChange={() => {}} />
    </Provider>
  );
}

const status = () => document.getElementById('layout-camera-filter-status')!;

/**
 * Spec 476 T003. Pins what the camera filter says and does, which no test did before the dialog was split into units:
 * every sentence except "N of M cameras match" (pinned in `…Retention`), the live region, the field's description, and
 * clearing the filter on close. Green on the unsplit dialog by design; it must stay green after.
 */
describe('LayoutEditorDialog — the camera filter says what it is doing', () => {
  beforeEach(() => {
    cameraQuery.current = {
      data: { items: [CAMERA], count: 1, complete: true },
      isLoading: false,
      isFetching: false,
      isError: false,
    };
  });

  it('Says it is searching while the typed fragment has not been asked of the query yet, then states the result', async () => {
    const user = userEvent.setup();
    render(ui(true));

    await user.type(screen.getByLabelText(/find a camera/i), 'fur');

    expect(status()).toHaveTextContent('Searching…');
    await vi.waitFor(() => expect(status()).toHaveTextContent('1 of 1 cameras match.'));
  });

  it('Says the list could not be loaded when the camera query failed', () => {
    cameraQuery.current = { data: undefined, isLoading: false, isFetching: false, isError: true };

    render(ui(true));

    expect(status()).toHaveTextContent('The camera list could not be loaded.');
  });

  it('States how many matches were found, with no total, when the walk is incomplete', async () => {
    const user = userEvent.setup();
    cameraQuery.current = {
      data: { items: [CAMERA], count: 2, complete: false },
      isLoading: false,
      isFetching: false,
      isError: false,
    };
    render(ui(true));

    await user.type(screen.getByLabelText(/find a camera/i), 'fur');

    await vi.waitFor(() => expect(status()).toHaveTextContent('1 matches so far — the list is incomplete.'));
  });

  it('Announces its status politely and describes the field with it', () => {
    render(ui(true));

    expect(status()).toHaveAttribute('aria-live', 'polite');
    expect(screen.getByLabelText(/find a camera/i)).toHaveAttribute('aria-describedby', 'layout-camera-filter-status');
  });

  it('Starts with an empty filter again after the dialog is closed and reopened', async () => {
    const user = userEvent.setup();
    const { rerender } = render(ui(true));
    await user.type(screen.getByLabelText(/find a camera/i), 'fur');
    expect(screen.getByLabelText(/find a camera/i)).toHaveValue('fur');

    rerender(ui(false));
    rerender(ui(true));

    expect(screen.getByLabelText(/find a camera/i)).toHaveValue('');
  });
});
