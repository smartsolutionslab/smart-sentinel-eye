# Plan — Spec 159, a settle that waits for the state

**Phase:** 2 (Plan) — ADR-0037
**Issue:** [#2386](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2386) · **Branch:** `fix/2386-camera-swap-tests-wait-for-the-condition`
**Spec:** `spec.md`
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour: BEHAVIOUR-PRESERVING (characterisation, observed GREEN)** —
plus a mandatory counterfactual, because green alone cannot discriminate here.
See §7.

---

## 1. Bounded context and layers — there are none

This change lives entirely in `apps/shared`'s **frontend test layer**. No bounded
context, no domain, no aggregate, no value object, no messaging, no persistence,
no boundary rule is engaged. `Shared.Contracts` is not touched; NetArchTest has
nothing to say. Recorded so the absence is deliberate rather than an omission the
reviewer has to chase.

**One file changes:**

```
apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx
```

**Plus this spec directory.** Nothing else (SC-006).

---

## 2. The defect, in one sentence

`flushConnect()` spends a **fixed count of timers-phase turns** and is then used
as if it were a *bound* on a chain that advances through Node's **poll and check**
phases — the real `fetchBaseQuery` reading a real `Response` body. It is an
assumption, and under CI contention it is false.

The repair is the one this file already contains, applied to the callers commit
`44ee5737` did not reach: **poll the actual condition against a deadline.**

---

## 3. The shape of the change

### 3a. One waiting idiom, generalised from the one already here

`waitForNewPeerConnection` (`:290-317`) is the proven shape: a real-timer poll
inside `act`, against a deadline, with a message that names what it was waiting
for. Generalise it rather than inventing a second idiom:

- add `waitUntil(condition: () => boolean, description: string, timeoutMs = 4000)`
  — polls on `await act(async () => { await new Promise(r => setTimeout(r, 10)); })`,
  throws `Timed out after Nms waiting for <description>.` on the deadline (FR-007);
- re-express `waitForNewPeerConnection` in terms of it, **preserving its existing
  message verbatim** (it names the had/have counts, which is diagnostically
  better than a generic line — keep that).

**Why `waitUntil` and not `findBy*`/`waitFor`.** `findBy*` is the repo-wide
precedent (20+ suites) and would be the default choice. It is *not* the default
choice here, for two reasons:

1. **It only reaches DOM conditions.** Half of what this file waits on is not DOM
   — `srcObject`, `FakePeerConnection` state, the RTK Query cache, `fetchMock`
   calls. Two idioms in one file for one concept is how the next reader picks the
   wrong one.
2. **`waitUntil`'s loop is known to advance *this* chain.** It is byte-for-byte
   the drive that `flushConnect` and `waitForNewPeerConnection` already perform
   (`act` + real `setTimeout`), so its ability to move a real-`fetchBaseQuery`
   chain forward is already demonstrated in this file. RTL's `waitFor` polls on
   its own interval and should behave the same, but "should" is what this issue
   is about.

`findBy*` remains acceptable **if and only if** SC-001 holds with it. SC-001 is
the arbiter, not this preference.

### 3b. Per-assertion conversions

| # | Test | Change |
|---|---|---|
| 1 | Stops showing camera A the instant the tile is reassigned | **assertions unchanged** — see §4. Only `goLive`'s internals move. |
| 2 | …every stream read for camera B fails | wait for `Could not reach the streaming service.`; re-observe `Viewer error` after `realWait(5200)` |
| 3 | …the gateway refuses with 403 | wait for `Viewer error` — **and assert it**, which this test never did |
| 4 | Reads "Stream is offline" rather than Connecting | wait for `Stream is offline` |
| 5 | …camera B is already warm in the cache | wait for the **cache to actually be warm** — see §3d |
| 6 | Reads Viewer error, not Idle, on a first mount | wait for `Viewer error` |
| 7 | Keeps the session across a new `getToken` closure | **`flushConnect()` stays** — see §3e |

In every converted case the wait names the state that must **appear** (FR-002),
and the negative assertions (`Connecting…` absent, `srcObject` null) follow it
synchronously. That ordering is sound because label and hint are rendered by one
`ViewerOverlay` call (`CameraViewer.tsx:398-403`): once the positive is on screen,
the negative is settled in the same commit, not merely "probably also true by now".

### 3c. `goLive` — wait for the connect chain, not for a count (FR-003)

`goLive` (`:305-316`) already waits for the peer connection by deadline, then
calls `flushConnect()` before firing `ontrack` and `connected`. That second settle
is the same defect: the connect chain it is waiting on is
`createOffer → setLocalDescription → WHEP POST → setRemoteDescription`, which
crosses the real `fetch`.

Make the condition observable on the fake and wait for it:

- `FakePeerConnection` gains `remoteDescriptionSet = false`, set `true` in
  `setRemoteDescription()`;
- `goLive` waits for `pc.remoteDescriptionSet` instead of `flushConnect()`.

That is the exact condition — the answer has been applied — rather than a proxy
for it. A fixture field, not a production change.

**Risk and its detector:** if something after `setRemoteDescription` must also
complete before `ontrack` is safe to fire, this wait is too early. The fake's
`iceGatheringState` is `'complete'` from construction, so nothing obvious
remains — and SC-001 is the detector: a budget-1 harness that is still green
proves no residual count is being relied on. If SC-001 reds here, add a second
condition (the WHEP `POST` for that URL having been issued) rather than restoring
the count.

### 3d. The warm-cache premise, which can currently evaporate in silence

Test 5 warms camera B into the RTK Query cache with:

```ts
const warm = render(viewerFor(CAM_B));
await flushConnect();
warm.unmount();
```

If that `flushConnect()` runs out before camera B's read lands, the cache is
**not** warm, `warm.unmount()` discards an in-flight entry, and the test goes on
to exercise the *cold* ordering while claiming to exercise the warm one. It
would still pass — it passes at budget 1 today — while testing something else.
That is worse than a red: a guard that silently stops guarding is the failure mode
this repository has had to correct repeatedly.

Wait for the premise instead:

```ts
await waitUntil(
  () => streamsApi.endpoints.getStream.select(CAM_B)(store.getState()).data !== undefined,
  'camera B to be warm in the RTK Query cache',
);
```

`streamsApi` and `store` are both already in scope. This is a **strengthening**,
not a delay, and it is the one place where the fix makes a test able to fail that
previously could not.

### 3e. Where a fixed count is the right tool, and stays

Test 7 asserts that a re-render with a fresh `getToken` closure changes
**nothing** — `pcA.closed` is still `false`, `srcObject` is unchanged, one POST.
There is no condition to wait for: the assertion is about the *absence* of an
event, and a condition wait cannot wait for a non-event. The correct instrument is
a bounded drive that gives the misbehaviour a chance to occur — which is exactly
what `flushConnect` is for.

So `flushConnect` keeps its budget of 10 and keeps this call site. FR-004 is
about it never being the last thing before an assertion **about a state that has
to arrive**; it says nothing against driving.

**The honest wrinkle, recorded rather than smoothed over:** SC-001's harness
reduces *every* remaining loop to one yield, including this one, which weakens
test 7's probe for the duration of the harness run. That is acceptable and does
not undermine SC-001, because the harness proves a different property — *no
assertion depends on the count* — and test 7's assertions are satisfied by a weak
probe trivially. The probe strength that actually ships is the unmodified file's
budget of 10.

---

## 4. Invariants this change must not move

1. **Every test name is unchanged.** Names are the contract with CI's log and with
   this issue.
2. **No assertion is removed or loosened** (FR-006). Assertions are *added* (the
   403 positive, the warm-cache premise) and none is subtracted.
3. **Test 1's post-rerender assertions stay synchronous.** `rerender` is
   act-wrapped, so React flushes the camera-change effect's cleanup and setup
   before it returns: `pcA.closed`, `srcObject === null`, `Connecting…` present
   and `instances.length === 1` are all true with **zero** yields. Wrapping them
   in a wait would make a deterministic assertion look conditional and would mask a
   regression that made them late. Leave them.
4. **`realWait(5200)` keeps its real elapse** (FR-005). Only its trailing
   `flushConnect()` is replaced by the caller's condition wait.
5. **Real timers throughout.** No `vi.useFakeTimers()`; the file's header comment
   explains why and it still holds.
6. **No `retry` in vitest config, no `test.retry`, no `it.skip`.**
7. **No production file is touched** (FR-008).

---

## 5. Messaging, persistence, boundaries

None. No domain event, no integration event, no migration, no `Shared.Contracts`
change, no cross-context reference. Stated so the reviewer can stop looking.

---

## 6. Why this discriminates a real fix from one that merely passes

The failure mode this spec must avoid is a change that raises the count, passes
locally, passes CI once, and leaves the mechanism intact — which is exactly what
#2383's green looked like.

The discriminator is **SC-001**: a copy of the fixed file with *every* remaining
fixed-count loop reduced to a single macrotask yield must be 7/7 green, five
times running.

- A fix that raises the budget to 100 **fails** SC-001 — the harness reduces
  whatever count is there to 1.
- A fix that adds `retry: 2` **fails** SC-001 deterministically at budget 1.
- A fix that deletes or loosens an assertion **fails** SC-006 and review.
- A fix that genuinely waits on the condition **passes at any budget**, because
  the budget has stopped being load-bearing.

And **SC-002** — the same harness reding exactly the four CI-named tests against
the *unfixed* file, captured before any edit — is what makes the green meaningful
rather than vacuous.

---

## 7. Phase 4a — colour, and what "observed" means here

**Colour: BEHAVIOUR-PRESERVING → characterisation, observed GREEN.**

The production behaviour of `CameraViewer` and `useWhepSession` is unchanged and
must stay unchanged; the artifact being edited is itself the test. The standard
characterisation rule therefore applies with an unusual twist: the covering tests
*are* the file being changed, so "pass unmodified afterwards" cannot be the whole
of the evidence — the whole point is that the file is modified.

So phase 4a must produce **three artefacts**, in this order, all quoted verbatim
in the PR:

1. **BEFORE, unmodified — 7/7 green.** The baseline the change must not break.
2. **BEFORE, budget-1 harness — exactly 4 failures**, matching CI run
   34956788262's four names. This is the counterfactual: it demonstrates that the
   CI failure is reproducible locally from the settle budget alone, with no
   component change. **Captured before any edit to the real file.**
3. **AFTER, budget-1 harness — 7/7 green, five consecutive runs**, plus the
   unmodified fixed file 7/7 green.

Artefact 2 is what replaces the red this change cannot have. A local red is
unavailable because local is already green; an artificially induced red under a
reduced settle budget is the strongest evidence available, it mirrors how #2371
and spec 158 proved their pins, and unlike "CI went green" it can be produced
**before** the commit and re-run by a reviewer in under a minute.

**CI green is confirmation, not the gate.** It arrives after the push, it is the
evidence that already misled once, and it is phase 5's business (SC-005).

**If the engineer finds an assertion that cannot be made to pass at budget 1**,
that is a finding, not an obstacle: it means the state genuinely is not reached
by that path, the diagnosis in `spec.md` §*The question this spec had to settle*
is wrong for that assertion, and the run must **stop and report** rather than
restore a count. A production race would make this behaviour-**changing** and owe
a red.

---

## 8. Rejected alternatives

| Alternative | Why not |
|---|---|
| Raise `flushConnect`'s count to 50 | Buys time, keeps the mechanism, fails SC-001. The count is unfalsifiable — nobody can say what number is enough, which is the defect. |
| `retry: 2` in the vitest config | Hides this class of defect everywhere, permanently. ADR-0144 forbids weakening a gate to reach green. |
| Convert the six microtask-drain suites too | Checked; none is latent (spec §Blast radius). Change without a finding. |
| Add an ESLint rule banning `flushConnect()` before an assertion | A new enforcement mechanism needs an ADR (ADR-0139) which the lane may not write, and it has one call site. Filed as a follow-up. |
| Fake the 5 s poll timer | It is RTK Query's machinery, not this file's, and faking `setTimeout` also removes the macrotask yield the connect chain needs (the file's own header comment). |
| Restructure `useWhepSession`'s dual ownership | #2157's territory. This diagnosis found no evidence it is implicated, and a production change here would need a red. |

## 9. Risks

1. **`waitUntil` does not advance the chain in some scenario.** Low — it is the
   same drive as `flushConnect` and `waitForNewPeerConnection`. Detected by SC-001
   at budget 1. Mitigation: deepen the condition, never restore a count.
2. **`goLive`'s `remoteDescriptionSet` wait fires too early** (§3c). Detected by
   SC-001; mitigation stated there.
3. **A converted wait hides a real regression by waiting for the wrong thing.**
   Mitigated by FR-002 (wait for a *positive*, specific state) and by the
   inverted-`failedRead` acceptance scenario, which a reviewer can run.
4. **File length.** The file is 578 lines; the change is roughly net-neutral
   (a helper added, per-site `flushConnect` calls replaced one-for-one). The
   300-LOC metric limit (ADR-0084, SonarAnalyzer) applies to C#, not to vitest
   suites; no frontend gate is engaged.
