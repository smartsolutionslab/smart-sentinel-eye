# Spec 142 — Tasks

**Phase:** 3 (Tasks) · **Date:** 2026-09-13
**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Issue:** #2198
**Engineer:** `frontend-engineer`. Every file is `.ts`/`.tsx` under `apps/`; no
backend, no infra, no Aspire resource, no migration, no `src/` file opened for
writing. `src/AppHost/AppHost.cs` and `src/AppHost/Resources/mediamtx.yml` were
**read** for the measurement and are not modified.

---

## Phase 4a colour — RED, both items

Declared here so phase 4a has no ambiguity to resolve, per ADR-0144.

| Item | Colour | Why |
|---|---|---|
| **1 — the release report** | **RED** | A failure that was discarded becomes a reported one. New behaviour under constitution §Testing / ADR-0139. A test arriving green is a phase-4 failure. |
| **2 — the tri-state** | **RED** | One `boolean` becomes four distinguishable answers, and the call site stops reporting one of them. New behaviour, and the call-site half changes what an operator sees. |

**Neither red is a compile error.** Spec 061's `24e6fc4c` ruled that a
`TS2305: has no exported member` is not a red test, so the constructions below
are chosen to fail at runtime **against today's code with the doubles reached**:

- **Item 1's reds** need no new export at all. `session-release-failed` is a
  string literal passed to an existing function; the cases fail today because
  **nothing is logged**, at `expect(...).toHaveLength(1)`.
- **Item 2's reds are written against the *values*, not the type.** Each case
  asserts `expect(client.setPlayoutTarget(120)).toBe('not-connected')` and so on.
  Against today's code the method returns `false` or `true`, so each fails at its
  `toBe` with a legible `expected false to be 'not-connected'`. **`toBe` is not
  typed against the receiver** — spec 095's PR proved exactly this by
  mis-spelling an expected value with a narrowed return type in place and getting
  `tsc` rc=0 — so these cases compile and run before `PlayoutTargetOutcome`
  exists, and are genuine runtime reds rather than guards.
- **The call-site red (T005)** is red because today `'not-connected'` (any
  falsy-mapped answer) produces a line and the case asserts zero.

**The behaviour-preserving claims** (FR-009, plan invariants 1-6 and the release
invariants 1-6) are held by the **existing** suites, which must pass
**unmodified**: `WhepClient.test.ts`'s five `close()` cases, and
`CameraViewerAlignment.test.tsx`'s four spec-095 cases plus the three spec-045
cases. **An assertion that has to be edited is a block, not an adjustment.**
The single licensed exception is R1: three test *doubles* change their return
value's spelling because the seam's type changed. Their `expect(...)` lines stay
byte-identical, and a diff that changes one is a block.

**Format:** `[ID] [P?] [Story] description`.

---

## Phase 4a — the reds, written and observed failing before any fix

Owner: `test-writer`. Returns the **verbatim** failing output; the engineer
receives it as its brief and may not edit these tests to pass.

- **[T001] [US2]** *Foundational for item 2's reds — give the fake peer
  connection receivers a `WhepClient` can act on.*

  `WhepClient.test.ts`'s `FakePeerConnection.receivers` is typed
  `{ track: { stop: () => void } }[]` (`:26`). `setPlayoutTarget` skips every
  receiver whose `track?.kind !== 'video'`, so **today the harness cannot reach
  any branch of the method under test.** That is why `WhepClient.test.ts` has no
  `setPlayoutTarget` case at all.

  Widen it to `{ track: FakeTrack; jitterBufferTarget?: number | null }[]` —
  `FakeTrack` (`:9`) already carries `kind`, `id` and `stop`, and is the shape
  the `ontrack` cases already build — and add four receiver builders at module
  scope:
  - `videoReceiverWithTarget()` — `jitterBufferTarget: null` as an own data
    property, so `'jitterBufferTarget' in receiver` is true and the assignment
    lands;
  - `videoReceiverWithoutTarget()` — no such key at all;
  - `videoReceiverThatThrows()` — `Object.defineProperty(r, 'jitterBufferTarget',
    { set() { throw new Error('engine refused the value'); }, get: () => null,
    configurable: true })`, so the key **is** present and the assignment throws;
  - `audioReceiverWithTarget()` — `track.kind === 'audio'`, key present.

  The existing `close() releases the peer connection` case and the `ontrack`
  cases must keep passing: `teardownLocally` calls `receiver.track?.stop()`, and
  `FakeTrack` already has `stop`.

  **Blocks T003 and T004.** Lands in the same commit as T003 — a harness with no
  case is dead code.

