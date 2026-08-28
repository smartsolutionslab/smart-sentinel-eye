import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { Provider } from 'react-redux';
import { store } from '../../app/store.js';

/**
 * Spec 045 T019 / T025. **management-web has no wall, so it must never align.**
 *
 * <p>
 * It mounts the same shared `CameraViewer` a kiosk tile does, and shows one
 * camera at a time with nothing to align it against. The guarantee is that it
 * <b>passes no alignment props and therefore runs nothing</b> — no sampling
 * interval, and above all no `jitterBufferTarget` ever written to a receiver
 * that has no wall to be in step with.
 * </p>
 *
 * <p>
 * The server's `IsBrowserKiosk()` gate drops desktop measurements (#1893), but
 * that is the <b>backstop, not the design</b>. If these tests need it to pass,
 * the client is wrong.
 * </p>
 *
 * <p>
 * <b>Asserted as an absence of any write</b>, never as unchanged latency: a
 * controller that set a lone tile to its own measured lag would change nothing
 * observable and would still be wrong (FR-004).
 * </p>
 */

const setPlayoutTarget = vi.fn(() => true);
const stats = vi.fn(async () => new Map());

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    connect = vi.fn().mockResolvedValue(undefined);
    close = vi.fn();
    stats = stats;
    setPlayoutTarget = setPlayoutTarget;
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

describe('CameraViewer on a page with no wall', () => {
  beforeEach(() => {
    setPlayoutTarget.mockClear();
    stats.mockClear();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('Never writes a playout target when no wall asked for one', () => {
    render(
      <Provider store={store}>
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} />
      </Provider>,
    );

    act(() => {
      vi.advanceTimersByTime(30_000);
    });

    expect(setPlayoutTarget).not.toHaveBeenCalled();
  });

  it('Starts no lag sampling when nobody is listening for it', () => {
    render(
      <Provider store={store}>
        <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} />
      </Provider>,
    );

    const before = stats.mock.calls.length;
    act(() => {
      vi.advanceTimersByTime(30_000);
    });

    // The decode sampler (spec 040) still runs — it is not this feature's, and
    // silencing it would be a regression. What must not appear is the second,
    // faster alignment sampler on top of it: at a 2 s cadence over 30 s that
    // would be an order of magnitude more reads than the 5 s decode interval.
    const reads = stats.mock.calls.length - before;
    expect(reads).toBeLessThanOrEqual(6);
  });
});
