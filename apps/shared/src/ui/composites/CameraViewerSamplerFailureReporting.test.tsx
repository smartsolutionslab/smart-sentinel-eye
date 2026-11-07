// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import type { CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * Spec 483 T003. **How a throwing sampler is counted and reported**, through the real `CameraViewer`.
 *
 * <p>
 * Two behaviours the other `CameraViewer*` files leave unobserved: that a camera swap on a mounted tile restarts neither failure
 * counter (the line "count 1" is not said a second time for the second camera), and that the decode and lag samplers share ONE
 * `sampler_failed` throttle window on the tile (both are told to the console, only the first reaches the sink inside the window).
 * Every assertion is on an observable effect, a `[resilience]` console line or a POST to the sink. Green on today's code.
 * </p>
 */

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

const sessions: WhepClientDoubleOptions[] = [];

let statsBehaviour: () => unknown = () => Promise.reject(new Error('statsBehaviour not set for this case'));

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // Must report `connected`, or every effect (gated on `status !== 'live'`) never runs and every assertion here is vacuous.
    constructor(private readonly options: WhepClientDoubleOptions) {
      sessions.push(options);
    }

    async connect(videoEl: HTMLVideoElement) {
      videoEl.dataset['connected'] = 'true';
      this.options.onConnectionStateChange?.('connected');
    }
    close() {}
    stats = () => statsBehaviour();
    setPlayoutTarget = () => 'applied' as const;
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

const STREAM: StreamHealth = {
  cameraIdentifier: 'cam-42',
  state: 'Healthy',
  whepUrl: 'http://sfu/whep/cam-42',
  transcodeMode: 'Passthrough',
  lastSuccessAt: null,
  error: null,
};

const OTHER_STREAM: StreamHealth = {
  ...STREAM,
  cameraIdentifier: 'cam-43',
  whepUrl: 'http://sfu/whep/cam-43',
};

const GET_TOKEN = () => Promise.resolve('token');

function viewer(props: Partial<CameraViewerProps> = {}) {
  return (
    <CameraViewer cameraIdentifier="cam-42" getToken={GET_TOKEN} stream={STREAM} onLagMeasured={() => {}} {...props} />
  );
}

const advance = (milliseconds: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });

describe('CameraViewer sampler failure reporting (spec 483)', () => {
  let infoSpy: MockInstance<typeof console.info>;

  const counts = (transition: string) =>
    infoSpy.mock.calls
      .filter(
        (call) =>
          call[0] === '[resilience]' && (call[1] as { transition?: unknown } | undefined)?.transition === transition,
      )
      .map((call) => (call[1] as { count: unknown }).count);

  const samplerFailedPosts = () =>
    vi
      .mocked(fetch)
      .mock.calls.map(
        ([, init]) => JSON.parse(String(init?.body)) as { measurement: string; elapsedMilliseconds: number },
      )
      .filter((body) => body.measurement === 'sampler_failed')
      .map((body) => body.elapsedMilliseconds);

  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(null, { status: 202 }))),
    );
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('keeps counting across a camera change in the tile: no second "count 1", and the lag count reaches 10', async () => {
    const stats = vi.fn(() => Promise.reject(new Error('stats exploded')));
    statsBehaviour = stats;
    const { rerender } = render(viewer());

    await advance(12_000);
    expect(counts('decode-sampler-failed'), 'the decode sampler must have failed and said so').toEqual([1]);
    expect(counts('lag-sampler-failed'), 'the lag sampler must have failed and said so').toEqual([1]);
    const callsBeforeSwap = stats.mock.calls.length;

    rerender(viewer({ cameraIdentifier: 'cam-43', stream: OTHER_STREAM }));
    await advance(12_000);

    expect(stats.mock.calls.length, 'the samplers must have kept running for the new camera').toBeGreaterThan(
      callsBeforeSwap,
    );
    // The lag sampler ticks every 2 s: six failures before the swap, counts 7 to 10 after it. Reaching "10" is only possible if the
    // counter carried across; a restart at the swap would say "1" again and never get there.
    expect(counts('decode-sampler-failed')).toEqual([1]);
    expect(counts('lag-sampler-failed')).toEqual([1, 10]);
  });

  it('tells the console about both samplers but the sink about only the first inside one throttle window', async () => {
    statsBehaviour = () => Promise.reject(new Error('stats exploded'));
    render(viewer());

    await advance(12_000);

    expect(counts('lag-sampler-failed'), 'the lag sampler must have failed and said so').toEqual([1]);
    expect(counts('decode-sampler-failed'), 'the decode sampler must have failed and said so').toEqual([1]);
    expect(samplerFailedPosts(), 'one POST for the two failures: they share the tile throttle').toEqual([1]);
  });
});
