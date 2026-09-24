# Plan 233: A refusal is not a reconnect

**Spec**: [spec.md](./spec.md) · **Phase**: 2 (Plan) · **Issue**: #2355

## 1. Where it lives

This is a frontend-only change in `apps/shared/src/ui/composites`. It touches no bounded context, and `Shared.Contracts`, C#, AppHost and `WhepClient` all stay unchanged. DDD layering and §II do not apply (no domain model). NetArchTest is unaffected.

| File | Change |
|---|---|
| `useWhepSession.ts` | Classify the refusal, add the terminal transition and the resilience line, and correct the docblock |
| `CameraViewer.tsx` | `labelFor('error')` becomes **Access refused**. Nothing else changes: the overlay tone for `'error'` is already `text-accent-fault` and the hint already comes from `errorMessage` |
| `FrameGrabber.tsx` | Add `'error'` to the fail-fast arm and correct the comment |

## 2. Design

### D1: Reuse the existing `'error'` member; the union does not change

`CameraViewerStatus` already declares `'error'`, and `CameraViewer` already styles it. Adding a new `'refused'` member would mean editing the union, `labelFor`, the overlay tone and FrameGrabber, while leaving `'error'` unreachable. Reusing `'error'` makes the existing code reachable and changes only the wording. **Consequence**: there is no exhaustiveness fallout, since no consumer outside the three composites references the union (spec §1).

### D2: Classification is a module-local predicate

```ts
function isRefusal(cause: unknown): cause is WhepError {
  return cause instanceof WhepError && (cause.kind === 'unauthorized' || cause.kind === 'forbidden');
}
```

It is local to `useWhepSession.ts` and not exported, with one call site (ADR-0036). `WhepError` becomes a **value** import from `@smart-sentinel-eye/shared/streaming/WhepClient` next to the existing `WhepClient` value import. `'stream-unavailable'` is deliberately excluded, since it describes the path, not the viewer.

### D3: The terminal branch sits in the `connect()` rejection handler

At `:343-346`:

```ts
client.connect(videoEl, controller.signal).catch((cause: unknown) => {
  if (disposed || controller.signal.aborted) return;
  if (isRefusal(cause)) {
    logResilienceEvent('stream', 'whep-refused', { cameraIdentifier, kind: cause.kind });
    transitionTo('error', REFUSED_MESSAGE);
    return;
  }
  scheduleRetry(cause instanceof Error ? cause.message : String(cause));
});
```

- `REFUSED_MESSAGE = 'The stream server refused this viewer. This tile will not retry on its own.'` is a module constant next to the other constants.
- **No teardown is needed.** `WhepClient.connect` already calls `teardownLocally()` on any rejection (`WhepClient.ts:111-114`), and nothing was POSTed successfully, so no `Location` exists to DELETE. The effect cleanup still runs `client.close()` on the next re-run or unmount, and that is harmless.
- **No media timers can be armed.** They are armed only on `connected`, which requires a remote description the refused POST never produced. No `clearMediaTimers()` is needed.
- `attemptRef` is left untouched. The next exit (FR-003) is an outside change, and the camera-swap path already resets it.
- `cameraIdentifier` is already in scope (the effect depends on it) and is used as-is.

### D4: Exits reuse existing mechanisms (FR-003); nothing new is built

| Trigger | Mechanism already present |
|---|---|
| Camera change | camera-swap effect → `transitionTo('connecting')`, and the session effect re-runs on `cameraIdentifier` |
| `whepUrl` / `offlineMessage` change | session-effect dependencies |
| Degraded → Healthy | the health effect bumps `retryNonce` (`:366-370`) |
| Remount / reload | new hook instance |

The `Degraded` demotion (`:362`) applies only to `live`, so it cannot pull a tile out of `'error'`. That is correct, and it needs no change.

### D5: The resilience line

`logResilienceEvent(subsystem, transition, detail)` (`observability/resilienceLog.ts:9`) prints `console.info('[resilience]', {subsystem, transition, ...detail})` and reaches the single sink (ADR-0118). The call is `('stream', 'whep-refused', { cameraIdentifier, kind })`. The transition name follows the existing kebab-case convention (`session-release-failed`, `track-without-stream`, `playout-target-unsupported`). It fires once per refusal because the state is terminal, so it cannot flood.

### D6: FrameGrabber

At `:99`, the condition becomes `status === 'reconnecting' || status === 'offline' || status === 'error'`. The comment at `:95-98` is rewritten to drop the "`'error'` is not reachable" claim and to say that a refusal now arrives as `'error'`.

## 3. Messaging / boundaries

None. No domain or integration event, no contract, no cross-context reference. The `whep-refused` line is a browser resilience log, not a message.

## 4. Test plan

These are new test files, following the per-concern pattern `CameraViewer{Alignment,Announcement,CameraSwap,Media}.test.tsx`. The harness is copied from `CameraViewer.test.tsx`: `FakePeerConnection`, `sdpResponse`, the mocked `useGetStreamQuery`, fake timers and `Math.random` pinned to 0.5.

- **`CameraViewerRefusal.test.tsx`** (new): the spec §3 Gherkin, except FrameGrabber. The fetch mock answers `new Response('forbidden', {status: 403})` / `{status: 401}` / `'stream unavailable'` 403 / 500. Assertions cover the visible label, the `camera-viewer-status` region text, `fetchMock` POST count and `FakePeerConnection.instances.length` after `advance(60_000)`, and a `console.info` spy for the resilience line.
  - Red: 401 refusal, 403 refusal, refusal on retry, null token, Degraded→Healthy single attempt, unmount.
  - Characterisation (green before and after): the transient ladder outline, and the camera-swap exit.
- **`FrameCapture.test.tsx`** (append one `it`): *"Abandons a capture whose WHEP offer is refused without waiting for the timeout"*. It is characterisation, green today via `reconnecting`, and turns red if D3 lands without D6.
- **`CameraViewerMedia.test.tsx`**: add `'Access refused'` to `NON_LIVE_LABELS` so that "Live = absence of every non-live label" stays complete. This strengthens a test and weakens nothing.

Coverage: `apps/shared` falls under the Shared ≥ 90% gate (ADR-0065). The new branch is fully covered by the red tests.

## 5. Constitution and ADR check

- §IV: N/A (spec §7). §Availability: R1 is recorded, not waived; it needs acceptance in writing (spec §5).
- ADR-0143: the same argument spec 142 made, *do not re-send a credential that cannot succeed*, applied here to the setup path. No amendment is needed.
- ADR-0036: three production files, a local predicate and a constant. No new abstraction, no config knob, no attempt counter.
- ADR-0139 / §Testing: red first (spec §6).
- **No new ADR** for points 1–3.

## 6. Verification (phase 5)

Spec §4. Cite: the stub call count per tile over 120 s (expect 1), the observed `kind`, and screenshots of a refused wall tile and of the overlay-editor capture message.
