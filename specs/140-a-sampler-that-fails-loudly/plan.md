# Plan 140 — A sampler that fails loudly

**Spec:** [`spec.md`](spec.md) · **Issue:** #2189 · **Branch:**
`fix/2189-a-sampler-that-fails-loudly` · **Engineer:** `frontend-engineer`

---

## Bounded context and layers

**None of the nine.** This is entirely inside `apps/shared`, the React library both
front-ends consume (ADR-0074), and it touches one composite plus that composite's test.
No `src/` project is opened, no `Shared.Contracts` message changes, no NetArchTest rule
is in play, and no context boundary is crossed — which is itself worth stating, because
the *tempting* version of this fix (item 3) would cross one, into StreamDistribution.

Layer within `apps/shared`:

```
apps/shared/src/
  observability/
    resilienceLog.ts        the channel — READ ONLY, unchanged
    kioskLatency.ts         the decode instrument — READ ONLY, unchanged (FR-010)
    wallAlignment.ts        the lag instrument   — READ ONLY, unchanged
  streaming/
    WhepClient.ts           stats() — READ ONLY, unchanged
  ui/composites/
    CameraViewer.tsx        THE ONLY PRODUCTION FILE CHANGED
    CameraViewerAlignment.test.tsx   the red tests
    CameraViewer.test.tsx            byte-identical, green (NFR-001)
    CameraViewerMedia.test.tsx       untouched
```

## Entities, value objects, invariants

No domain model, no value objects — this is a browser component, and §II's primitive ban
scopes to domain models. The three invariants the change must hold are stated here in
their place:

1. **A tile shows video, whatever the instruments do.** The strongest rule in this
   component (ADR-0128 FR-013, spec 040 FR-011). An observer that can break what it
   observes is worse than no observer.
2. **A counter is per sampler and per mounted tile.** Not per camera, not per effect
   instance, not shared between the two samplers. (Spec §*Decision* 2; FR-005, FR-006.)
3. **Silence means healthy.** A sampler that does not throw emits nothing (FR-007). The
   spec-095 review found the mirror-image hole on the actuator half — every double threw
   or refused, so the `!applied` guard could have been deleted with the suite green — and
   this plan carries a guard test for exactly that reason.

## Messaging — domain event → integration event

**None.** Nothing is published, nothing is consumed, no RabbitMQ, no Wolverine. The
signal's whole journey is `console.info` inside one browser tab. That is the constraint
that makes item 3 out of scope rather than a smaller version of the same change.

---

## The change, concretely

### `apps/shared/src/ui/composites/CameraViewer.tsx` — four edits

**Edit 1 — two module-private helpers**, placed at the bottom of the file beside
`labelFor` (module scope, not component scope: they close over nothing).

```ts
/**
 * Counts a sampler failure and answers the running count when this one is worth a
 * line, or null.
 *
 * The decade cadence #2084 landed for the fab-less frame (`CellPage.tsx`
 * `countReportableSkew`), minus that case's fab predicate. A wall runs for weeks: a
 * line per tick evicts the first — the diagnostically valuable — occurrence from any
 * console buffer, and a line per session is emitted before anyone is looking.
 *
 * Duplicated rather than shared with CellPage deliberately; see spec 140
 * §"The cadence is #2084's decade curve". Extract it at the third site.
 */
function countReportableFailure(counter: { current: number }): number | null {
  counter.current += 1;
  const count = counter.current;
  let decade = 1;
  while (decade < count) decade *= 10;
  return decade === count ? count : null;
}

/** The message of a thrown value, or its string form when it is not an Error. */
function reasonFrom(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
```

**Edit 2 — the counters and the reporter**, at component scope immediately after
`reportMissingStatsField` (`:145`), carrying a comment that points at the neighbouring
block rather than restating its 27 lines:

```ts
// Issue #2189: a sampler that throws says so, instead of discarding the throw and
// every one after it.
//
// TWO COUNTERS, NOT ONE — the opposite of `reportedMissingFieldsRef` above, and for
// the reason #2084 gives for its own two: "one counter would hide it". A missing
// stats field is one fact about the browser engine that both samplers read, so it is
// shared and keyed by field. A throw is not. The lag sampler runs the wall's own
// `onLagMeasured` callback, which the decode sampler never touches, and the two
// report different §IV legs — so a lag failure must not consume the decode sampler's
// first line about an unrelated fault of its own.
//
// Same scope and same ref-not-state reasoning as the block above: both effects are
// keyed on `status`, so a counter inside either one resets on every reconnect, and a
// tile flapping through the night would report the same permanent fault hundreds of
// times.
const decodeSampleFailuresRef = useRef(0);
const lagSampleFailuresRef = useRef(0);
const reportSamplerFailure = useCallback(
  (counter: { current: number }, transition: 'decode-sampler-failed' | 'lag-sampler-failed', error: unknown) => {
    const count = countReportableFailure(counter);
    if (count === null) return;
    logResilienceEvent('stream', transition, { cameraIdentifier, count, reason: reasonFrom(error) });
  },
  [cameraIdentifier],
);
```

