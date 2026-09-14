# Plan — Spec 157, a tile that shows its own camera

**Phase:** 2 (Plan) — ADR-0037
**Spec:** `specs/157-a-tile-that-shows-its-own-camera/spec.md`
**ADRs:** ADR-0075 (RTK Query), ADR-0074 (two apps, one shared composite),
ADR-0123 (composite+render is the operator's wait), ADR-0128 (playout
alignment), ADR-0117 (§VII binds implemented legs), ADR-0109 (`[P]`),
ADR-0036 (smallest change; read before write), ADR-0144 (lane).

**Bounded context: none.** This is `apps/`, not `src/`. No domain model, no
aggregate, no value object, no domain event, no integration event, no
persistence, no message, no migration. The "no cross-context project
references" rule and the `Shared.Contracts` boundary are **not engaged** —
nothing here is in a context. `NetArchTest` is untouched. Recorded explicitly so
the absence reads as checked rather than forgotten.

**Latency budget (§IV):** three legs cited, no cell moves, no measurement owed.
Earned in `spec.md` § *Latency-budget impact*; the §VII/#1940 condition raised
there is a gate item, not a plan item.

---

## 1. Layers, such as they are

Three production files, one layer each in the frontend's own stratification:

| File | Role | Change |
|---|---|---|
| `apps/shared/src/ui/composites/CameraViewer.tsx` | composite — owns the `<video>`, the overlay and the two samplers | the query read (FR-001); the error presentation (FR-005) |
| `apps/shared/src/ui/composites/useWhepSession.ts` | hook — owns the per-tile session state machine | the camera-change effect (FR-003, FR-006); the explicit dep (FR-004) |
| `apps/shared/src/ui/composites/FrameGrabber.tsx` | composite — one capture, one session | the query read (FR-002) |

No file outside `apps/` changes. Nothing under `src/**` changes.

---

## 2. The shape of the change

### 2a. `CameraViewer.tsx` — one word, and one presentation branch

```
  const { data: stream, … }    →    const { currentData: stream, … }
```

`stream` then feeds three consumers, and each must be re-read against
`undefined` being *normal during a swap* rather than only *normal before the
first load*:

| Consumer | Line | Effect of the change |
|---|---|---|
| `useWhepSession({ whepUrl, streamState, streamError })` | `:105-107` | all three become `undefined`/`null` during the gap — the point of FR-001 |
| `ViewerOverlay`'s `stream` prop | `:358` | `labelFor`'s `stream?.state === 'Provisioning'` branch stops firing during a swap; correct, the new camera is not known to be provisioning |
| nothing else | — | `stream` has no other reader |

**FR-005 (the failed read).** `ViewerOverlay` already receives `queryError` and
already renders *"Could not reach the streaming service."* as a hint
(`:383`), but it renders at all only when `status !== 'live'` and its **label**
and **tone** come from `labelFor(status, stream)`, which has no error arm. The
smallest expression: give `labelFor` and the tone the query error, so a read
that has failed with no stream for the current camera reads as an error
(`text-accent-fault`) rather than as "Connecting…" or "Idle".

Do **not** introduce a new status value for this. `status` is the session state
machine and a failed *read* is not a session state; conflating them is what
would drag #2355's dead `'error'` member into scope.

### 2b. `useWhepSession.ts` — a camera-change effect, placed before the session effect

A new effect, declared **above** the session effect at `:148`:

- guarded by a `previousCameraRef`, mirroring `previousStreamStateRef` (`:123`)
  — the file's own idiom for "did this prop actually change";
- **no-op on first mount**, so today's initial `idle` → `Idle` /
  `Provisioning stream…` labels are untouched (FR-007);
- on a change: `videoRef.current.srcObject = null`, `transitionTo('connecting')`,
  `attemptRef.current = 0`.

**Ordering is the reason it goes above, and it is load-bearing.** React runs
every cleanup in declaration order, then every setup in declaration order. With
the new effect first:

```
cleanup(cameraChangeEffect)   — none
cleanup(sessionEffect)        — closes camera A's WhepClient        (existing)
setup(cameraChangeEffect)     — clears srcObject, leaves live, resets attempts
setup(sessionEffect)          — !whepUrl → early return, no session (existing)
```

Declared *below*, the clear would run before the teardown and A's teardown could
not be relied on to leave the element empty. Put a one-line comment saying so;
this is exactly the class of "why" the house rules keep.

**`transitionTo` inside an effect trips `react-hooks/set-state-in-effect`** (v7,
`--max-warnings 0`). The file already carries one such disable with a stated
reason at `:159`; use the same form and a reason specific to this effect —
*the status machine has two drivers, and a camera change is the second one.*

**Clearing `srcObject` does not break the media watchdog, and the reasoning is
already written down.** `useWhepSession.ts:227-233` explains that
`mediaBaseline` exists *because* `teardownLocally` never clears `srcObject`, so
a tile that has ever shown a picture would read the previous session's frames as
this one's (#2111). Clearing on a camera change makes `totalVideoFrames` reset
to 0 via the media-element load algorithm, so `armMediaWatch`'s baseline becomes
0 — which is *correct* for a genuinely new camera — and `pollForMedia` already
handles a decrease (`:219`). **Scope the clear to the camera-change path only.**
Clearing on every teardown would change the retry path's baseline semantics and
is a different, larger change.

