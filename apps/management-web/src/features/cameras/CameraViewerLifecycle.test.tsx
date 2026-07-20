import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';
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

  afterEach(() => {
    vi.useRealTimers();
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

  it('Reconnects automatically with a fresh WhepClient after the peer connection fails', () => {
    vi.useFakeTimers();
    render(
      <Provider store={store}>
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} />
      </Provider>,
    );
    expect(construct).toHaveBeenCalledTimes(1);
    const options = construct.mock.calls[0]![0] as {
      onConnectionStateChange?: (state: string) => void;
    };

    act(() => options.onConnectionStateChange?.('failed'));
    expect(screen.getByText('Reconnecting…')).toBeInTheDocument();

    // Backoff base is 1 s with ±20% jitter, so 1.2 s always covers it.
    act(() => {
      vi.advanceTimersByTime(1200);
    });

    expect(close).toHaveBeenCalledTimes(1);
    expect(construct).toHaveBeenCalledTimes(2);
  });

  it('Closes the WHEP session when the viewer unmounts', () => {
    const { unmount } = render(
      <Provider store={store}>
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} />
      </Provider>,
    );

    unmount();

    expect(close).toHaveBeenCalledTimes(1);
  });
});
