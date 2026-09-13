// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockInstance } from 'vitest';
import type { PlayoutTargetOutcome } from '@smart-sentinel-eye/shared/streaming/WhepClient';

/**
 * Spec 045 T024 / FR-013. **Alignment must never cost a picture.**
 *
 * <p>
 * The wall gives up its claim, never the video. An observer — or a controller —
 * that can break the thing it manages is worse than not having one, which is
 * the rule spec 040 set for the decode instrument and which holds here for a
 * component that actively writes to the receiver.
 * </p>
 *
 * <p>
 * <b>Spec 095 T004-T006 / FR-001…FR-004.</b> The same component, asked a
 * different question: when the instrument cannot read, does it say so? A wall
 * whose receiver omits one counter is byte-identical today to a wall that is
 * perfectly aligned — no lag reported, no skew computed, no tile badged, and no
 * line anywhere saying why (issue #2109 item 3). The playout half is the same
 * shape from the other end: nothing separates an engine that <em>cannot</em>
 * hold a target from a wall that has not converged <em>yet</em>, and the first
 * is permanent while the second happens on every startup (item 4).
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

// Spec 095: the double answers whatever the case under test installed, and
// `beforeEach` puts the throwing pair back. One mocked module exports one class,
// so a case needing a *reading* receiver — or one that refuses a target rather
// than throwing on it — swaps the behaviour rather than adding a second double.
let statsBehaviour: () => unknown = statsThrows;
let setPlayoutTargetBehaviour: (milliseconds: number) => PlayoutTargetOutcome = setPlayoutTargetThrows;

interface WhepClientDoubleOptions {
  onConnectionStateChange?: (state: string) => void;
}

// Every session the component built, in order. A flapping tile tears its client
// down and builds another, and a test claiming "it did not report twice" has to
// show that a second session really happened.
const sessions: WhepClientDoubleOptions[] = [];
const latestSession = (): WhepClientDoubleOptions => sessions[sessions.length - 1]!;

vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient', () => ({
  WhepClient: class {
    // **The double must report `connected`, or this whole suite is vacuous.**
    // Every effect in CameraViewer guards on `status !== 'live'`, and `status`
    // is driven only by this callback — never by `connect()` resolving. An
    // earlier version of this file omitted it, so the sampler and the actuator
    // were never reached and all three tests passed with both deleted. Which is
    // the exact failure the tests exist to prevent, committed into the tests
    // themselves.
    constructor(private readonly options: WhepClientDoubleOptions) {
      sessions.push(options);
    }

    async connect(videoEl: HTMLVideoElement) {
      videoEl.dataset['connected'] = 'true';
      this.options.onConnectionStateChange?.('connected');
    }
    close() {}
    stats = () => statsBehaviour();
    setPlayoutTarget = (milliseconds: number) => setPlayoutTargetBehaviour(milliseconds);
  },
}));

const { CameraViewer } = await import('@smart-sentinel-eye/shared/ui/composites/CameraViewer');

/**
 * A receiver statistics report carrying one inbound video stat, with the named
 * counters **absent from the object** rather than set to null. An engine that
 * does not implement a statistic omits the property; a null is a different
 * shape and exercises a different branch.
 */
function videoStatWithout(...absent: readonly string[]): Map<string, unknown> {
  const stat: Record<string, unknown> = {
    type: 'inbound-rtp',
    kind: 'video',
    jitterBufferDelay: 2.5,
    jitterBufferEmittedCount: 200,
    totalProcessingDelay: 1.25,
    totalDecodeTime: 0.5,
    framesDecoded: 200,
  };
  for (const field of absent) delete stat[field];
  return new Map<string, unknown>([['v', stat]]);
}

/** A session that has connected but is not producing video yet — every mount. */
const audioOnlyReport = (): Map<string, unknown> =>
  new Map<string, unknown>([['a', { type: 'inbound-rtp', kind: 'audio', framesDecoded: 200 }]]);