**FR-004, the explicit dependency.** Add `cameraIdentifier` to `:304`. The effect
body does not reference it, so `react-hooks/exhaustive-deps` will report an
*unnecessary dependency* and fail the build at `--max-warnings 0`. The file
already solved this exact problem one line into the same effect:

```ts
void retryNonce; // dep only: each bump forces a fresh connection attempt
```

Mirror it — `void cameraIdentifier;` with a reason naming FR-004's failure mode
(a future refactor moving the camera behind a ref silently deletes the
teardown). **Reuse this idiom; do not add an eslint-disable.**

### 2c. `FrameGrabber.tsx` — one word

`data` → `currentData` at `:37`. No other change. Behaviour-preserving today
(spec FR-002); its covering suite is `FrameCapture.test.tsx`, captured green
first and passing unmodified after.

---

## 3. Invariants this change must not move

Each is a real assertion in the characterisation tasks, not a hope.

1. **Position keying stays.** `CellPage` keys on `positionKey(row, col)`; nothing
   here keys a tile on its camera. `CameraViewer.tsx:113-126` (spec 095's
   once-per-mounted-tile scope for `reportedMissingFieldsRef`,
   `reportedNoPlayoutRef`) is untouched and still correct: those record facts
   about the browser engine, not about the camera.
2. **The two sampler-failure counters still do *not* reset on a camera swap.**
   `CameraViewer.tsx:165-171` states this deliberately. FR-006 resets
   `attemptRef` — a *session* counter — and must not be read as licence to reset
   the sampler counters.
3. **The retry ladder's shape is unchanged.** `jitteredRetryDelay`,
   `RETRY_BASE_MS`, `RETRY_CAP_MS`, `DISCONNECT_GRACE_MS`, `MEDIA_WATCHDOG_MS`,
   `MEDIA_POLL_MS`, `scheduleRetry`'s clear-then-transition order and the
   `confirmMedia` reset point all stay byte-identical.
4. **The Degraded→Healthy re-dial stays.** `:306-318` and `previousStreamStateRef`
   are untouched; the new ref is a second, separate one.
5. **The `offline` path stays.** `offlineMessage` still short-circuits before any
   client is built, and an offline *new* camera still reads "Stream is offline".
6. **`stats` and `setPlayoutTarget` keep their stable identities.** Both are
   `useCallback(…, [])` for a reason issue 1889 paid for; nothing here gives
   them dependencies.
7. **No composite in `apps/shared` learns about Redux.** #2374 is open on this.
   The test provides the store; the component does not acquire one.

---

## 4. Messaging, persistence, boundaries

**None.** No domain event, no integration event, no `Shared.Contracts` message,
no queue, no outbox, no saga, no EF model, no migration, no idempotency key, no
new HTTP endpoint or DTO. The only wire interaction is an existing
`GET stream-distribution/streams/{cameraIdentifier}` whose shape does not change,
and an existing WHEP `POST`/`DELETE` whose shape does not change.

**Retry safety (ADR-0142/0143): not engaged.** No `POST` this spec controls is
retried by it. The WHEP `POST` is `WhepClient`'s and is untouched; #2355 owns its
refusal handling.

---

## 5. The test architecture — the part that is not obvious

### The problem

Eight suites mock `useGetStreamQuery` as `{ data, isLoading, error }`. A mocked
hook returns the same value for every argument, so **the whole mocked estate is
structurally incapable of distinguishing `data` from `currentData`** — it cannot
go red against this defect, and it *will* go red against the fix, because
`currentData` is simply absent from every mock and `stream` becomes `undefined`
everywhere. That is the blast radius: eight files, no assertion among them
wrong.

### The resolution — two moves, in this order

**Move 1 (T001, characterisation, green).** Widen all eight mocks to return
`currentData` alongside `data`, mirroring the same object. Mechanical,
behaviour-preserving, and it must land *before* any production change so the
suites are seen green on both sides of it. **No assertion in those files may be
edited.** If one has to be, behaviour moved and the change is blocked (§Testing).

Widening the mocks makes the estate able to *survive* the fix. It does **not**
make it able to *catch* the defect — the mocks still answer irrespective of
argument. That is what move 2 is for, and the distinction is why T001 is not
allowed to count as the phase-4a test.

**Move 2 (T005, RED).** One new file driving the **real** `streamsApi` hook.
Precedent and reference:
`apps/management-web/src/features/overlays/OverlayEditorDialogChainRetention.test.tsx`
— real hook, locally-built store, stubbed gateway, and a header comment
explaining why a mocked suite could not have caught it.

Shape, adapted to `apps/shared`:

