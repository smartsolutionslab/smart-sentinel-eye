import { useEffect, useRef } from 'react';
import { logResilienceEvent } from '../../../observability/resilienceLog';
import type { PlayoutTargetOutcome } from '../../../streaming/WhepClient';
import type { CameraViewerStatus, WhepSessionResult } from '../useWhepSession';

interface PlayoutTargetInput {
  status: CameraViewerStatus;
  playoutTargetMilliseconds: number | null | undefined;
  setPlayoutTarget: WhepSessionResult['setPlayoutTarget'];
  cameraIdentifier: string;
}

export function usePlayoutTarget({
  status,
  playoutTargetMilliseconds,
  setPlayoutTarget,
  cameraIdentifier,
}: PlayoutTargetInput) {
  // Spec 095 FR-004: an engine that cannot hold a playout target says so once.
  //
  // Held for the life of the mounted tile for the same reason as the missing-field ref in `useSamplerReporting` — this effect
  // is keyed on `status` too, so a flapping tile would otherwise report on every
  // recovery. And on the same scope for the same reason: once per mounted tile,
  // because an engine that carries no `jitterBufferTarget` carries none for any
  // camera the wall later puts in this slot.
  const reportedNoPlayoutRef = useRef(false);

  // Apply the wall's decision. Undefined and null both mean "leave this tile
  // alone", which is what a single-camera page and an unconverged wall both
  // want — and neither is the same as a target of zero.
  useEffect(() => {
    if (status !== 'live' || playoutTargetMilliseconds === undefined || playoutTargetMilliseconds === null) {
      return;
    }
    // Wrapped, not merely ignored. `setPlayoutTarget` guards the assignment
    // itself, but the call can still throw before it gets there — an engine
    // without `getReceivers`, a torn-down connection — and an exception here
    // would take the render effect with it. A tile that cannot be aligned must
    // carry on showing video (FR-013). Mapped to 'refused', one frame out: the
    // engine refused, and it is still reported below, exactly as today's
    // `applied = false` was.
    let outcome: PlayoutTargetOutcome;
    try {
      outcome = setPlayoutTarget(playoutTargetMilliseconds);
    } catch {
      outcome = 'refused';
    }

    // The answer is no longer collapsed into one boolean (#2198 item 2, spec
    // 142). `setPlayoutTarget` now answers which of four things happened, and
    // this effect reports two of them: 'unsupported' (no video receiver ever
    // carries `jitterBufferTarget` — Firefox, Safari, pre-115 Chromium) and
    // 'refused' (a receiver carries it and every assignment threw). Both are
    // permanent facts about this browser engine, and mean the same thing to an
    // operator reading the line: this tile will not align.
    //
    // 'not-connected' is no longer reported. It is the transient every tile
    // passes through on its way up — no peer connection yet, or one with no
    // video receiver attached yet — and treating it the same as the permanent
    // causes was spec 095's own recorded residual: a null `clientRef.current`
    // (or a session mid-connect) answered the same falsy value as an engine
    // that will never carry the property, so a tile could latch this line on
    // its first render regardless of health. The `status === 'live'` guard
    // above already screens out most of that window; closing the residual here
    // makes it a second line of defence rather than the only one.
    if ((outcome === 'unsupported' || outcome === 'refused') && !reportedNoPlayoutRef.current) {
      reportedNoPlayoutRef.current = true;
      logResilienceEvent('stream', 'playout-target-unsupported', { cameraIdentifier });
    }
  }, [status, playoutTargetMilliseconds, setPlayoutTarget, cameraIdentifier]);
}