- **[T002] [US1]** *Red — a session release that fails says so.* Four cases in
  `WhepClient.test.ts`, beside the existing `close()` group.

  Every case spies `const info = vi.spyOn(console, 'info').mockImplementation(()
  => undefined)` and reads `resilienceLines(info.mock.calls,
  'session-release-failed')` (`:130`, unchanged). Every case **sequences** the
  fetch answers — `fetchMock.mockResolvedValueOnce(offerAnswerWithLocation)` then
  the DELETE's answer — because one `vi.fn` serves both requests (plan R4); a
  bare `mockResolvedValue` would hand the 401 to `connect()` and fail for an
  unrelated reason.

  1. **The rejected fetch.** DELETE rejects with `new Error('network down')`.
     Assert exactly one line, `toEqual({ subsystem: 'stream', transition:
     'session-release-failed', error: 'Error: network down' })`, **and** that
     `FakePeerConnection.lastInstance().closed` is `true` — the report must not
     have displaced the local teardown (FR-005). **Red today: zero lines.**
  2. **The 401.** DELETE resolves `new Response('', { status: 401 })`. Assert
     exactly one line, `toEqual({ …, status: 401 })`. **Red today: zero lines.**
     This is the issue's named dominant failure and the one FR-004 cites when it
     declines a retry.
  3. **The 500.** DELETE resolves `new Response('', { status: 500 })`. One line,
     `status: 500`. Present as a second status so the report is not written
     against 401 alone.
  4. **A rejecting `getToken`.** `getToken` resolves once for `connect()` then
     rejects at release time. Assert exactly one line carrying an `error` key —
     this path is swallowed identically today and is the one the `.catch`
     silently covered.

  All four are runtime reds against today's `.catch(() => undefined)`.
  No dependency.

- **[T003] [US2]** *Red — `setPlayoutTarget` answers which of the four.* Six
  cases in `WhepClient.test.ts`, in a new `describe`.

  1. **Never connected.** A fresh `WhepClient`, no `connect()`. Assert
     `client.setPlayoutTarget(120)` is `'not-connected'`. **Red: `false`.**
  2. **Connected, no receivers.** `connectedSession()` with `receivers = []`.
     Assert `'not-connected'`. **Red: `false`.** FR-007's first half.
  3. **Connected, one audio receiver carrying the property.** Assert
     `'not-connected'` **and** that the audio receiver's `jitterBufferTarget` is
     still `null` — the positive that proves the loop ran and skipped it, so a
     method that wrote to everything fails here rather than passing.
     **Red: `false`, and — if the kind guard were ever dropped — the written
     value.** FR-007's second half.
  4. **One video receiver without the property.** Assert `'unsupported'`.
     **Red: `false`.**
  5. **One video receiver whose setter throws.** Assert `'refused'`, **and** that
     `setPlayoutTarget` did not throw (the call is not wrapped in `expect(…).not
     .toThrow()` theatre — it simply returns, and the assertion on the value
     proves it returned). FR-008. **Red: `false`.**
  6. **Two video receivers, one accepting and one throwing.** Assert `'applied'`
     — plan invariant 3, the precedence line — **and** that the accepting
     receiver's `jitterBufferTarget` is `120`. **Red: `true`.**

  Depends on **T001**.

- **[T004] [US2]** *Red/quiet — the applied case, and the actuation proof.* Two
  cases in `WhepClient.test.ts`.

  1. **QUIET + the FR-009 positive.** One video receiver carrying
     `jitterBufferTarget`. Assert **first** that the receiver's
     `jitterBufferTarget` is `120` — the actuation actually happened — then that
     the outcome is `'applied'`. **Red today at the second assertion only
     (`true`), which is the point: the actuation half is already correct and this
     case pins it so the rewrite cannot move it.**
  2. **Two video receivers, both accepting.** Assert **both** receivers read
     `120`, then `'applied'`. This is plan invariant 2 — the loop must not
     short-circuit at the first success — and it is the case that fails if the
     implementation is written as `return 'applied'` inside the `try`.

  **Counterfactual (run at phase 5):** change the implementation's
  `outcome = 'applied'` to `return 'applied'`. Case 2 must fail, alone, on the
  second receiver's value.
  Depends on **T001**. Same file as T003; serial.