- `// @vitest-environment jsdom` pragma (the package default is `node`).
- `vi.stubEnv('VITE_API_GATEWAY_URL', 'http://gateway.test')` **before** any
  dynamic import reaches `gateway.ts`, which resolves the origin at module load
  — the form `rules.api.test.ts` and the chain-retention precedent both use.
  Node's `Request` rejects a relative URL.
- `configureStore({ reducer: { [streamsApi.reducerPath]: streamsApi.reducer },
  middleware: (g) => g().concat(streamsApi.middleware) })`, wrapped in
  `<Provider>`. Built by the test; the composite is unchanged (invariant 7).
- **The real `WhepClient`**, with the `FakePeerConnection` +
  `globalThis.RTCPeerConnection` substitution and the duck-typed `fetch` stub
  that `apps/shared/src/ui/composites/CameraViewer.test.tsx:13-57` already
  establishes. The one extension: the stub must serve **both** surfaces — a JSON
  `GET /streams/{id}` and an SDP WHEP `POST` — dispatched on URL, and must be
  able to hold or fail the `GET` for a named camera on command.
- Assertions read `fetchMock.mock.calls` for WHEP POST URLs (the existing
  pattern at `CameraViewer.test.tsx:302-305`), `FakePeerConnection.instances[i]
  .closed` for the teardown, and `videoEl.srcObject` for the frame.

### Why this is the discriminating shape, in one line

The test **re-props a mounted component** and asserts the DOM element identity
is unchanged across the swap. A test that remounts proves nothing — the whole
defect is that the tile is not remounted. That assertion is not decoration; it
is what makes the rest of the file mean anything.

### What is red today, precisely

Against unfixed `develop`, at the instant after the prop changes and before
camera B's `GET` answers:

| Assertion | Today | Why |
|---|---|---|
| no second WHEP POST to camera A's URL | **FAILS** | the effect re-runs on `transitionTo` and re-dials A's stale `whepUrl` |
| `videoEl.srcObject === null` | **FAILS** | `teardownLocally` never clears it |
| the tile is not Live | **FAILS** | the `!whepUrl` early return never calls `transitionTo` |
| the `<video>` element identity is unchanged | passes | the premise, not the finding |

Three independent reds, one premise. Quote all of it in the PR (ADR-0139).

---

## 6. Rejected alternatives

| Rejected | Why |
|---|---|
| Key tiles on `cameraIdentifier` in `CellPage` | Overturns spec 095's deliberate once-per-mounted-tile scope, multiplies engine-level resilience lines by every camera that passes through a slot overnight, and is a change to the kiosk's render path rather than to the defect. |
| `skipToken` / `skip` while the camera is changing | The hook has no way to know a change is in flight that is cheaper than `currentData` already knowing. |
| Clear `srcObject` in `WhepClient.teardownLocally` | Changes every teardown, including every retry, and inverts the `mediaBaseline` reasoning at `useWhepSession.ts:227-233` that #2111 paid for. A camera swap is the narrow case; keep the change there. |
| Reset `status` by giving `useWhepSession` a `key` from the caller | Moves the remount decision back to `CellPage` and reintroduces the position-keying question the spec closes. |
| Fix only `CameraViewer`, leave `useWhepSession` | Produces A's frozen frame labelled Live — the rejected freeze-the-last-frame shape, minus the badge. See spec § *What `currentData` alone actually produces*. |
| Fix only `useWhepSession`, leave `CameraViewer` | `whepUrl` is still A's, so the rebuilt session still goes to A. Neither half works alone. |
| Add the ESLint rule here | ADR-class (ADR-0036); lane-forbidden (ADR-0144); and the remaining sites would land it red. |

---

## 7. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | The mock widening (T001) silently changes a suite's behaviour | Assertions may not be edited. Run each suite green before and after T001 and diff nothing but the mock objects. |
| R2 | Effect ordering is got wrong and the clear runs before the teardown | The new effect is declared above the session effect, with a comment saying why. T005's `srcObject === null` assertion is the guard. |
| R3 | `exhaustive-deps` rejects `cameraIdentifier` as unnecessary and `--max-warnings 0` fails | `void cameraIdentifier;`, mirroring `:149`. Verified by `pnpm lint`, which is a task's done-when. |
| R4 | `transitionTo('connecting')` in the new effect trips `set-state-in-effect` | Same disable form as `:159`, with a reason specific to this effect. |
| R5 | The new suite's `fetch` stub mis-dispatches and a WHEP POST is read as a stream GET | Dispatch on URL prefix; assert the *positive* case (a POST to B's URL does appear) as well as the negative, so a stub that serves nothing cannot pass. |
| R6 | #2355 lands first or concurrently and conflicts | Same effect block, different lines — no textual conflict expected. If it lands first, rebase and check whether its terminal refusal state needs clearing on a camera change; that clearing belongs to whichever lands second. |
| R7 | The gap is visible on the wall as a black tile for a round-trip | Accepted, and it is the decision: a visible gap is the price of never showing an unvouched picture. Bounded by one gateway round-trip in the ordinary case. |
| R8 | A reviewer strikes FR-006 | Fine — it is flagged as strikeable in the spec and is one line in a new effect. Nothing else depends on it. |