**Edit 3 — the decode catch at `:185`**:

```ts
      })().catch((error: unknown) => reportSamplerFailure(decodeSampleFailuresRef, 'decode-sampler-failed', error));
```

and `reportSamplerFailure` joins that effect's dependency array at `:189`.

**Edit 4 — the lag catch at `:258`**, identically with `lagSampleFailuresRef` and
`'lag-sampler-failed'`, plus the dependency at `:262`.

**Why the `.catch` survives at all** — the reasoning belongs in the spec, not as a
fourth comment block in the file; the code now reports, which is its own explanation.

### What is explicitly NOT edited

| File / line | Why |
|---|---|
| `apps/shared/src/observability/kioskLatency.ts:110` | The documented POST swallow. Correct, and FR-010 forbids touching it. |
| `CameraViewer.tsx:286–291` | The `setPlayoutTarget` catch. Already reports through `!applied` at `:302`. Not silent, not this defect. |
| `CameraViewer.tsx:109–145` | `reportMissingStatsField` and its ref. The new mechanism sits beside it, not inside it. |
| `apps/shared/src/observability/resilienceLog.ts` | `transition` is already an open string. Nothing to widen. |
| `apps/kiosk-web/src/features/cell/CellPage.tsx` | The extraction that was considered and rejected. |
| `src/StreamDistribution/**` | Item 3. Deferred to its own issue. |

---

## How the red is constructed — the seam, verified, not reasoned about

The seam is `CameraViewerAlignment.test.tsx:58–77`: the whole `WhepClient` module is
mocked, and the double's `stats = () => statsBehaviour()` dispatches to a module-level
`let` that each case sets. `beforeEach` (`:135–140`) restores `statsThrows`, which
**throws synchronously**:

```ts
const statsThrows = vi.fn(() => { throw new Error('getStats exploded'); });
```

A synchronous throw from `stats()` inside `await stats()` rejects the async IIFE — which
is exactly the production path, and exactly what the two catches swallow today. So
**every red case below is one `statsBehaviour` assignment plus assertions**; no new
double, no new harness, no browser.

The file already supplies the three things the assertions need:

- `resilienceLines(transition)` (`:110–116`) — the `[resilience]` calls carrying one
  transition, filtered out of `useWhepSession`'s own status lines.
- `flapThroughReconnect()` (`:123–133`) — `connected → disconnected → +10 s`, through the
  real state machine, which tears the client down and builds another.
- The double's own comment at `:60–67` warning that **the double must report
  `connected` or the whole suite is vacuous** — every effect in `CameraViewer` guards on
  `status !== 'live'`. That is why each case below asserts the double **was called**
  before asserting anything about lines.

### The tick arithmetic the assertions depend on

Advancing 20 000 ms of fake time:

| Sampler | Interval | Ticks in 20 s | Decade boundaries hit | Lines expected |
|---|---|---|---|---|
| decode (`:158`) | 5 000 ms | 4 | 1 | **1** |
| lag (`:208`) | 2 000 ms | 10 | 1, 10 | **2** |

Advancing 200 000 ms: lag ticks 100 times → boundaries 1, 10, 100 → **3** lines, which is
what makes the cadence assertable rather than merely asserted.

### The one case that needs a new fixture

*"A failing sampler does not silence the other"* needs `stats()` to **succeed** while
`onLagMeasured` throws — and for `onLagMeasured` to actually be reached, `lagBetween` and
`bufferDelayBetween` must both return non-null. Both return null unless the counters
**advance** between samples (`wallAlignment.ts:169–179`, `:201–209`):
`jitterBufferEmittedCount` and `framesDecoded` must increase, and `jitterBufferDelay` /
`totalProcessingDelay` must not go backwards. The file's existing `videoStatWithout()`
returns **constant** values, so it cannot serve. The test needs a small advancing
factory — increment `framesDecoded` and `jitterBufferEmittedCount` by 30 and the two
delay totals by a small positive amount on each call — and must assert `onLagMeasured`
was actually called, or the case is vacuous in the way this suite has already been once.

