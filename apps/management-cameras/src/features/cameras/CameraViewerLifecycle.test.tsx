import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen } from '@testing-library/react';

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

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

// CameraViewer no longer calls useGetStreamQuery itself (ADR-0169, #2846) — it
// takes the stream as a prop, so this is passed directly instead of mocked.
const STREAM = {
  cameraIdentifier: 'cam-42',
  state: 'Healthy' as const,
  whepUrl: 'http://sfu/whep/cam-42',
  transcodeMode: 'Passthrough' as const,
  lastSuccessAt: null,
  error: null,
};

describe('CameraViewer connection lifecycle', () => {
  beforeEach(() => {
    construct.mockClear();
    connect.mockClear();
    close.mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Does not renegotiate the peer connection when the getToken closure changes between renders, and the session can still reach the fresh token', async () => {
    const { rerender } = render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token-a')} stream={STREAM} />,
    );
    expect(construct).toHaveBeenCalledTimes(1);
    const options = construct.mock.calls[0]![0] as { getToken: () => Promise<string | null> };
    await expect(options.getToken()).resolves.toBe('token-a');

    // Callers pass a fresh inline getToken on every render; that alone must
    // not tear down and rebuild the RTCPeerConnection (the live-video path).
    rerender(<CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token-b')} stream={STREAM} />);
    expect(construct).toHaveBeenCalledTimes(1);
    expect(close).not.toHaveBeenCalled();
    // The session was NOT rebuilt — and it can still reach the new token.
    await expect(options.getToken()).resolves.toBe('token-b');
  });

  it('Reconnects automatically with a fresh WhepClient after the peer connection fails', () => {
    vi.useFakeTimers();
    render(<CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} stream={STREAM} />);
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
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} stream={STREAM} />,
    );

    unmount();

    expect(close).toHaveBeenCalledTimes(1);
  });
});
