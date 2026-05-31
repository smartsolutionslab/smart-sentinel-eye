import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

const construct = vi.fn();
const connect = vi.fn().mockResolvedValue(undefined);
const close = vi.fn();

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    constructor(options: unknown) {
      construct(options);
    }
    connect = connect;
    close = close;
  },
}));

vi.mock('@smart-sentinel-eye/shared/api/streams.api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@smart-sentinel-eye/shared/api/streams.api')>();
  return {
    ...actual,
    useGetStreamQuery: () => ({
      data: { state: 'Healthy', whepUrl: 'http://sfu/whep/cam-42', error: null },
      isLoading: false,
      error: undefined,
    }),
  };
});

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

describe('CameraViewer connection lifecycle', () => {
  beforeEach(() => {
    construct.mockClear();
    connect.mockClear();
    close.mockClear();
  });

  it('Does not renegotiate the peer connection when the getToken closure changes between renders', () => {
    const { rerender } = render(
      <Provider store={store}>
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token-a')} />
      </Provider>,
    );
    expect(construct).toHaveBeenCalledTimes(1);

    // Callers pass a fresh inline getToken on every render; that alone must
    // not tear down and rebuild the RTCPeerConnection (the live-video path).
    rerender(
      <Provider store={store}>
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token-b')} />
      </Provider>,
    );
    expect(construct).toHaveBeenCalledTimes(1);
    expect(close).not.toHaveBeenCalled();
  });
});
