// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';
import type { StreamHealth } from '@smart-sentinel-eye/shared/api/streams.api';
import type { CameraViewerProps } from '@smart-sentinel-eye/shared/ui/composites/CameraViewer';

/**
 * Spec 479 (FR-003). **Applying the wall's playout target, and which answers
 * are reported.**
 *
 * <p>
 * `setPlayoutTarget` answers one of four outcomes. `CameraViewer` reports two
 * of them — `unsupported` and `refused` — once per mounted tile, treats a
 * throw as `refused`, and says nothing about `not-connected` (the transient
 * every tile passes through on its way up) or `applied`. It also applies
 * nothing until the tile is live. None of that was observed by the covering
 * files; this one pins it through the double's recorded calls and the
 * `[resilience]` console line, never through the source.
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

// When false, `connect()` leaves the tile on `connecting`.
let autoConnect = true;

type PlayoutBehaviour = () => 'applied' | 'unsupported' | 'refused' | 'not-connected';
let playoutBehaviour: PlayoutBehaviour = () => 'applied';
const playoutCalls: number[] = [];

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
      if (autoConnect) this.options.onConnectionStateChange?.('connected');
    }
    close() {}
    stats = () => Promise.resolve(new Map<string, unknown>());
    setPlayoutTarget = (milliseconds: number) => {
      playoutCalls.push(milliseconds);
      return playoutBehaviour();
    };
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

function viewer(props: Partial<CameraViewerProps> = {}) {
  return (
    <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} stream={STREAM} {...props} />
  );
}

const driveFakes = () =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(0);
  });

describe('CameraViewer playout target (FR-003)', () => {
  let infoSpy: MockInstance<typeof console.info>;

  const unsupportedLines = () =>
    infoSpy.mock.calls.filter(
      (call) =>
        call[0] === '[resilience]' &&
        (call[1] as { transition?: unknown } | undefined)?.transition === 'playout-target-unsupported',
    );

  beforeEach(() => {
    vi.useFakeTimers();
    sessions.length = 0;
    playoutCalls.length = 0;
    autoConnect = true;
    playoutBehaviour = () => 'applied';
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('Applies the target to a live tile and says nothing when the engine takes it', async () => {
    render(viewer({ playoutTargetMilliseconds: 400 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400]);
    expect(unsupportedLines()).toHaveLength(0);
  });

  it('Applies nothing while the tile is not yet live, then applies the target once it is', async () => {
    autoConnect = false;
    render(viewer({ playoutTargetMilliseconds: 400 }));
    await driveFakes();

    expect(sessions, 'the tile must have a session that has not connected').toHaveLength(1);
    expect(playoutCalls, 'a tile that is not live is left alone').toEqual([]);
    expect(unsupportedLines(), 'and nothing is blamed on the engine yet').toHaveLength(0);

    await act(async () => {
      sessions[0]!.onConnectionStateChange?.('connected');
    });

    expect(playoutCalls).toEqual([400]);
  });

  it.each([
    ['undefined', undefined],
    ['null', null],
  ])('Leaves a live tile alone when the target is %s', async (_name, target) => {
    render(viewer({ playoutTargetMilliseconds: target }));
    await driveFakes();

    expect(sessions).toHaveLength(1);
    expect(playoutCalls).toEqual([]);
    expect(unsupportedLines()).toHaveLength(0);
  });

  it('Applies a target of zero — which is not the same as no target', async () => {
    render(viewer({ playoutTargetMilliseconds: 0 }));
    await driveFakes();

    expect(playoutCalls).toEqual([0]);
  });

  it('Reports an engine that carries no jitterBufferTarget, once', async () => {
    playoutBehaviour = () => 'unsupported';
    const { rerender } = render(viewer({ playoutTargetMilliseconds: 400 }));
    await driveFakes();

    expect(unsupportedLines()).toHaveLength(1);
    expect(unsupportedLines()[0]![1]).toMatchObject({ subsystem: 'stream', cameraIdentifier: 'cam-42' });

    // The wall moves the target: it is applied again, but the permanent fact
    // about the engine has been said.
    rerender(viewer({ playoutTargetMilliseconds: 500 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400, 500]);
    expect(unsupportedLines()).toHaveLength(1);
  });

  it('Reports an engine that refuses every assignment, once', async () => {
    playoutBehaviour = () => 'refused';
    const { rerender } = render(viewer({ playoutTargetMilliseconds: 400 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400]);
    expect(unsupportedLines()).toHaveLength(1);

    rerender(viewer({ playoutTargetMilliseconds: 500 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400, 500]);
    expect(unsupportedLines()).toHaveLength(1);
  });

  it('Treats a throw from setPlayoutTarget as a refusal, reports it once, and keeps the picture', async () => {
    playoutBehaviour = () => {
      throw new Error('getReceivers is not a function');
    };
    const { container, rerender } = render(viewer({ playoutTargetMilliseconds: 400 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400]);
    expect(unsupportedLines()).toHaveLength(1);
    expect(container.querySelector('video'), 'a tile that cannot be aligned carries on showing video').not.toBeNull();

    rerender(viewer({ playoutTargetMilliseconds: 500 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400, 500]);
    expect(unsupportedLines()).toHaveLength(1);
  });

  it('Does not report not-connected, the transient every tile passes through', async () => {
    playoutBehaviour = () => 'not-connected';
    render(viewer({ playoutTargetMilliseconds: 400 }));
    await driveFakes();

    expect(playoutCalls).toEqual([400]);
    expect(unsupportedLines()).toHaveLength(0);
  });
});
