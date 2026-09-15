# Spec 159 — a settle that waits for the state

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2386](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2386) · **Branch:** `fix/2386-camera-swap-tests-wait-for-the-condition`
**Lane:** autonomous (ADR-0144) — `#2386` carries `agent:ready`.
**ADRs:** ADR-0139 (rules that fail the build, not the review — §Testing's two
obligations), ADR-0144 (the lane; phase 4a's two colours), ADR-0052 (test stack),
ADR-0053 (test naming), ADR-0075 (RTK Query — whose real machinery this file
deliberately drives), ADR-0074 (two apps; the composite is shared), ADR-0109
(`[P]` marking), ADR-0037 (the phased workflow itself).
**Constitution:** §Testing (two obligations — this is the *behaviour-preserving*
one), §IV (latency budget — **N/A**, see below).

**Latency budget (§IV): N/A. No leg is touched.** No production file changes, so
no cell of the §IV table moves and no measurement is owed. The file under change
is a vitest suite; it runs in CI, never on a wall.

---

## The finding

`apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx` fails on CI and
passes locally. Four CI runs, and **the green is the outlier**:

| Run | PR | Base | Frontend bucket |
|---|---|---|---|
| 34942048886 | #2383 | `44ee5737` | **pass** |
| 34943426027 | #2384 | `70ed02b2` | 3 failed |
| 34952180803 | #2384 (rebased) | `37011568` | 2 failed |
| 34956788262 | #2390 | `37011568` | **4 failed** |

PR #2390's diff touches only `apps/management-web/**` and `specs/158-**` — nothing
under `apps/shared`. The cause is therefore not any PR's change, and this file is
now blocking **every** frontend PR, including #2382 (via PR #2384, `agent:blocked`)
and #2379 (via PR #2390, parked).

---

## The question this spec had to settle first: the component, or the test?

The failing assertions are of the form *"`Connecting…` is gone and an error is
shown"*. If `CameraViewer` can genuinely sit on `Connecting…` under some
interleaving, the correct outcome is a **production fix**, and making the test
wait longer would paper over a defect an operator would eventually meet on a
loaded wall. Issue #2157 already records that `useWhepSession`'s dual-owner state
machine wants restructuring, so a genuine race there would not have been a
surprise.

**It is the test.** The evidence, in four parts.

### 1. The CI failure reproduces locally, exactly, with no component change

`flushConnect()` (`CameraViewerCameraSwap.test.tsx:240-250`) settles by a **fixed
count of macrotask yields** — ten `setTimeout(resolve, 0)` turns, each followed by
five microtask drains — and the assertions then run synchronously. Reducing that
count from `10` to `1`, changing nothing else anywhere, produces on this machine:

```
 × Never resolves to camera A when every stream read for camera B fails
 × Never resolves to camera A when the gateway refuses camera B's stream read with 403
 × Reads "Stream is offline" rather than Connecting when the new camera answers Offline
 × Reads Viewer error, not Idle, on a first mount whose stream read fails
 Tests  4 failed | 3 passed (7)
```

— the same four tests, in the same order, with the same messages
(`AssertionError: expected <span …(1)></span> to be null`,
`TestingLibraryElementError: Unable to find an element with the text: Stream is offline.`,
`… Viewer error`) as CI run 34956788262. The knob that reproduces CI is the
settle budget, and nothing else.

### 2. The outcome is monotone in the budget, and stochastic only at the margin

| `flushConnect` budget | Local result |
|---|---|
| 1 | **4 failed** (deterministic, repeated) |
| 2 | **1 failed** on one run, **2 failed** (a different pair) on the next |
| 3 | 7 passed |
| 4 | 7 passed |
| 6 | 7 passed |
| 10 (shipped) | 7 passed |

This is the signature of **latency**, not of a race. The asserted state is reached
and then *stays* reached — every budget at or above the threshold passes, and
nothing above it ever fails. A genuine race would show a state that can be missed
regardless of how long one waits.

The budget-2 row is the important one: **the same budget on the same machine gives
different failing sets on consecutive runs.** That is precisely CI's observed
pattern of 3, 2, 4 and once 0 — a margin crossed stochastically, not a branch-
specific perturbation. It also explains #2383's green, which the issue rightly
refused to treat as conclusive.

### 3. The fourth failing test contains no swap, no effect and no state machine

`Reads Viewer error, not Idle, on a first mount whose stream read fails` is one
mount and one failed read. The label it asserts is a **pure render-time
derivation** — `CameraViewer.tsx:387`:

```ts
const failedRead = stream === undefined && queryError !== undefined && status !== 'offline';
```

No effect, no timer, no `transitionTo`, no dual ownership participates. The only
thing between `render()` and `Viewer error` is *"has RTK Query's rejected action
reached the store yet"*. No component race can explain this test failing, and any
explanation that does not cover it is not an explanation of the file's failure.

### 4. Why a *fixed count of turns* is load-dependent at all

This is the part worth writing down, because "ten turns is ten turns regardless of
CPU speed" is the obvious objection and it is wrong here.

Unlike every other viewer suite in the repo, this file drives the **real**
`useGetStreamQuery`, the real `fetchBaseQuery` and real Node `Response` objects
(that is the whole point of the file — see its own header comment; a mocked hook
cannot express `data` vs `currentData`). Consuming a `Response` body advances
through Node's **poll and check** phases; `setTimeout(resolve, 0)` callbacks fire
in the **timers** phase. A fixed count of timers-phase turns is not a bound on a
chain that advances in other phases: under contention a timer callback can fire
before the pending stream work has been delivered, so the same logical chain
consumes a varying number of `setTimeout(0)` rounds.

Commit `44ee5737` already wrote this reasoning into this very file, one layer up,
when it replaced `goLive`'s fixed count with `waitForNewPeerConnection`'s
deadline poll. The remaining callers were not converted. This spec finishes that.

### The two genuine-race candidates, examined and disproved

Both are recorded rather than waved away, because "the test is impatient" is the
convenient answer.

**(a) Does a poll refetch flash the tile back to `Connecting…`?** `failedRead`
requires `queryError !== undefined`. The 5 s poll re-issues the read every 5 s,
and if RTK Query cleared `error` while the refetch was pending, the tile would
read `Connecting…` for the duration of each refetch — a real defect, and one that
would make the post-`realWait(5200)` assertion at `:440` legitimately flaky.
It does not. `writePendingCacheEntry`
(`@reduxjs/toolkit@2.12.0`, `dist/query/rtk-query.modern.mjs:1281-1303`) sets
`status`, `requestId`, `originalArgs` and `startedTimeStamp`, and **never touches
`substate.error`**. The error survives the pending refetch, the label stays
`Viewer error`, and the assertion is sound.

**(b) Can the warm-cache effect ordering leave the tile on `Connecting…`?** Yes —
and it is already pinned. `useWhepSession.ts:152-189` documents that both effects'
setups call `transitionTo` in the same commit when camera B's data is warm, and
that the camera-change effect is declared *first* so `'offline'` is the last
writer. That ordering is deterministic under React's model (declaration order), is
guarded by the *"Reads Stream is offline, not Connecting forever, when camera B is
already warm in the cache"* test, and that test is **not among the failures** at
any budget. Nothing here reopens it.

**Conclusion: no production change. `useWhepSession.ts` and `CameraViewer.tsx` are
not touched by this spec, and #2157 is neither supported nor weakened by it.**

---

## Blast radius: exactly one file, and it was checked rather than assumed

Seven test files in `apps/` install a `globalThis.fetch` stub. Six of them
**mock `useGetStreamQuery`** and settle with a **microtask-only** drain
(`await Promise.resolve()` × 12), which is sound: a microtask drain of *N* rounds
settles any chain of at most *N* microtask-only awaits, with no wall-clock
dependence at all.

| File | Query hook | Settle | Latent? |
|---|---|---|---|
| `apps/shared/.../CameraViewer.test.tsx` | mocked (`:7-9`) | microtasks | no |
| `apps/shared/.../CameraViewerMedia.test.tsx` | mocked (`:53`) | microtasks | no |
| `apps/shared/.../FrameCapture.test.tsx` | mocked (`:103`) | microtasks | no |
| `apps/shared/.../OverlayEditorBackdrop.test.tsx` | mocked (`:100`) | microtasks | no |
| `apps/management-web/.../OverlayEditorDialog.test.tsx` | mocked (`:88`) | microtasks | no |
| `apps/shared/src/streaming/WhepClient.test.ts` | n/a | **one** `setTimeout(0)` | no — see below |
| **`apps/shared/.../CameraViewerCameraSwap.test.tsx`** | **real** | **10 × `setTimeout(0)`** | **this is it** |

`WhepClient.test.ts` is the only other file using a real macrotask yield. It uses
exactly **one**, after `client.close()`, against a `vi.fn()` fetch resolving a
pre-built `Response` whose body the DELETE path never reads (`.status` only). A
single macrotask boundary drains the entire microtask queue, so one turn is a
genuine bound there, not an assumption. **Excluded with a reason, not by
omission.**

So the scope is **one file and its helpers** — not lines 427/459/479, and not a
follow-up sweep. There is no latent twin to defer.

---

## User stories

### US1 (P1) — a frontend PR is reddened only by its own change

**As** anyone delivering a frontend change (a person or the autonomous lane),
**I want** `CameraViewerCameraSwap.test.tsx` to fail only when the behaviour it
describes is broken, **so that** a red frontend bucket is information rather than
noise, and a PR that touches nothing under `apps/shared` is not blocked by it.

This is the whole slice. It is independently shippable, observable end to end (the
CI frontend bucket), and there is no P2.

**Why it matters beyond the inconvenience:** a spurious red costs a full re-run of
a check set whose e2e bucket alone takes ~30 minutes, and — the worse cost — it
trains the next reader to dismiss a frontend red without reading it. Two
deliveries are blocked on it today.

---

## Functional requirements

**FR-001 — no assertion is reached by a fixed count.** No assertion in
`CameraViewerCameraSwap.test.tsx` about component state that depends on a real
network read may be preceded solely by `flushConnect()`. Each such assertion is
reached through a **deadline-bounded condition wait** that polls the actual
condition.

**FR-002 — each wait names the positive state the test is about.** Waiting for an
*absence* (`Connecting…` is gone) proves nothing — an absence is also true before
anything has rendered. Every wait names the state that must **appear**:
`Viewer error` / `Could not reach the streaming service.` for the two failed-read
scenarios, `Stream is offline` for the offline scenario, `Viewer error` for the
first-mount scenario. The negative assertions (`queryByText('Connecting…')` is
null, `srcObject` is null) are then asserted immediately after, **in the same
commit** as the positive — the label and its hint render from one `ViewerOverlay`
call, so once the positive is observed the negative is settled deterministically.

This is a strengthening, not merely a delay. The 403 scenario currently asserts
*only* absences (`:459-460`); it gains the positive `Viewer error` assertion it
never had.

**FR-003 — `goLive`'s internal settle is a condition wait too.** `goLive`
(`:305-316`) waits for the peer connection by deadline (`44ee5737`) and then calls
`flushConnect()` before firing `ontrack`. That second settle is the same fixed
count used as a synchronisation primitive — it must instead wait for the condition
it actually needs: the WHEP `POST` to the camera's URL having been issued.

**FR-004 — `flushConnect` survives only as a driver.** It may advance the fakes;
it may not be the last thing before an assertion. Operationally: **with every
remaining fixed-count loop in the file reduced to a single macrotask yield, the
suite must still be 7/7 green** (SC-001). This is the requirement that a bigger
fixed count cannot satisfy.

**FR-005 — `realWait(5200)` keeps its wall-clock elapse.** The 5 s RTK Query poll
is real machinery this file does not own and must not fast-forward. The wait stays;
only its trailing settle becomes a condition wait.

**FR-006 — no assertion's meaning may be weakened.** No test deleted or skipped,
no assertion removed, no `getByText` loosened to a regex that would also match the
wrong state, no `queryByText('Connecting…')` dropped, no suite-level `retry` added.
A raised timeout is not a fix and does not satisfy FR-001. (ADR-0144: the lane may
not weaken a gate to reach green.)

**FR-007 — a timeout says what it was waiting for.** Each deadline failure names
the condition and the budget, in the shape `waitForNewPeerConnection` already uses
— so the next CI red is diagnosable from the log rather than from a re-run.

**FR-008 — no production file changes.** `CameraViewer.tsx`, `useWhepSession.ts`,
`WhepClient.ts` and `streams.api.ts` are untouched.

---

## Acceptance scenarios (Gherkin)

### Happy path — the file is green as it is

```gherkin
Given the unmodified test file on this branch
When `pnpm vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx` is run in apps/shared
Then 7 of 7 tests pass
```

### The discriminator — green under a budget of one

```gherkin
Given a copy of the FIXED file with every remaining fixed-count settle loop reduced to a single macrotask yield
When that copy is run five consecutive times
Then all five runs are 7/7 green
```

### The counterfactual — the same harness reds the unfixed file

```gherkin
Given a copy of the UNFIXED file with flushConnect's loop reduced to a single macrotask yield
When that copy is run
Then exactly four tests fail
And they are the four named in CI run 34956788262
```

### Conflict — a real regression must still be caught

```gherkin
Given the fixed test file
And `CameraViewer.tsx`'s `failedRead` derivation is temporarily inverted so a failed read never shows an error
When the suite is run
Then the failed-read scenarios fail at their deadline
And the failure names the state that never appeared, not a stale-DOM assertion
```

### Bad request — an offline camera still reads offline

```gherkin
Given the fixed test file
When the swap scenario answers camera B's read with state Offline
Then the tile is observed to read "Stream is offline" and "Source powered down."
And `Connecting…` is absent and `srcObject` is null at that same observation
And no WHEP POST was issued for camera B
```

### Authorisation — the 403 scenario gains its positive

```gherkin
Given the fixed test file
When the gateway refuses camera B's stream read with 403
Then the tile is observed to read "Viewer error"
And `Connecting…` is absent and `srcObject` is null at that same observation
```

### Regression — the shapes that are not defects

```gherkin
Given the fixed test file
Then the warm-cache effect-ordering guard still passes unmodified
And the "unrelated re-render with a new getToken closure" test still passes unmodified
And `realWait(5200)` still elapses 5 s of wall clock
```

---

## Independent end-to-end test procedure

Runnable by a reviewer with no context beyond this file.

1. `cd apps/shared && npx vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx`
   → **7 passed**.
2. `sed '<flushConnect line>s/i < 10/i < 1/' src/ui/composites/CameraViewerCameraSwap.test.tsx > src/ui/composites/ScratchBudget.test.tsx`
   on the file **as it stands before the fix**, run it → **4 failed**, matching CI.
3. Apply the fix. Repeat step 2's reduction against the fixed file — reducing
   every remaining fixed-count loop to `1` — run it **five times** → **7 passed**
   each time.
4. `rm src/ui/composites/ScratchBudget.test.tsx`, then
   `pnpm --filter @smart-sentinel-eye/shared test` → whole package green.
5. Push; the CI frontend bucket is green. **Confirmation, not the gate** — see
   §Phase 4a.

---

## Success criteria

- **SC-001** — a copy of the fixed file with every fixed-count settle reduced to a
  single macrotask yield is 7/7 green on five consecutive runs.
- **SC-002** — the same reduction against the unfixed file produces exactly the
  four CI-named failures. (Captured **before** the fix; it is the counterfactual.)
- **SC-003** — the unmodified fixed file is 7/7 green.
- **SC-004** — `pnpm --filter @smart-sentinel-eye/shared test` green; format, lint
  and typecheck green.
- **SC-005** — the CI frontend bucket is green on the PR, and on the next frontend
  PR that follows it. Phase 5 confirmation.
- **SC-006** — no file outside `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`
  and `specs/159-*/` appears in the diff.

---

## Locked technical choices consumed

| Concern | Choice | ADR |
|---|---|---|
| Test framework | vitest + `@testing-library/react` | ADR-0052 |
| Test naming | sentence-style (existing names unchanged) | ADR-0053, ADR-0062 |
| Frontend state | RTK Query — real, deliberately, in this file | ADR-0075 |
| Shared composite | `apps/shared`, consumed by both apps | ADR-0074 |
| Waiting idiom | deadline poll, as `waitForNewPeerConnection` (`44ee5737`); `findBy*`/`waitFor` is the repo-wide precedent in 20+ suites | — |

Nothing new is introduced. The fix is the *existing* idiom from this same file,
applied to the callers `44ee5737` did not reach.

---

## Latency-budget impact (constitution §IV)

**N/A — no leg, no cell, no measurement owed.** No production file changes; the
change is confined to a vitest suite. §VII's dashboard rule (ADR-0117) binds
implemented legs and is not engaged, because nothing on any leg moves.

Recorded explicitly rather than omitted: §IV's table has twice been wrong by
clerical drift, and "N/A because nothing production-side changed" is a claim a
reviewer can check against SC-006's diff constraint.

---

## Is a new ADR needed? No.

Three candidate decisions were considered and none is architectural:

1. *"Waiting on a condition rather than a yield count"* — not a new decision.
   §Testing already requires tests that fail for the behaviour they describe, and
   `44ee5737` already applied this idiom in this file. Restating it as an ADR
   would be recording a repair as a decision.
2. *"A lint rule banning a fixed-count settle before an assertion"* — that would
   be a new enforcement mechanism and **would** need an ADR (ADR-0139's
   enforce-versus-advise question). It is therefore **out of scope**; see below.
3. *"Restructuring `useWhepSession`'s dual-owner state machine"* — #2157's
   territory, and this spec's diagnosis found no evidence for or against it.

**The lane is not blocked.** No ADR is written, and none is required to proceed.

---

## What this spec does not take

- **No lint rule.** A rule forbidding `flushConnect()` immediately before an
  assertion is attractive and is exactly the shape ADR-0139 says to consider —
  but it is a new enforcement mechanism, it needs an ADR the lane may not write,
  and it has one call site. **File it as a follow-up** (tasks §Follow-ups);
  extract at the second site, per the repo's own standing practice.
- **No sweep of the microtask-drain suites.** Six were checked and none is latent
  (§Blast radius). A drive-by conversion would be change without a finding.
- **No production change**, including the one a reader might expect: nothing about
  `useWhepSession` is altered, and #2157 remains open on its own merits.
- **No `retry` in the vitest config.** It would hide exactly the class of defect
  this file exists to catch.

---

## Contradictions with the issue text, listed

1. **"Three assertions"** (title and body) — there are **four** failing tests, and
   the fourth is not a camera swap. The issue's third comment already records
   this; the title is stale. Scope is the file, not lines 427/459/479.
2. **"`goLive`'s path is not involved"** (implied by the body's "the three failing
   assertions never go through that path") — true of `waitForNewPeerConnection`,
   but `goLive` still calls `flushConnect()` before firing `ontrack`. FR-003
   converts it.
3. **"confirmed by CI going green"** (third comment) — CI green is confirmation,
   not evidence. #2383's green was exactly that evidence and it was not
   conclusive. SC-001/SC-002 are the gate.

## Assumptions and decisions, marked

- **Assumed:** the CI runner's contention margin sits near the local budget-2
  point. Not measurable from here; it is not load-bearing — SC-001 removes the
  budget as a variable entirely rather than sizing it.
- **Decided:** the discriminating harness uses budget **1**, not 2, because
  budget 2 was observed to give different failing sets on consecutive runs and a
  stochastic discriminator discriminates nothing.
- **Decided:** `realWait`'s 5 s elapse stays real. Faking it would make the
  polling scenario test a timer this file does not own.
