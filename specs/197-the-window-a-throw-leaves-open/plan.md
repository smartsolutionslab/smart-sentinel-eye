# Plan 197 — The window a throw leaves open

**Phase:** 2 (Plan) — ADR-0037
**Spec:** [`spec.md`](./spec.md)
**Issue:** [#2314](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2314)
**ADRs:** ADR-0128, ADR-0129, ADR-0122, ADR-0117, ADR-0074, ADR-0139, ADR-0144, ADR-0109, ADR-0036

---

## Not a bounded context — the frontend shared package

This change is entirely in TypeScript/React. There is **no** .NET bounded
context, no domain model, no value object, no entity, no aggregate, no
messaging, and no persistence. The DDD sections of the plan template do not
apply and are answered below rather than omitted, so the absence is a recorded
claim rather than a gap.

| | |
|---|---|
| **Package** | `apps/shared` (`@smart-sentinel-eye/shared`) |
| **File changed** | `apps/shared/src/ui/composites/CameraViewer.tsx` — **one file** |
| **File added** | `apps/shared/src/ui/composites/CameraViewerSamplerWindow.test.tsx` |
| **Files read, not changed** | `apps/shared/src/observability/wallAlignment.ts`, `apps/shared/src/observability/kioskLatency.ts`, `apps/kiosk-web/src/features/cell/useWallAlignment.ts`, `apps/kiosk-web/src/features/cell/CellPage.tsx` |
| **Backend** | untouched. `src/` is not opened by this spec |
| **Public API** | unchanged. `CameraViewerProps` keeps the same members and the same types |
| **New dependencies** | none |
| **New exports** | none |

### Why the test goes in a new file rather than into `CameraViewerAlignment.test.tsx`

Three reasons, and the first is the binding one.

1. **`CameraViewerAlignment.test.tsx` must pass unmodified.** It carries
   #2189's four cases, and those are the regression guard that the reset does
   not re-swallow the throw. ADR-0144's phase-4 rule is that an assertion which
   has to be edited is evidence the behaviour moved; keeping the new cases
   physically out of that file makes "unmodified" checkable by `git diff`
   rather than by reading.
2. **The fixture is incompatible.** That file's module-scope
   `advancingVideoStat()` advances by *constant* increments, and this spec's
   cases need a **stepped** rate (see below). Adding a second fixture to a file
   whose `beforeEach` resets to the first invites the two to be confused.
3. **ADR-0109 disjoint-file parallelism.** A separate file lets the new cases
   and any later work on the alignment suite proceed without contention.

The existing file is still executed unchanged as part of the gate.

---

## The change

Two one-line edits, one per sampler, in the `.catch` that #2189 already
attached to each async tick.

### Lag sampler — `CameraViewer.tsx:300`

```ts
// today
})().catch((error: unknown) => reportSamplerFailure(lagSampleFailuresRef, 'lag-sampler-failed', error));
```

```ts
// after
})().catch((error: unknown) => {
  // A tick that threw never reached `previous = current`, so the window it
  // opened is still open. Left alone, a callback (or a `getStats`) that keeps
  // throwing pins `previous` for the whole outage, and the first tick that
  // succeeds afterwards reports a per-frame mean over all of it — the
  // cumulative session average `lagBetween` exists to avoid (#2314).
  previous = null;
  reportSamplerFailure(lagSampleFailuresRef, 'lag-sampler-failed', error);
});
```

### Decode sampler — `CameraViewer.tsx:227`

The same, with `decodeSampleFailuresRef` / `'decode-sampler-failed'`, and a
comment that points at the lag sampler rather than repeating the argument.

---

## Why the outer `.catch` and not a dedicated try/catch around the callback

#2314 proposes resetting "inside the catch block for the callback's own try".
That would need a **new** try/catch plus a rethrow to keep #2189's surfacing:

```ts
try { onLagMeasuredRef.current?.(cameraIdentifier, lag, buffered); }
catch (error) { previous = null; throw error; }
```

Rejected, for four reasons:

1. **It misses half the defect.** `stats()` throws into the same `.catch` and
   pins `previous` identically. The repo has written down what happens when only
   the filed half is fixed (`kioskLatency.ts`, `missingDecodeFieldIn`: *"fixing
   one and not the other would leave the identical silence one file over"*).
2. **Bigger diff, more moving parts.** A rethrow that must be preserved exactly
   is a thing a later edit can break silently; `previous = null` in the existing
   handler cannot.
3. **It puts caller-shaped control flow in the middle of the measurement.** The
   `if (previous !== null)` block is the arithmetic; the `.catch` is where this
   effect already decides what a failed tick means.
4. **The `.catch` is already the single failure path for this tick.** Both
   throw sources converge there, and #2189 chose that shape deliberately.

**What the outer `.catch` does NOT change**, and this is the load-bearing
compatibility claim: `reportSamplerFailure` is called with the same counter, the
same transition and the same error, on the same decade cadence. The reset is a
sibling statement, not a wrapper. Nothing about #2189's loudness or its
callback-vs-`getStats` distinguishability is touched.

---

## State, invariants, and what actually changes

`previous` is a plain `let` in the effect closure — a two-state machine.

| state | meaning | reached by |
|---|---|---|
| `null` | **no open window**; the next reading only seeds | effect start; **(new)** a thrown tick |
| `LagSample` / `DecodeSample` | a window is open from this sample | the end of a successful tick |

**The invariant this restores**, stated so it is checkable:

> The sample pair handed to `lagBetween` / `bufferDelayBetween` /
> `decodeElapsedBetween` spans **exactly one sampler interval**, except for the
> two documented early returns (unreadable report, missing counter).

Before: the pair spans one interval *plus the whole of any preceding run of
thrown ticks*, unbounded. After: it spans one interval, or the sampler produces
nothing.

**Nothing else is stateful.** `reportedMissingFieldsRef`,
`decodeSampleFailuresRef`, `lagSampleFailuresRef` and `reportedNoPlayoutRef` are
component-scope refs with their own documented scoping arguments (#2084's decade
curve, the flap reasoning). **None of them is reset by this change**, and that
is deliberate: a thrown tick is not a new fault, and resetting a decade counter
would reintroduce the firehose those refs exist to prevent.

### Closure capture

`previous` is declared with `let` inside the effect body and the `.catch` arrow
closes over the same binding the async IIFE writes. Assignment from the handler
is the same binding, not a copy. No `useRef` is needed or wanted — a ref would
outlive the effect and survive a reconnect, which is the opposite of what a
fresh window means.

---

## Boundary rules

| rule | status |
|---|---|
| No cross-context project references | **n/a** — no .NET project is touched; `BoundaryTests` unaffected |
| `apps/shared` must not import from `apps/kiosk-web` or `apps/management-web` | **held** — no import added; `useWallAlignment`/`CellPage` are read for evidence only |
| Composite stays layout-agnostic (spec 002 FR-016) | **held** — no new prop, no layout concept enters |
| §II value objects | **n/a** — TypeScript frontend; §II binds the .NET domain model |
| ADR-0105 `Ensure.That` guards | **n/a** — same reason |
| ADR-0141 `Option<T>` | **n/a** — same reason; `previous`'s `T \| null` is the file's existing idiom and is unchanged in type |

---

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change,
no RabbitMQ, no Wolverine. The only wire traffic on this path is the existing
`POST /stream-distribution/streams/kiosk-latency`, whose request body, headers,
scope and handler are untouched.

---

## Test design

### The fixture is the design

The suite's existing `advancingVideoStat()` advances by **constant** increments
per call (`+30` frames, `+30` emitted, `+0.05 s` buffer, `+0.02 s` processing),
so its per-frame buffer mean is `0.05 / 30 × 1000 = 1.667 ms` **at every window
width**. A widening test on that fixture is green before and after the fix and
proves nothing — the exact "an assertion that cannot fail" trap this repo keeps
finding.

So the new file supplies a **stepped** fixture: a constant rate before the
throwing tick, a different constant rate after it. Then the widened window and
the fresh window are arithmetically different numbers, and the test can name
both.

### The fixture must be a function of TIME, not of call count

**This is the part a straightforward reading gets wrong, and it would silently
corrupt every figure below.** `advancingVideoStat()` mutates counters **per
call**, and *both* samplers call the same `stats()` double. At 2 000 ms and
5 000 ms the two interleave — the decode sampler's read at t = 5 000 consumes an
advance that the lag sampler's own sequence then skips. With the existing
constant increments this is invisible; with a stepped rate it moves the step
into the wrong window and the expected numbers stop matching.

So the new fixture computes **absolute counter values from the elapsed fake
time**, which is what these counters physically are:

```ts
// rate: 30 frames per 2 000 ms = 0.015 frames/ms
framesDecoded(t)              = 200 + 0.015 * t
jitterBufferEmittedCount(t)   = 200 + 0.015 * t
totalProcessingDelay(t)       = 1.25 + 7.5e-6 * t          // a flat 0.5 ms/frame
jitterBufferDelay(t)          = 2.5 + 1.5e-5 * min(t, STEP_AT)
                                    + 6.0e-5 * max(0, t - STEP_AT)
                                //   1.0 ms/frame before, 4.0 ms/frame after
```

Any two reads at `t1 < t2` then yield deltas that depend only on the two times,
so **the decode sampler's interleaved reads cannot perturb the lag sampler's
arithmetic** — and the same fixture serves the US2 decode case unchanged.

**Use `Date.now()`, not `performance.now()`.** Vitest's default `toFake` set
includes `Date` and **excludes `performance`**; `CellPage.test.tsx:503-506`
already carries that finding in a comment and opts `performance` in explicitly
when it needs it. A fixture reading an unfaked `performance.now()` would see
real wall-clock time while the component's timers ran on fake time — every delta
≈ 0, every figure null, every assertion vacuous.

### The arithmetic, written out so the red is predictable

`STEP_AT = 4_000` — immediately after the lag tick that throws.
`onLagMeasured` throws on its **first** invocation only.

| lag tick | reads | window | today | after the fix |
|---|---|---|---|---|
| t = 2 000 | S(2000) | seed | `previous := S(2000)` | `previous := S(2000)` |
| t = 4 000 | S(4000) | 2 000→4 000, buffer **1.0** ms, lag 1.5 ms | callback throws; nothing reported; `previous` stays `S(2000)` | callback throws; nothing reported; **`previous := null`** |
| t = 6 000 | S(6000) | today 2 000→6 000 (**4 s**) | reports `presentation_buffer` **2.5**, `onLagMeasured` lag **3.0** | reports nothing; `previous := S(6000)` |
| t = 8 000 | S(8000) | 6 000→8 000 (2 s) | reports **4.0** / lag 4.5 | reports **4.0** / lag **4.5** |

Widened window at t = 6 000, today: buffer delta `1.5e-5 × 2000 + 6.0e-5 × 2000
= 0.030 + 0.120 = 0.150 s` over `0.015 × 4000 = 60` frames → `0.150 / 60 × 1000
= 2.5 ms`. Fresh window: `0.120 / 30 × 1000 = 4.0 ms`.

**The red assertion:** the first `presentation_buffer` figure reported after the
throw is `4.0`. Today it is `2.5` — the widened window flattening a 4× step-up
into a 60 %-of-true reading, which is precisely the excursion-flattening
`lagBetween`'s doc forbids. The same case asserts the first post-throw
`onLagMeasured` lag is `4.5`, not `3.0`, covering the controller consumer.

Both figures divide exactly in binary (`0.120/30×1000 = 4`, `0.030/30×1000 = 1`,
`0.150/60×1000 = 2.5`), so the assertions are exact equalities with no epsilon —
an epsilon wide enough to absorb float noise would be wide enough to absorb the
1.5 ms difference under test.

### How each figure is observed

- **`presentation_buffer` / `receive_to_decoded`**: `reportKioskLatency` writes
  `console.info('[latency]', { measurement, camera, elapsedMilliseconds })`
  (`kioskLatency.ts:87`). The new file installs an `infoSpy` and filters on
  `'[latency]'` + `measurement`, mirroring `CameraViewerAlignment.test.tsx`'s
  `resilienceLines` helper exactly. `kioskLatency.test.ts:97` already asserts
  against this line, so it is an established observation point, not one invented
  here.
- **The controller's value**: `onLagMeasured` is a `vi.fn()`; read
  `.mock.calls`.
- **`fetch`** is absent under jsdom; `send()`'s own `try/catch` swallows the
  resulting `ReferenceError` (`kioskLatency.ts:110-117`). No `fetch` stub is
  required and none should be added.

### Assert the subject ran, before asserting about it

Every case asserts the callback and the sampler were **actually reached**
before asserting what they produced — the convention
`CameraViewerAlignment.test.tsx` already states in prose ("Asserted BEFORE the
verdicts: a callback never reached would make … true of a component that
measured nothing"). Without it, a fixture that silently returns an unreadable
report makes every figure assertion vacuously absent.

### Phase-4a colour: **RED**

Behaviour-changing. What happens after a thrown tick is different: a figure that
would have been reported is not, and a later figure has a different value. Per
CLAUDE.md §Workflow and ADR-0144, ambiguity resolves to red, and there is no
ambiguity here. **A new-behaviour test arriving green is a phase-4 failure**, and
in this spec it has a specific known cause — writing the case on the constant
fixture. The red output is quoted verbatim in the PR.

---

## Risks

| risk | likelihood | mitigation |
|---|---|---|
| The new test is written on the constant fixture and passes green from birth | **high** — the fixture is right there and looks suitable | The spec, this plan and T003 all name it. The red must be observed and quoted (ADR-0139). |
| The stepped fixture steps on **call count**, and the decode sampler's interleaved reads move the step into the wrong window | **high** — it is how the existing fixture is written | §"function of TIME, not of call count" above. The expected figures are stated to the millisecond; a call-count fixture will not produce them, which is itself the signal. |
| The fixture reads `performance.now()`, which vitest does not fake by default | medium | `Date.now()`, with the `CellPage.test.tsx:503-506` citation. Symptom is every figure null and every assertion vacuous — caught by the "assert the subject ran first" rule. |
| The reset lands in a place that also swallows the throw, undoing #2189 | medium | `.catch` sibling statement, never a wrapper. `CameraViewerAlignment.test.tsx` runs **unmodified** as the guard; `git diff --stat` on that path must be empty. |
| A reviewer reads the change as "we now lose a sample" and reverts | medium | spec §"Decision record" argues it: the cost lands only on a single isolated throw, and under a repeating throw nothing was reported either way. |
| The decode half is dropped as "not in the issue" | medium | US2, with the `missingDecodeFieldIn` precedent quoted. |
| §IV's table is edited because the measurement got better | low | Spec and PR both state the table is unchanged and why. Nobody has read a figure off a running wall. |
| Fake-timer flake from the async IIFE | low | `await vi.advanceTimersByTimeAsync(...)` inside `act`, the pattern the existing suite already uses for exactly this effect. |

---

## Latency-budget impact (constitution §IV)

**Legs cited: presentation buffer (≤ 200 ms) and SFU → kiosk decode (≤ 120 ms) —
their measurement fidelity, not their duration.**

No code on the media path changes. `setPlayoutTarget` is not called differently,
the jitter buffer is not touched, no `getStats` call is added or removed, and no
render happens that did not happen before — the reset writes a closure variable,
not state. The budget is unchanged because its inputs are unchanged.

**§IV's table is not amended.** The presentation-buffer row stays *"recorded,
not yet observed"*; the decode row stays *"in part"*. Improving a recorded
figure's fidelity is not the same as someone reading it off a running wall, and
upgrading the column on this work would be the unearned discharge §IV warns
about in its own text.

---

## Constitution check

| section | verdict |
|---|---|
| §II DDD / value objects | n/a — frontend TypeScript |
| §III bounded-context isolation | n/a — no .NET project touched |
| §IV latency budget | **cited above**; no leg's duration changes, table unchanged |
| §VII observability | improved: a figure reported after a failure now describes the interval it claims to. No instrument added or removed |
| §VIII safe at trust boundaries | unchanged — no new input, no new endpoint, no new scope |
| §IX forward-compat interfaces | n/a — no interface added |
| §Testing | new behaviour → **red first**, observed and quoted |
| ADR-0036 Karpathy | smallest change that fixes the bug: two lines plus two comments. No refactor, no abstraction, no new knob |
| ADR-0144 | no ADR written, no gate weakened, no test deleted or edited to pass |