### Two hazards, each a flake or a false green if missed

1. **`afterEach` does not reset the counters; `cleanup()` does.** The refs are component
   scope, so unmounting is what resets them — which the file's `afterEach` already does.
   A case that renders twice in one test is measuring one continuous counter.
2. **`statsThrows` is a shared `vi.fn` cleared in `beforeEach`.** A case that swaps
   `statsBehaviour` must not then assert on `statsThrows`; assert on its own double.

---

## Phase 4a — colour, and which tests are which

**Behaviour-changing → red** (ADR-0144; the issue says so and the spec confirms it). A
test that arrives green here is a phase-4 failure.

**R = must be observed failing. G = must be green before and after.**

| | Name | Why it is that colour |
|---|---|---|
| **R1** | Says so when reading the tile lag throws | Zero `[resilience]` lines today. |
| **R2** | Says so when the decode sampler throws on a page with no wall | Zero today; also proves the decode half is independent of `onLagMeasured`. |
| **R3** | A sampler that fails does not silence the other | Zero today; the case that would fail a single shared counter. |
| **R4** | Bounds a permanently broken sampler to a decade cadence | Zero today; would also fail a naive log-every-tick fix. |
| **R5** | Counts across a flap rather than starting again | Zero today; would fail an effect-scoped counter. |
| **G1** | Says nothing about a sampler that does not throw | Green today (nothing logs). Pins FR-007 — without it the whole guard could be `logResilienceEvent(...)` unconditionally and every kiosk would claim a broken instrument forever, suite green. This is the spec-095 review finding, re-applied. |
| **G2** | *Keeps showing video when reading the tile lag throws* (existing, `:154`) | Must pass **unmodified**. It is the FR-013 invariant. |
| **G3** | `CameraViewer.test.tsx` whole file | Byte-identical, green (NFR-001). Its `toEqual` over the complete `[resilience]` array is the tripwire for a leaked line. |

G1 and G2 are **not** counted as reds, per ADR-0139: "counting them as red would be the
shortcut ADR-0139 exists to prevent."

---

## Alignment check

| Rule | Status |
|---|---|
| Constitution §II (primitives on domain models) | N/A — no domain model. |
| Constitution §IV (latency budget) | Cited; no leg moves. Spec §*Latency budget impact*. |
| Constitution §VII (dashboard per implemented leg) | Unchanged obligation; item 3 is the *dashboard* half and is deferred with a named follow-up, not dropped. |
| ADR-0036 (smallest change, no drive-by error handling) | Two catches, two refs, one callback, two helpers. No unrelated file opened. The one deliberate duplication is recorded. |
| ADR-0122 (browser measurement enters through a service) | Respected by **not** inventing a new browser→service signal. |
| ADR-0139 / §Testing | Behaviour-changing → red, five reds, three guards. |
| ADR-0141 (`Option<T>`) | N/A — TypeScript, and `null` is this file's established absence vocabulary. |
| ADR-0105 (`Ensure.That`) | N/A — .NET guard rule. |
| ADR-0109 (`[P]`) | Two files, one the test for the other. Parallelism is nil; see tasks.md. |
| ADR-0084 (300 LOC) | Backend analyzer rule; no ESLint equivalent configured. `CameraViewer.tsx` already exceeds it and this adds ~35 lines, most of them comment. Recorded, not hidden. |

## Files to change

1. `apps/shared/src/ui/composites/CameraViewer.tsx` — the four edits above.
2. `apps/shared/src/ui/composites/CameraViewerAlignment.test.tsx` — R1–R5 and G1.

Nothing else. Two files, both in `apps/shared`.

## Risks

- **R-A — the fix fires on the healthy path.** The most damaging failure mode, and
  invisible in a suite where every double throws. G1 is the counter-measure and must be
  written before the implementation, not after.
- **R-B — `reason` becomes noisy.** A non-`Error` throw stringifies to `[object Object]`.
  Accepted: the count and the transition still carry the information that matters, and
  `String(error)` is what the rest of the app does with unknown throws.
- **R-C — the extraction argument is reopened at review.** Recorded in the spec with the
  reason and the trigger (a third site), so review can overrule with evidence rather than
  re-deriving it.