/**
 * Issue #2189. A receiver statistics report whose lag-relevant counters
 * **advance on every call**, unlike {@link videoStatWithout}'s constants.
 *
 * <p>
 * `lagBetween` and `bufferDelayBetween` answer null unless
 * `jitterBufferEmittedCount` and `framesDecoded` increase between samples and
 * the two delay totals do not go backwards (`wallAlignment.ts:169-179,
 * 201-209`) — needed so `onLagMeasured` is ever reached at all. A case built
 * on the constant fixture would leave `onLagMeasured` unreached and every
 * assertion about it vacuously true, which is exactly the failure mode this
 * file's other doubles already guard against.
 * </p>
 */
function advancingVideoStat(): () => Promise<Map<string, unknown>> {
  let framesDecoded = 200;
  let jitterBufferEmittedCount = 200;
  let jitterBufferDelay = 2.5;
  let totalProcessingDelay = 1.25;
  return () => {
    framesDecoded += 30;
    jitterBufferEmittedCount += 30;
    jitterBufferDelay += 0.05;
    totalProcessingDelay += 0.02;
    return Promise.resolve(
      new Map<string, unknown>([
        [
          'v',
          {
            type: 'inbound-rtp',
            kind: 'video',
            jitterBufferDelay,
            jitterBufferEmittedCount,
            totalProcessingDelay,
            totalDecodeTime: 0.5,
            framesDecoded,
          },
        ],
      ]),
    );
  };
}

