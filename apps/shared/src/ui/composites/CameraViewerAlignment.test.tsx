// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Spec 045 T024 / FR-013. **Alignment must never cost a picture.**
 *
 * <p>
 * The wall gives up its claim, never the video. An observer — or a controller —
 * that can break the thing it manages is worse than not having one, which is
 * the rule spec 040 set for the decode instrument and which holds here for a
 * component that actively writes to the receiver.
 * </p>
 */

const useGetStreamQueryMock = vi.fn();

vi.mock('@smart-sentinel-eye/shared/api/streams.api', () => ({
  useGetStreamQuery: (...args: unknown[]) => useGetStreamQueryMock(...args),
}));

const statsThrows = vi.fn(() => {
  throw new Error('getStats exploded');
});
const setPlayoutTargetThrows = vi.fn(() => {
  throw new Error('receiver refused the target');
});

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    async connect(videoEl: HTMLVideoElement) {
      // A real session attaches a stream; the point of this suite is that the
      // element survives everything the controller does around it.
      videoEl.dataset['connected'] = 'true';
    }
    close() {}
    stats = statsThrows;
    setPlayoutTarget = setPlayoutTargetThrows;
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

describe('CameraViewer when alignment fails', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    statsThrows.mockClear();
    setPlayoutTargetThrows.mockClear();
    useGetStreamQueryMock.mockReturnValue({
      data: { state: 'Healthy', whepUrl: 'http://sfu/whep/cam-42', error: null },
      isLoading: false,
      error: undefined,
    });
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it('Keeps showing video when reading the tile lag throws', async () => {
    const { container } = render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={() => {}} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // The sampler ran and failed repeatedly; the picture is still there.
    expect(container.querySelector('video')).not.toBeNull();
  });

  it('Keeps showing video when the receiver refuses a playout target', async () => {
    const { container } = render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        playoutTargetMilliseconds={120}
        onLagMeasured={() => {}}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    expect(container.querySelector('video')).not.toBeNull();
  });

  /**
   * A wall that has not converged, and a page with no wall at all, both pass
   * nothing — and neither is the same as a target of zero, which would jolt the
   * tile's playout to live and undo any alignment it had.
   */
  it('Writes no target at all when the wall has not decided one', async () => {
    render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        playoutTargetMilliseconds={null}
        onLagMeasured={() => {}}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    expect(setPlayoutTargetThrows).not.toHaveBeenCalled();
  });
});
