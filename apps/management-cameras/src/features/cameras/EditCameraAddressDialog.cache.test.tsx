import { describe, it, expect, vi, afterEach } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { createApiStore } from '@smart-sentinel-eye/shared/store';

// gateway.ts resolves the API origin at module load; stub it before the dynamic imports below.
vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test');

const { EditCameraAddressDialog } = await import('./EditCameraAddressDialog.js');
const { camerasApi } = await import('@smart-sentinel-eye/shared/api/cameras.api');
const { streamsApi } = await import('@smart-sentinel-eye/shared/api/streams.api');

const CAMERA = '11111111-1111-1111-1111-111111111111';

const isAddressRead = (request: Request) =>
  request.method === 'GET' && new URL(request.url).pathname.endsWith(`/cameras/${CAMERA}/address`);

/**
 * Spec 438: the address is secret-bearing and its read is audited, so it must live only while the dialog
 * is open: no shared tag for other camera traffic to invalidate it by, and no retention once it closes.
 */
describe('EditCameraAddressDialog address cache (spec 438)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function setup() {
    const fetchMock = vi.fn((_request: Request) =>
      Promise.resolve(
        new Response(JSON.stringify({ rtspUrl: 'rtsp://10.0.5.12/h264?token=X', version: 12 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
    const store = createApiStore([camerasApi, streamsApi]);
    const addressReads = () => fetchMock.mock.calls.filter(([request]) => isAddressRead(request));
    const ui = (open: boolean) => (
      <Provider store={store}>
        <EditCameraAddressDialog open={open} onOpenChange={vi.fn()} cameraIdentifier={CAMERA} />
      </Provider>
    );
    const view = render(ui(true));
    const cacheStatus = () =>
      camerasApi.endpoints.getCameraAddress.select({ cameraIdentifier: CAMERA })(store.getState()).status;
    return { store, addressReads, cacheStatus, setOpen: (open: boolean) => view.rerender(ui(open)) };
  }

  it('Is not requested again when the Camera tag is invalidated while the dialog is open', async () => {
    const { store, addressReads } = setup();
    await waitFor(() => expect(addressReads()).toHaveLength(1));

    await act(async () => {
      store.dispatch(
        camerasApi.util.invalidateTags(['Camera', { type: 'Camera', id: CAMERA }, { type: 'Camera', id: 'LIST' }]),
      );
      // Let any refetch the invalidation triggers reach fetch before asserting it did not.
      await new Promise((resolve) => setTimeout(resolve, 0));
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(addressReads()).toHaveLength(1);
  });

  it('Leaves no cache entry once the dialog closes', async () => {
    const { addressReads, cacheStatus, setOpen } = setup();
    await waitFor(() => expect(addressReads()).toHaveLength(1));
    await waitFor(() => expect(cacheStatus()).toBe('fulfilled'));

    setOpen(false);

    await waitFor(() => expect(cacheStatus()).toBe('uninitialized'));
  });

  it('Remounts the form with the new address when the cached read changes under an open dialog', async () => {
    const { store, addressReads } = setup();
    expect(await screen.findByLabelText(/rtsp url/i)).toHaveValue('rtsp://10.0.5.12/h264?token=X');
    expect(addressReads()).toHaveLength(1);

    await act(async () => {
      await store.dispatch(
        camerasApi.util.upsertQueryData(
          'getCameraAddress',
          { cameraIdentifier: CAMERA },
          { rtspUrl: 'rtsp://10.0.5.99/other?token=Y', version: 13 },
        ),
      );
    });

    // Save quotes the new version, so the field must show the address that version belongs to.
    await waitFor(() => expect(screen.getByLabelText(/rtsp url/i)).toHaveValue('rtsp://10.0.5.99/other?token=Y'));
  });
});