describe('CameraViewer when alignment fails', () => {
  let infoSpy: MockInstance<typeof console.info>;

  /**
   * The `[resilience]` lines carrying one transition. Nothing else is counted:
   * `useWhepSession` files every status change down the same channel.
   */
  const resilienceLines = (transition: string) =>
    infoSpy.mock.calls.filter(
      (call) =>
        call[0] === '[resilience]' && (call[1] as { transition?: unknown } | undefined)?.transition === transition,
    );

  /**
   * `connected → disconnected → connected`, through the real state machine: the
   * 5 s disconnect grace, then the first jittered retry, which tears the client
   * down and builds another that reports `connected` from `connect()`.
   */
  const flapThroughReconnect = async () => {
    act(() => {
      latestSession().onConnectionStateChange?.('disconnected');
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });
  };

  beforeEach(() => {
    vi.useFakeTimers();
    statsThrows.mockClear();
    setPlayoutTargetThrows.mockClear();
    statsBehaviour = statsThrows;
    setPlayoutTargetBehaviour = setPlayoutTargetThrows;
    sessions.length = 0;
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {});
    useGetStreamQueryMock.mockReturnValue({
      data: { state: 'Healthy', whepUrl: 'http://sfu/whep/cam-42', error: null },
      isLoading: false,
      error: undefined,
    });
  });

  afterEach(() => {
    cleanup();
    infoSpy.mockRestore();
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
    // Asserted BEFORE the survival check: if the throwing double was never
    // reached, "the video survived" is true of a component that did nothing.
    expect(statsThrows, 'the lag sampler must actually have run').toHaveBeenCalled();
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
    expect(setPlayoutTargetThrows, 'the actuator must actually have run').toHaveBeenCalled();
    // Asserted BEFORE the survival check: if the throwing double was never
    // reached, "the video survived" is true of a component that did nothing.
    expect(statsThrows, 'the lag sampler must actually have run').toHaveBeenCalled();
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

  /**
   * Spec 095 T004 / FR-001, FR-003. One line, not fourteen.
   *
   * <p>
   * A browser does not grow a statistics field halfway through a session, so a
   * counter that is absent is absent for the session's life. The lag sampler
   * ticks ten times in twenty seconds and the decode sampler four, over the
   * same stat — and this is one fact about the engine, not fourteen dropped
   * events.
   * </p>
   */
  it('Names a statistics counter its receiver does not report, once for the session', async () => {
    const statsMissingField = vi.fn(() => Promise.resolve(videoStatWithout('totalProcessingDelay')));
    statsBehaviour = statsMissingField;
    const onLagMeasured = vi.fn();

    const { container } = render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        onLagMeasured={onLagMeasured}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // Asserted BEFORE the outcome. This suite has already once passed with the
    // code under test deleted — see the double's own comment — and "one line
    // was logged" would otherwise be judged against a component that never
    // sampled.
    expect(statsMissingField, 'the lag sampler must actually have run').toHaveBeenCalled();
    expect(statsMissingField.mock.calls.length, 'the samplers must have ticked more than once').toBeGreaterThan(1);

    const named = resilienceLines('stats-field-missing');
    expect(named).toHaveLength(1);
    expect(named[0]![1]).toEqual({
      subsystem: 'stream',
      transition: 'stats-field-missing',
      cameraIdentifier: 'cam-42',
      field: 'totalProcessingDelay',
    });
    // The sample is still dropped and the picture is still there: the silence
    // is what changed, not the behaviour (FR-006).
    expect(onLagMeasured).not.toHaveBeenCalled();
    expect(container.querySelector('video')).not.toBeNull();
  });

  /**
   * Spec 095 T004 case 2 / FR-002. **Absent is not the same as not-yet.**
   *
   * <p>
   * A report with no inbound video stat at all is a session that has not
   * started producing. It happens on every mount, and reporting it would put a
   * line on the console every two seconds for the normal case. Green before the
   * fix by construction — this one bounds the fix rather than establishing
   * missing behaviour.
   * </p>
   */
  it('Says nothing about a session that is not producing video yet', async () => {
    const statsWithoutVideo = vi.fn(() => Promise.resolve(audioOnlyReport()));
    statsBehaviour = statsWithoutVideo;

    render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={() => {}} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    expect(statsWithoutVideo, 'the lag sampler must actually have run').toHaveBeenCalled();
    expect(resilienceLines('stats-field-missing')).toHaveLength(0);
  });

  /**
   * Spec 095 T005 / FR-004. **The controller reports what it never actuated.**
   *
   * <p>
   * `setPlayoutTarget` answers false when no video receiver carries
   * `jitterBufferTarget` — Firefox, Safari, pre-115 Chromium. The boolean is
   * discarded today, so a wall on such an engine shows a spread that never
   * closes and nothing anywhere says the actuator is not connected. Distinct
   * from the throwing double above: this receiver is reached, and refuses.
   * </p>
   */
  it('Says so when the receiver cannot hold a playout target at all', async () => {
    const setPlayoutTargetRefuses = vi.fn((): PlayoutTargetOutcome => 'unsupported');
    setPlayoutTargetBehaviour = setPlayoutTargetRefuses;

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

    expect(setPlayoutTargetRefuses, 'the actuator must actually have run').toHaveBeenCalledWith(120);

    const unsupported = resilienceLines('playout-target-unsupported');
    expect(unsupported).toHaveLength(1);
    expect(unsupported[0]![1]).toEqual({
      subsystem: 'stream',
      transition: 'playout-target-unsupported',
      cameraIdentifier: 'cam-42',
    });
    // Spec 045 FR-013: a tile that cannot be aligned still shows video.
    expect(container.querySelector('video')).not.toBeNull();
  });

  /**
   * Spec 095 FR-004, the quiet half. **An engine that can hold a target is not
   * told it cannot.**
   *
   * <p>
   * The twin of *"Says nothing about a session that is not producing video
   * yet"*, on the actuator rather than the sampler — and the case that makes
   * the new signal's most damaging failure mode fail loudly rather than
   * quietly. Every other `setPlayoutTarget` double in the tree throws, refuses,
   * or is never reached, so without this case the `!applied` half of the guard
   * could be dropped and every tile on every supported browser would
   * permanently claim its target unsupported — with the whole suite still
   * green. Found in code review.
   * </p>
   */
  it('Says nothing about a receiver that holds the playout target', async () => {
    const setPlayoutTargetApplies = vi.fn((): PlayoutTargetOutcome => 'applied');
    setPlayoutTargetBehaviour = setPlayoutTargetApplies;

    render(
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

    // Asserted BEFORE the silence: an actuator that was never reached is silent
    // too, and would make this case true of a component that did nothing.
    expect(setPlayoutTargetApplies, 'the actuator must actually have run').toHaveBeenCalledWith(120);
    expect(resilienceLines('playout-target-unsupported')).toHaveLength(0);
  });

  /**
   * Spec 142 T005 / FR-007, FR-010. **The residual spec 095 recorded, closed
   * from the call site.** `setPlayoutTarget` reported `false` for at least
   * four different real causes (#2198 item 2), and this call site could not
   * tell "still connecting" from "cannot align" — a null `jitterBufferTarget`
   * fresh out of `connect()` was indistinguishable from an engine that will
   * never carry one. `false` is the only spelling a double in this tree could
   * give "not connected" before the tri-state exists (every other
   * `setPlayoutTarget` double here answers `true` or `false` too); the point
   * of this case is that once `CameraViewer.tsx` stops treating every falsy
   * answer alike, this same answer must stop being reported.
   */
  it('Says nothing when the actuator reports it is not connected (#2198)', async () => {
    const setPlayoutTargetNotConnected = vi.fn((): PlayoutTargetOutcome => 'not-connected');
    setPlayoutTargetBehaviour = setPlayoutTargetNotConnected;

    render(
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

    expect(setPlayoutTargetNotConnected, 'the actuator must actually have run').toHaveBeenCalledWith(120);
    expect(resilienceLines('playout-target-unsupported')).toHaveLength(0);
  });

  /**
   * Spec 095 T006 / FR-003, plan risk R3. **A flap is not a new engine.**
   *
   * <p>
   * The samplers are keyed on `status`, so a tile that goes
   * `live → reconnecting → live` tears its effects down and rebuilds them. A
   * once-per-session guard living inside an effect closure would be reset by
   * every recovery, and a fab wall reconnecting through the night would log the
   * same permanent fact hundreds of times.
   * </p>
   */
  it('Names a missing statistics counter once across a session that flaps', async () => {
    const statsMissingField = vi.fn(() => Promise.resolve(videoStatWithout('totalProcessingDelay')));
    statsBehaviour = statsMissingField;

    render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={() => {}} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });
    const sampledBeforeFlap = statsMissingField.mock.calls.length;

    await flapThroughReconnect();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // Asserted BEFORE the count: a flap that never rebuilt the session, or a
    // second live window that never sampled, makes "still one line" true of a
    // component that did nothing the second time.
    expect(sessions.length, 'the flap must have built a second session').toBeGreaterThan(1);
    expect(sampledBeforeFlap, 'the first live window must have sampled').toBeGreaterThan(0);
    expect(statsMissingField.mock.calls.length, 'the second live window must have sampled too').toBeGreaterThan(
      sampledBeforeFlap,
    );

    expect(resilienceLines('stats-field-missing')).toHaveLength(1);
  });

  /** Spec 095 T006 / FR-004, plan risk R3 — the actuator half of the same rule. */
  it('Says a playout target is unsupported once across a session that flaps', async () => {
    const setPlayoutTargetRefuses = vi.fn((): PlayoutTargetOutcome => 'unsupported');
    setPlayoutTargetBehaviour = setPlayoutTargetRefuses;

    render(
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
    const actuatedBeforeFlap = setPlayoutTargetRefuses.mock.calls.length;

    await flapThroughReconnect();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    expect(sessions.length, 'the flap must have built a second session').toBeGreaterThan(1);
    expect(actuatedBeforeFlap, 'the first live window must have actuated').toBeGreaterThan(0);
    expect(setPlayoutTargetRefuses.mock.calls.length, 'the second live window must have actuated too').toBeGreaterThan(
      actuatedBeforeFlap,
    );

    expect(resilienceLines('playout-target-unsupported')).toHaveLength(1);
  });

  /**
   * Issue #2189 / spec 140. **A thrown `getStats` used to vanish, forever, on
   * every tick** — both samplers wrapped their whole async IIFE in
   * `.catch(() => undefined)`. This is the decode sampler's half: R2 below is
   * its independent twin, and R3/R4/R5 the cadence and scoping this reporting
   * must hold.
   *
   * <p>
   * Spec 140 §*Decision* 4: two transitions, not one — `decode-sampler-failed`
   * names the `SFU → kiosk decode` leg. Cadence and detail shape follow #2084's
   * `countReportableSkew` (quoted in the plan): report the first failure and
   * every failure count that is a power of ten thereafter, never every tick.
   * </p>
   */
  it('Says so when reading the tile lag throws', async () => {
    const { container } = render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={() => {}} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // Asserted BEFORE the outcome, per this file's convention: a double never
    // reached would make every later assertion true of a component that did
    // nothing.
    expect(statsThrows, 'the lag sampler must actually have run').toHaveBeenCalled();

    // 2 000 ms interval, 20 000 ms advanced: 10 ticks, decade boundaries at 1
    // and 10 (plan §*The tick arithmetic*).
    const failed = resilienceLines('lag-sampler-failed');
    expect(failed).toHaveLength(2);
    expect(failed[0]![1]).toEqual({
      subsystem: 'stream',
      transition: 'lag-sampler-failed',
      cameraIdentifier: 'cam-42',
      count: 1,
      reason: 'getStats exploded',
    });
    expect(failed[1]![1]).toEqual({
      subsystem: 'stream',
      transition: 'lag-sampler-failed',
      cameraIdentifier: 'cam-42',
      count: 10,
      reason: 'getStats exploded',
    });

    // Spec 140 FR-009 / ADR-0128 FR-013: reporting must not cost the picture.
    expect(container.querySelector('video')).not.toBeNull();
  });

  /**
   * Issue #2189 / spec 140, R2. The decode sampler's own report, on a page
   * that never asked for lag at all — proving the decode half fails and
   * reports independently of `onLagMeasured`, and that the lag interval does
   * not start (and so cannot report) when nobody passed a callback
   * (`CameraViewer.tsx:204-207`, FR-004).
   */
  it('Says so when the decode sampler throws on a page with no wall', async () => {
    render(<CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} />);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    expect(statsThrows, 'the decode sampler must actually have run').toHaveBeenCalled();

    // 5 000 ms interval, 20 000 ms advanced: 4 ticks, only the first decade
    // boundary (1) is reached (plan §*The tick arithmetic*).
    const failed = resilienceLines('decode-sampler-failed');
    expect(failed).toHaveLength(1);
    expect(failed[0]![1]).toEqual({
      subsystem: 'stream',
      transition: 'decode-sampler-failed',
      cameraIdentifier: 'cam-42',
      count: 1,
      reason: 'getStats exploded',
    });

    expect(resilienceLines('lag-sampler-failed')).toHaveLength(0);
  });

  /**
   * Issue #2189 / spec 140, R3. **The case a single shared counter would
   * fail.** Spec §*Decision* 2 argues two independent refs rather than one,
   * because the lag sampler runs `onLagMeasured` — the wall's own code, which
   * the decode sampler never touches — and the two observe different §IV
   * legs. A stats read that succeeds but a wall callback that always throws
   * must report only `lag-sampler-failed`, never `decode-sampler-failed`.
   *
   * <p>
   * Needs the advancing fixture: the constant one leaves `lagBetween` and
   * `bufferDelayBetween` null forever, so `onLagMeasured` would never be
   * reached and the case would be vacuous.
   * </p>
   */
  it('A sampler that fails does not silence the other', async () => {
    statsBehaviour = advancingVideoStat();
    const onLagMeasuredThrows = vi.fn(() => {
      throw new Error('wall callback exploded');
    });

    render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        onLagMeasured={onLagMeasuredThrows}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // Asserted BEFORE the verdicts: a callback never reached would make "no
    // decode-sampler-failed line" true of a component that measured nothing.
    expect(onLagMeasuredThrows, 'the wall callback must actually have run').toHaveBeenCalled();

    expect(resilienceLines('lag-sampler-failed').length).toBeGreaterThanOrEqual(1);
    expect(resilienceLines('decode-sampler-failed')).toHaveLength(0);
  });

  /**
   * Issue #2189 / spec 140, R4. **Bounds a permanently broken sampler to a
   * decade cadence** — the case a naive log-every-tick fix fails. 200 000 ms
   * at the lag sampler's 2 000 ms interval is 100 ticks; the decade boundaries
   * are 1, 10 and 100, and nothing else (plan §*The tick arithmetic*).
   */
  it('Bounds a permanently broken sampler to a decade cadence', async () => {
    render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={() => {}} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(200_000);
    });

    expect(statsThrows, 'the lag sampler must actually have run').toHaveBeenCalled();

    const failed = resilienceLines('lag-sampler-failed');
    expect(failed.map((call) => (call[1] as { count: unknown }).count)).toEqual([1, 10, 100]);
  });

  /**
   * Issue #2189 / spec 140, R5. **A flap is not a new fault.** The counter
   * lives at component scope (spec §*Decision* 2, mirroring
   * `reportedMissingFieldsRef`'s own scoping argument), so a tile that goes
   * `live → reconnecting → live` must continue counting from where it left
   * off rather than restart at 1 — an effect-scoped counter would instead emit
   * a third line here, at the reconnected count 1.
   *
   * <p>
   * Assert on the totals, not on a tick count during the flap: the 5 s
   * disconnect grace may or may not leave the sampler running, and this case
   * must not depend on which (plan T007).
   * </p>
   */
  it('Counts across a flap rather than starting again', async () => {
    render(
      <CameraViewer cameraIdentifier="cam-42" getToken={() => Promise.resolve('token')} onLagMeasured={() => {}} />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });
    const calledBeforeFlap = statsThrows.mock.calls.length;
    expect(resilienceLines('lag-sampler-failed'), 'the first live window must have reported 1 and 10').toHaveLength(2);

    await flapThroughReconnect();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // Asserted BEFORE the totals, per this file's convention: a flap that
    // never rebuilt the session, or a second live window that never sampled,
    // would make "still two lines" true of a component that did nothing the
    // second time.
    expect(sessions.length, 'the flap must have built a second session').toBeGreaterThan(1);
    expect(statsThrows.mock.calls.length, 'the second live window must have sampled too').toBeGreaterThan(
      calledBeforeFlap,
    );

    // Still exactly 2: the second window's 10 ticks carry the counter from 10
    // to 20, and the next boundary (100) is nowhere near reached.
    expect(resilienceLines('lag-sampler-failed')).toHaveLength(2);
  });

  /**
   * Issue #2189 / spec 140, G1 (US-2) — **must stay green before and after.**
   * Pins FR-007: without this guard the fix could be an unconditional
   * `logResilienceEvent(...)` on every tick, and every kiosk would claim a
   * permanently broken instrument with the whole suite still green (the
   * spec-095 review finding on the actuator half, re-applied here). Uses the
   * advancing fixture so both `stats()` and `onLagMeasured` are actually
   * reached and actually succeed.
   */
  it('Says nothing about a sampler that does not throw', async () => {
    const advancingStats = vi.fn(advancingVideoStat());
    statsBehaviour = advancingStats;
    const onLagMeasured = vi.fn();

    render(
      <CameraViewer
        cameraIdentifier="cam-42"
        getToken={() => Promise.resolve('token')}
        onLagMeasured={onLagMeasured}
      />,
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20_000);
    });

    // Asserted BEFORE the silence: a sampler or callback never reached is
    // silent too, and would make this case true of a component that did
    // nothing.
    expect(advancingStats, 'the samplers must actually have run').toHaveBeenCalled();
    expect(onLagMeasured, 'the wall callback must actually have run').toHaveBeenCalled();

    expect(resilienceLines('decode-sampler-failed')).toHaveLength(0);
    expect(resilienceLines('lag-sampler-failed')).toHaveLength(0);
  });
});
