// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import type { CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * Spec 479 (FR-003, SC-006). **What the samplers say, to whom, and how often.**
 *
 * <p>
 * Characterisation of the reporting seams of `CameraViewer` that the ten
 * covering files leave unobserved, written before the component is split:
 * the missing-stats-field line (once per field per mounted tile, shared by
 * both samplers), the sampler-failure reason, the `getToken` a failure report
 * is sent with, and which closure the lag sampler hands its figures to.
 * Every assertion is on an observable effect — a `[resilience]` or
 * `[latency]` console line, a call to the caller's `getToken` or
 * `onLagMeasured` — never on the source text.
 * </p>
 *
 * <p>
 * Lives in its own file: the ten covering `CameraViewer*.test.tsx` files must
 * pass unmodified through the split.
 * </p>
 */

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

const sessions: WhepClientDoubleOptions[] = [];

let statsBehaviour: () => unknown = () => Promise.reject(new Error('statsBehaviour not set for this case'));

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // Must report `connected`, or every effect (gated on `status !== 'live'`)
    // never runs and every assertion here is vacuous — the warning
    // CameraViewerAlignment.test.tsx carries, found the hard way there.
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

const FRAMES_PER_MS = 0.015;

/**
 * One inbound-video report, a pure function of elapsed time `t` (never of call
 * count). `omit` drops counters from the report; `frozen` pins counters at
 * their value for `t = 0`, so a delta over them is zero.
 */
function reportAt(t: number, { omit = [], frozen = [] }: { omit?: string[]; frozen?: string[] } = {}) {
  const stat: Record<string, unknown> = {
    type: 'inbound-rtp',
    kind: 'video',
    jitterBufferDelay: 2.5 + 0.000015 * (frozen.includes('jitterBufferDelay') ? 0 : t),
    jitterBufferEmittedCount: 200 + FRAMES_PER_MS * (frozen.includes('jitterBufferEmittedCount') ? 0 : t),
    totalProcessingDelay: 1.25 + 0.0000075 * (frozen.includes('totalProcessingDelay') ? 0 : t),
    totalDecodeTime: 0.5 + 0.000015 * (frozen.includes('totalDecodeTime') ? 0 : t),
    framesDecoded: 200 + FRAMES_PER_MS * (frozen.includes('framesDecoded') ? 0 : t),
  };
  for (const field of omit) delete stat[field];
  return new Map<string, unknown>([['v', stat]]);
}

function statsDouble(options: { omit?: string[]; frozen?: string[] } = {}): () => Promise<Map<string, unknown>> {
  const epoch = Date.now();
  return vi.fn(() => Promise.resolve(reportAt(Date.now() - epoch, options)));
}

function viewer(props: Partial<CameraViewerProps> = {}) {
  return <CameraViewer cameraIdentifier="cam-42" getToken={GET_TOKEN} stream={STREAM} {...props} />;
}

const driveFakes = () =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });

const advance = (milliseconds: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });

describe('CameraViewer reporting seams (FR-003)', () => {
  let infoSpy: MockInstance<typeof console.info>;

  const resilienceLines = (transition: string) =>
    infoSpy.mock.calls
      .filter(
        (call) =>
          call[0] === '[resilience]' && (call[1] as { transition?: unknown } | undefined)?.transition === transition,
      )
      .map((call) => call[1] as Record<string, unknown>);

  const latencyLines = (measurement: string) =>
    infoSpy.mock.calls
      .filter(
        (call) =>
          call[0] === '[latency]' && (call[1] as { measurement?: unknown } | undefined)?.measurement === measurement,
      )
      .map((call) => call[1] as Record<string, unknown>);

  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => ({ ok: true, status: 202 })),
    );
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  describe('a counter the browser does not report', () => {
    it('Names a counter the decode sampler cannot read, once, with the camera that noticed', async () => {
      statsBehaviour = statsDouble({ omit: ['totalDecodeTime'] });
      render(viewer());

      await advance(20_000);

      expect(statsBehaviour, 'the decode sampler must actually have read').toHaveBeenCalled();
      expect(resilienceLines('stats-field-missing')).toEqual([
        {
          subsystem: 'stream',
          transition: 'stats-field-missing',
          cameraIdentifier: 'cam-42',
          field: 'totalDecodeTime',
        },
      ]);
      expect(latencyLines('receive_to_decoded'), 'the unreadable sample is still dropped').toHaveLength(0);
    });

    it('Names a counter only the lag sampler needs, once, with the camera that noticed', async () => {
      statsBehaviour = statsDouble({ omit: ['jitterBufferDelay'] });
      render(viewer({ onLagMeasured: vi.fn() }));

      await advance(20_000);

      expect(resilienceLines('stats-field-missing')).toEqual([
        {
          subsystem: 'stream',
          transition: 'stats-field-missing',
          cameraIdentifier: 'cam-42',
          field: 'jitterBufferDelay',
        },
      ]);
      expect(latencyLines('presentation_buffer'), 'the unreadable sample is still dropped').toHaveLength(0);
      expect(
        latencyLines('receive_to_decoded').length,
        'the decode sampler reads the same report fine and keeps reporting',
      ).toBeGreaterThan(0);
    });

    it('Says so once for a counter both samplers read, not once from each', async () => {
      statsBehaviour = statsDouble({ omit: ['totalProcessingDelay'] });
      render(viewer({ onLagMeasured: vi.fn() }));

      await advance(20_000);

      // 2 000 ms and 5 000 ms ticks both read the same report many times over.
      expect((statsBehaviour as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(10);
      expect(resilienceLines('stats-field-missing')).toHaveLength(1);
      expect(resilienceLines('stats-field-missing')[0]).toMatchObject({ field: 'totalProcessingDelay' });
    });

    it('Says so separately for each distinct counter, once each', async () => {
      statsBehaviour = statsDouble({ omit: ['totalDecodeTime', 'jitterBufferDelay'] });
      render(viewer({ onLagMeasured: vi.fn() }));

      await advance(20_000);

      const fields = resilienceLines('stats-field-missing').map((line) => line['field']);
      expect(fields.slice().sort()).toEqual(['jitterBufferDelay', 'totalDecodeTime']);
    });

    it('Reports the camera showing in the tile when it noticed, not the one the tile was mounted with', async () => {
      statsBehaviour = statsDouble({ omit: ['totalDecodeTime'] });
      const { rerender } = render(viewer());
      await advance(1_000);

      // A layout revision puts a different camera in this tile before the
      // first decode tick (5 000 ms) has read anything.
      rerender(viewer({ cameraIdentifier: 'cam-43', stream: OTHER_STREAM }));
      await advance(10_000);

      const lines = resilienceLines('stats-field-missing');
      expect(lines).toHaveLength(1);
      expect(lines[0]).toMatchObject({ cameraIdentifier: 'cam-43', field: 'totalDecodeTime' });
    });
  });

  describe('a sampler that throws', () => {
    it('Reports a thrown Error by its message', async () => {
      statsBehaviour = () => Promise.reject(new Error('getStats exploded'));
      render(viewer());

      await advance(5_000);

      expect(resilienceLines('decode-sampler-failed')).toEqual([
        {
          subsystem: 'stream',
          transition: 'decode-sampler-failed',
          cameraIdentifier: 'cam-42',
          count: 1,
          reason: 'getStats exploded',
        },
      ]);
    });

    it('Reports a thrown non-Error by its string form', async () => {
      statsBehaviour = () => Promise.reject('plain string failure');
      render(viewer());

      await advance(5_000);

      const lines = resilienceLines('decode-sampler-failed');
      expect(lines).toHaveLength(1);
      expect(lines[0]).toMatchObject({ count: 1, reason: 'plain string failure' });
    });

    it('Sends the failure count with the getToken the tile has now, not the one it was mounted with', async () => {
      statsBehaviour = () => Promise.reject(new Error('getStats exploded'));
      const mountedWith = vi.fn(() => Promise.resolve('token-at-mount'));
      const current = vi.fn(() => Promise.resolve('token-now'));

      const { rerender } = render(viewer({ getToken: mountedWith }));
      await driveFakes();
      rerender(viewer({ getToken: current }));
      await advance(5_000);

      expect(resilienceLines('decode-sampler-failed'), 'the decode sampler must actually have failed').toHaveLength(1);
      expect(current, 'the failure report is sent with the current getToken').toHaveBeenCalled();
      expect(mountedWith, 'and never with a getToken the tile no longer holds').not.toHaveBeenCalled();
    });
  });

  describe('what the lag sampler reports', () => {
    it('Hands its figures to the callback of the latest render, never to an earlier one', async () => {
      statsBehaviour = statsDouble();
      const first = vi.fn();
      const second = vi.fn();

      const { rerender } = render(viewer({ onLagMeasured: first }));
      await driveFakes();
      rerender(viewer({ onLagMeasured: second }));
      // Tick at 2 000 seeds, tick at 4 000 reports.
      await advance(4_000);

      expect(first, 'a closure the parent has replaced must not be called').not.toHaveBeenCalled();
      expect(second).toHaveBeenCalledTimes(1);
      const [camera, lag, buffer] = second.mock.calls[0]!;
      expect(camera).toBe('cam-42');
      // 30 frames per 2 000 ms tick: 1.0 ms buffered + 0.5 ms processing per frame.
      expect(lag).toBeCloseTo(1.5, 5);
      expect(buffer).toBeCloseTo(1.0, 5);
    });

    it('Keeps following the newest callback across several re-renders', async () => {
      statsBehaviour = statsDouble();
      const callbacks = [vi.fn(), vi.fn(), vi.fn()];

      const { rerender } = render(viewer({ onLagMeasured: callbacks[0] }));
      await driveFakes();
      rerender(viewer({ onLagMeasured: callbacks[1] }));
      await advance(1_000);
      rerender(viewer({ onLagMeasured: callbacks[2] }));
      await advance(3_000);

      expect(callbacks[0]).not.toHaveBeenCalled();
      expect(callbacks[1]).not.toHaveBeenCalled();
      expect(callbacks[2]).toHaveBeenCalledTimes(1);
    });

    it('Does not call the callback when no frames were decoded between the samples, but still reports the buffer', async () => {
      // `framesDecoded` frozen: the lag (buffer + processing, per decoded frame)
      // has nothing to say; the buffer figure, per emitted frame, still does.
      statsBehaviour = statsDouble({ frozen: ['framesDecoded'] });
      const onLagMeasured = vi.fn();
      render(viewer({ onLagMeasured }));

      await advance(10_000);

      expect(statsBehaviour, 'the lag sampler must actually have read').toHaveBeenCalled();
      expect(onLagMeasured).not.toHaveBeenCalled();
      expect(latencyLines('presentation_buffer').length).toBeGreaterThan(0);
    });
  });

  describe('the decode sampler with nothing to measure', () => {
    it('Reports no receive_to_decoded figure while no frames are being decoded', async () => {
      statsBehaviour = statsDouble({ frozen: ['framesDecoded'] });
      render(viewer());

      await advance(20_000);

      expect(statsBehaviour, 'the decode sampler must actually have read').toHaveBeenCalled();
      expect(latencyLines('receive_to_decoded')).toHaveLength(0);
      expect(resilienceLines('decode-sampler-failed')).toHaveLength(0);
    });
  });
});