- **[T005] [US2]** *Red — the call site stops reporting "not connected".* One
  case in `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx`.

  A live tile asked for a 120 ms target, with
  `setPlayoutTargetBehaviour = vi.fn(() => 'not-connected')`. Assert, **in this
  order**:
  1. `expect(setPlayoutTargetNotConnected, 'the actuator must actually have
     run').toHaveBeenCalledWith(120)` — R3's positive, asserted **before** the
     silence, because an actuator that was never reached is silent too;
  2. `expect(resilienceLines('playout-target-unsupported')).toHaveLength(0)`.

  **Red today**: a falsy answer produces exactly one line, so the case fails at
  its length assertion with `expected 1 to be 0`.

  **This case is the whole of US2 at the call site, and it is the one 095 could
  not write** — every `setPlayoutTarget` double in the tree could only say
  "true" or "false", and "not connected" had no spelling.

  **Counterfactual (run at phase 5):** widen the report condition back to
  `outcome !== 'applied'`. This case must fail, alone, at its length assertion.
  Depends on nothing; **`[P]` with T002/T003/T004** (different file) but the
  implementing commit order in 4b makes it serial in practice.

---

## Phase 4b — the implementation

Owner: `frontend-engineer`, briefed with T001-T005's verbatim failing output.

- **[T006] [US1]** *Report a failed release.* `apps/shared/src/streaming/WhepClient.ts`,
  `releaseSession` (`:213-230`).

  Add the two continuations from `plan.md` §"`releaseSession`'s shape". Keep
  `keepalive: true`, keep the `void`, keep the `sessionUrl === null` early
  return, keep `getToken()` resolved at release time, keep `teardownLocally()`
  synchronous after it. **No retry** (FR-004) — and the code comment says which
  number declines it: MediaMTX 1.21.0 reclaims an orphaned session on ICE
  failure at about 30 s (pion defaults, no idle-session setting exists), so a
  retry recovers a bounded transient, and the dominant failure is a 401 that a
  retry re-presents.

  The existing comment at `:218-221` ("Fire-and-forget: teardown must never
  depend on the server still being alive") is **kept and extended** with FR-003's
  sentence: on page unload the continuation does not run and nothing is logged —
  the cost of `keepalive`, stated rather than implicit.

  Makes **T002** green. Must not touch `postOffer` or `teardownLocally`.

- **[T007] [US2]** *The tri-state, in the client.*
  `apps/shared/src/streaming/WhepClient.ts`.

  Export `PlayoutTargetOutcome` beside `WhepErrorKind` (`:3`). Rewrite
  `setPlayoutTarget`'s body exactly as `plan.md` gives it. **The docstring's
  last sentence — "Returns whether the target was applied" — becomes the
  four-member table**, and the `catch`'s existing comment (spec 045 FR-013) is
  kept verbatim with one clause added: the throw is now *named* `'refused'` and
  is still not raised.

  Makes **T003** and **T004** green. Depends on **T006** (same file; one file,
  one index).

- **[T008] [US2]** *The seam.* `apps/shared/src/ui/composites/useWhepSession.ts`
  — **two lines, and no more.**

  `:48` → `setPlayoutTarget: (milliseconds: number) => PlayoutTargetOutcome;`
  `:332` → `clientRef.current?.setPlayoutTarget(milliseconds) ?? 'not-connected'`

  Import the type from `../../streaming/WhepClient.js`. **Do not touch**
  `transitionTo`, `statusRef`, `status`, the connection effect, `scheduleRetry`,
  the timers, the cleanup at `:293-301`, or the `set-state-in-effect`
  suppression — that is #2157, which is `agent:blocked`. The `useCallback`'s
  empty dependency array and the comment at `:324-330` (issue #1889 — a fresh
  identity kills the caller's sampling interval) stay byte-identical.

  **Review check, stated so it is checkable:** the diff for this file must touch
  no line that writes `status` or `statusRef`. If it does, block.
  Depends on **T007**.

- **[T009] [US2]** *The report condition.*
  `apps/shared/src/ui/composites/CameraViewer.tsx` (`:310-340`).

  ```ts
  let outcome: PlayoutTargetOutcome = 'not-connected';
  try {
    outcome = setPlayoutTarget(playoutTargetMilliseconds);
  } catch {
    outcome = 'refused';
  }
  if ((outcome === 'unsupported' || outcome === 'refused') && !reportedNoPlayoutRef.current) {
  ```

  The outer `catch` maps to `'refused'` — the engine refused, one frame out —
  and it still reports, exactly as today's `applied = false` did, so the
  spec-045 "receiver refuses a playout target" case passes unmodified.

  **The logged payload does not change** (FR-011): still
  `{ cameraIdentifier }`, so `CameraViewerAlignment.test.tsx:365-369`'s exact
  `toEqual` passes untouched.

  The comment at `:327-339` is rewritten to say what is now true: the answer is
  four-valued, `'not-connected'` is the transient every tile passes through and
  is no longer reported, and the `status === 'live'` guard is now a second line
  of defence rather than the only one. **Spec 095's recorded residual is closed
  here, and the comment says so by number.**

  Makes **T005** green. Depends on **T008**.

- **[T010] [P] [US2]** *Retype the management-web double.*
  `apps/management-web/src/features/cameras/CameraViewerAlignment.test.tsx:30` —
  `vi.fn(() => true)` becomes `vi.fn((): PlayoutTargetOutcome => 'applied')`.

  **Nothing else in this file changes.** Its two cases assert
  `expect(setPlayoutTarget).not.toHaveBeenCalled()`; the double's value is never
  read. `[P]` with T008/T009 — different app, disjoint file.
  Depends on **T007** (the type must exist).

- **[T011] [US2]** *Retype the shared doubles — three of them, and no assertion.*
  `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx`.

  | Line | Today | Becomes | Why that member |
  |---|---|---|---|
  | `:46` | `let setPlayoutTargetBehaviour: (ms: number) => boolean` | `=> PlayoutTargetOutcome` | the seam's type |
  | `:344` | `vi.fn(() => false)` | `vi.fn((): PlayoutTargetOutcome => 'unsupported')` | its own comment says *"no video receiver carries `jitterBufferTarget`"* |
  | `:389` | `vi.fn(() => true)` | `vi.fn((): PlayoutTargetOutcome => 'applied')` | the quiet case's healthy engine |
  | `:455` | `vi.fn(() => false)` | `vi.fn((): PlayoutTargetOutcome => 'unsupported')` | the flap case, same reason as `:344` |

  `setPlayoutTargetThrows` (`:37`) is **unchanged** — a function that only throws
  infers `never`, which is assignable to `PlayoutTargetOutcome`.

  **Every `expect(...)` line in this file stays byte-identical.** This is plan
  R1: a retyped double is not an edited assertion, and the diff is the proof.
  Verify with `git diff -U0 -- <file> | grep '^[-+].*expect('` returning nothing.
  Depends on **T009** (same file as T005).

---

## Phase 5 — what the verification note must contain

- **The four counterfactuals**, each re-run after the fix, each reported with the
  count of failures it produced:
  1. `outcome = 'applied'` → `return 'applied'` — must fail **T004 case 2**, alone.
  2. the report condition widened to `outcome !== 'applied'` — must fail **T005**,
     alone, at its length assertion.
  3. `if (!response.ok)` → `if (false)` in `releaseSession` — must fail **T002
     cases 2 and 3**, and nothing else.
  4. T003 case 3's audio receiver's kind guard removed — must fail **T003 case 3**
     on the written value, proving the kind guard is live.
- **The three suites' counts** (shared / kiosk-web / management-web), and the
  statement that kiosk-web's is **unchanged from develop**.
- **The MediaMTX figure, and what kind of claim it is.** If the stack was booted
  and `GET /v3/webrtcsessions/list` was watched after a killed kiosk tab, quote
  the observed drain time and say *measured*. If not, say *derived at the pinned
  tag, not observed* and leave spec A1/A2 standing. **Do not upgrade a derivation
  to a measurement** — §IV's leg table went wrong in exactly that way, twice.
- **Latency: N/A**, with FR-009's argument, and **no invented baseline** — #1714
  is why.

## Phase 3 gate

**Satisfied.** The feature issue **#2198** is already on Project #13 — verified
with `gh project item-list 13 --owner smartsolutionslab --limit 2000 --format
json` and a match on `content.url` (the number filter returns zero; the default
`--limit 30` makes a filled board look empty). No `item-add` was needed.

Per-task issues are **not** created (CLAUDE.md, phase 3 — feature-level since
spec 028). Verify with `--limit 2000`; the default 30 makes a filled board look
empty.
