# Spec 197 — The window a throw leaves open

**Issue:** [#2314](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2314)
(`bug`, `agent:ready`; Project #13, status Todo — verified with `--limit 2000`,
matched on `content.url`)
**Branch:** `2314-reset-previous-on-callback-throw`
**Phase:** 1 (Specify) — ADR-0037
**ADRs:** ADR-0128 (primary), ADR-0129, ADR-0122, ADR-0117, ADR-0144, ADR-0139, ADR-0109, ADR-0036
**Constitution:** §IV (the latency budget), §VII (observability), §Testing ("New behaviour")

---

## Summary

`apps/shared/src/ui/composites/CameraViewer.tsx` runs two latency samplers on an
interval. Each holds a `previous` sample in the effect's closure and advances it
**only at the very end of a fully successful tick**:

```ts
if (previous !== null) {
  …
  onLagMeasuredRef.current?.(cameraIdentifier, lag, buffered);   // caller code
  …
  reportKioskLatency('presentation_buffer', cameraIdentifier, buffered, getToken);
}
previous = current;                                              // never reached on a throw
```

Anything that throws before that last line — the wall's own `onLagMeasured`, or
`stats()` itself — leaves `previous` pinned to the sample it already held. **The
sample just read is discarded, and the window stays open.**

The decision #2314 asks for is whether to reset `previous` to `null` on a
callback throw. **The answer is yes**, and the reason is not the one-tick
widening the issue describes — that part is genuinely noise. It is that
`previous` is pinned for as long as the throwing continues, **without bound**,
and the first tick that succeeds afterwards then reports a per-frame mean over
the whole throwing period. That figure is precisely the cumulative session
average `lagBetween`'s own documentation forbids:

> **A delta between two samples, never a cumulative ratio.** The counters run for
> the life of the session, so a ratio of the totals reports the session average
> and flattens exactly the excursion a budget is about — a wall that fell out of
> alignment ten seconds ago would still read as aligned.
> — `apps/shared/src/observability/wallAlignment.ts:152-158`

The fix makes the module keep its own contract. It is one line per sampler,
inside the `.catch` that issue #2189 already put there.

---

## What was verified before this spec was written

Every load-bearing claim in #2314 was checked against the tree at `76248bc1`.
Three hold, **one is understated**, and **one is wrong in a way that changes the
decision**.

### 1. Confirmed — the structure is as described

`CameraViewer.tsx:255-300`. `previous = current` is at `:299`, outside and after
the `if (previous !== null)` block that contains both the callback call (`:284`)
and the `presentation_buffer` report (`:296`). A throw from either skips both the
report and the advance.

### 2. Confirmed — the cadence is exactly 2 000 ms, not "~2 s"

`LAG_SAMPLE_INTERVAL_MS = 2_000` (`CameraViewer.tsx:49`). The decode sampler runs
at `DECODE_SAMPLE_INTERVAL_MS = 5_000` (`:29`).

### 3. Wrong, and this is the part that nearly closed the issue as "won't fix"

**A single widened window is not a corrupted figure.** `lagBetween` and
`bufferDelayBetween` divide by the *frame-count delta*, not by elapsed time:

```ts
return (bufferSeconds / emitted) * 1000;                       // bufferDelayBetween
return (bufferSeconds / emitted) * 1000 + (processingSeconds / decoded) * 1000;  // lagBetween
```

A per-frame mean is arithmetically valid over **any** window. Widening it from
2 s to 4 s does not make the number wrong; it halves the temporal resolution for
exactly one sample. Against an OpenTelemetry `Histogram<double>`
(`src/ServiceDefaults/LatencyBudget.cs:53,121`) read as a distribution over a
dashboard window, one smoothed sample out of ~30 per tile per minute is below
the noise floor. **On the issue's own framing, the correct answer is "accept
it".**

### 4. Understated — `previous` is pinned indefinitely, not for one tick

The issue says "the next successful tick after a callback throw averages over a
widened window". It does more than that. Because the advance is *after* the
callback, a callback that keeps throwing never advances `previous` at all:

| tick | `previous` | window | callback | reported |
|---|---|---|---|---|
| N | `S(N-1)` | 2 s | **throws** | nothing |
| N+1 | `S(N-1)` — unchanged | 4 s | **throws** | nothing |
| N+2 | `S(N-1)` — unchanged | 6 s | **throws** | nothing |
| … | `S(N-1)` | … | … | nothing |
| N+k | `S(N-1)` | **2(k+1) s** | succeeds | **one near-session average** |

Nothing downstream can detect it. `ABSURDLY_LONG_MS = 60_000`
(`kioskLatency.ts:52`) bounds the *value*, and a per-frame mean stays in the
tens of milliseconds however wide the window. `bufferSeconds < 0` and
`emitted <= 0` catch counter resets, not width.

### 5. Confirmed — and there are three consumers of that figure, not one

The issue names the histogram. Two more read the same number:

| consumer | path | what a stale-window figure does |
|---|---|---|
| **§IV whole-leg histogram** | `reportKioskLatency('presentation_buffer', …)` → `POST kiosk-latency` → `LatencyBudget.PresentationBuffer` (`isWholeLeg: true`, budget 200 ms) | one sample that reads in-budget because it was flattened, on the one leg §IV marks *whole* |
| **the alignment controller** | `onLagMeasured` → `CellPage.tsx:363` → `useWallAlignment.reportLag` → `settleAlignment` → `playoutTargetMilliseconds` | a real jitter-buffer target, written to real receivers, computed from an hours-old mean |
| **overlay label ageing** | `useWallAlignment.ts:243` — `frameAgeFor` returns exactly `lagMilliseconds` → `Tile`'s label delay | §IV: "a label is **aged** to match its picture". A wrong age mis-pairs label and frame — the pairing ADR-0129 replaced frame-sync with |

`WALL_SKEW_BOUND_MS` is 33 ms (`wallAlignment.ts:46`), so the controller's whole
tolerance is a third of a frame-time; a flattened mean is well inside the range
that moves its decision.

---

## The callers, and how likely a throw actually is

Swept `apps/` for `onLagMeasured`. **Exactly one production call site exists:**

- `apps/kiosk-web/src/features/cell/CellPage.tsx:363` —
  `onLagMeasured={(camera, lag, buffer) => alignment.reportLag(cell.key, camera, lag, buffer)}`
- `reportLag` (`useWallAlignment.ts:105-112`) is a `useCallback` whose entire body
  is `lagsRef.current.set(tileKey, { camera, lagMilliseconds, bufferMilliseconds, at: performance.now() })`.

A `Map.set` on a ref plus `performance.now()`. **Neither can throw short of
memory exhaustion.** `management-web` mounts `CameraViewer` and passes no
callback at all, so its lag interval never starts (FR-004). Every other
occurrence is a test double.

**So today this is defensive, not live** — and that is recorded here rather than
used as a reason to close, for three reasons:

1. `onLagMeasured` is a **public prop of a shared composite**, documented in
   `apps/shared` for callers that do not exist yet. The next caller may dispatch
   to a store, post, or serialise.
2. `stats()` reaches the same `.catch` and is **not** ours. It is the browser's
   `RTCPeerConnection.getStats()` through `WhepClient`, and #2189 exists
   precisely because it was observed to throw. **The defect is not confined to
   the callback the issue names** — see the scope note below.
3. The consequence is silent and unbounded, on a §IV path. The repo's own
   standing rule is that a figure nobody can check is worse than no figure.

---

## Scope: both throw paths, both samplers

#2314 names the callback. The identical defect sits one branch over
(`stats()` throwing) and one effect up (the decode sampler, `CameraViewer.tsx:196-227`,
whose `previous = current` is likewise after `reportKioskLatency('receive_to_decoded', …)`).

Fixing only the half that was filed would leave the same silence one effect
away — which is the mistake this codebase already wrote down for itself:

> Issue #2109 lists only the alignment half; fixing one and not the other would
> leave the identical silence one file over.
> — `apps/shared/src/observability/kioskLatency.ts`, `missingDecodeFieldIn`

The reset therefore goes in each sampler's **existing outer `.catch`**, which
covers both throw paths at once and is a strictly smaller diff than a dedicated
try/catch around the callback.

---

## User stories

### US1 — A failed lag tick starts the next window fresh (P1)

**As** the presentation-buffer instrument and the wall's alignment controller,
**I want** a sampler tick that throws to abandon its open window,
**so that** the first figure produced after the failure describes the interval it
claims to describe, and not the whole outage.

**Independently shippable.** One line in `CameraViewer.tsx:300`. US2 can follow
in a separate PR without changing anything here.

### US2 — The decode sampler does the same (P2)

**As** the `receive_to_decoded` instrument,
**I want** the identical treatment on the identical structure,
**so that** the fix does not leave its own twin behind.

---

## Acceptance scenarios (Gherkin)

Cadence throughout: lag sampler 2 000 ms, decode sampler 5 000 ms. "Rate"
means buffer-seconds accrued per emitted frame, which is what these functions
return.

### US1

```gherkin
Scenario: Happy path — a fresh window after a one-off callback throw
  Given a live tile whose receiver statistics advance on every read
    And the tile's buffer accrues 1.0 ms per frame up to the throw
    And 4.0 ms per frame from the throw onward
    And onLagMeasured throws on its first invocation only
  When the lag sampler ticks past the throw and twice more
  Then the first presentation_buffer figure reported after the throw is 4.0 ms
   And it is NOT 2.5 ms — the mean of the two regimes over the widened window
   And the first onLagMeasured call after the throw carries the same fresh-window lag
```

```gherkin
Scenario: Conflict — a persistently throwing callback does not accumulate a window
  Given a live tile whose receiver statistics advance on every read
    And onLagMeasured throws on every invocation for ten ticks
    And the buffer rate steps up once during those ten ticks
  When the callback stops throwing
  Then the first presentation_buffer figure describes one 2 000 ms window
   And no figure anywhere in the run is a mean over the ten-tick outage
```

```gherkin
Scenario: The same for a throwing getStats, which is not our code
  Given a live tile whose stats() throws for several ticks and then succeeds
  When it succeeds
  Then the first figure after recovery describes one 2 000 ms window
```

```gherkin
Scenario: Bad request — the failure is still loud, and still attributed
  Given a live tile whose onLagMeasured throws
  When the lag sampler ticks
  Then a '[resilience]' line with transition 'lag-sampler-failed' is still written
   And it still carries cameraIdentifier, the decade count, and the thrown reason
   And NO 'decode-sampler-failed' line is written
```

The last scenario is the regression guard for #2189 and **must be green before
and after**. #2189 made a callback throw and a `getStats` throw distinguishable
and loud; a reset that swallowed the throw would silently undo it.

```gherkin
Scenario: Auth — unchanged, and deliberately so
  Given a kiosk whose getToken resolves null
  When a figure is reported
  Then reportKioskLatency still abandons the POST inside send()
   And no behaviour on this path is added, removed or re-authorised by this change
```

There is no new endpoint, no new scope, no new trust boundary. `POST
stream-distribution/streams/kiosk-latency` and its `RequireScope` are untouched.

```gherkin
Scenario: Nothing changes when nothing throws
  Given a live tile whose stats() and onLagMeasured both succeed on every tick
  When the sampler runs for ten ticks
  Then every window is 2 000 ms wide, exactly as today
   And no resilience line is written
```

### US2

```gherkin
Scenario: The decode sampler starts fresh after a throwing tick
  Given a live tile whose stats() throws on one decode tick and then succeeds
    And whose decode rate steps up across that throw
  When the decode sampler ticks twice more
  Then the first receive_to_decoded figure after the throw describes one 5 000 ms window
   And 'decode-sampler-failed' is still reported on its decade cadence
```

---

## Independent end-to-end test procedure

CI cannot produce video, so the automated discharge is the vitest suite in
`apps/shared/src/ui/composites/`. The manual procedure below is what a person
does to see it on a real wall; it is **not** required to close this issue, and
it does not discharge §IV's *observed* column (see "Latency budget" below).

1. Boot the stack (`dotnet run --project src/AppHost`), open `kiosk-web` on a
   published layout with **two or more** tiles so the controller runs.
2. Open devtools on the kiosk. Note `[latency] {measurement: 'presentation_buffer'}`
   lines arriving about every 2 s per tile.
3. In the console, monkey-patch the wall callback to throw for 30 s. (The
   supported way is a temporary `throw` inside `reportLag`; there is no runtime
   switch, and adding one would be speculative generality.)
4. Observe `[resilience] {transition: 'lag-sampler-failed'}` at counts 1 and 10.
5. Remove the throw. **Before this change**, the very next
   `presentation_buffer` line is a mean over the whole ~30 s outage and reads
   noticeably flatter than its neighbours. **After**, the first line after
   recovery is one tick late and reads in line with the lines that follow it.

---

## Locked tech choices used

Nothing new is introduced. Everything below already exists in the file.

| Concern | Choice | Source |
|---|---|---|
| Frontend | React + TypeScript + Vite, `apps/shared` composite | ADR-0074 |
| Test framework | **vitest + @testing-library/react + jsdom**, fake timers | the file's existing suite |
| Kiosk latency reporting | `reportKioskLatency` → gateway POST → `LatencyBudget.Record` | ADR-0122 |
| Alignment arithmetic | `lagBetween` / `bufferDelayBetween`, pure, per-frame deltas | ADR-0128 |
| Failure surfacing | `logResilienceEvent` on a decade cadence | #2189 / #2084 |

**No ADR is needed.** This is an implementation defect in code ADR-0128 already
governs, and the fix restores the behaviour ADR-0128's own module documents. No
architectural decision is made or changed. (Per ADR-0144, the autonomous lane
could not write one in any case.)

---

## Latency-budget impact (constitution §IV)

**Legs affected: presentation buffer (≤ 200 ms) and SFU → kiosk decode
(≤ 120 ms) — their *measurement*, not their duration.**

- **No latency is added or removed on any leg.** The change is one assignment in
  a `.catch` that only executes on a path where nothing is reported anyway. It
  does not touch `playoutTargetMilliseconds`, `setPlayoutTarget`, the jitter
  buffer, the render path, or the number of `getStats` calls.
- **The budget still holds** because the budget's inputs are unchanged. What
  changes is the fidelity of the figure reported after a failed tick.
- **§IV's table is not amended by this spec.** The presentation-buffer row stays
  **"recorded, not yet observed"** and the decode row stays **"in part"**. This
  work makes a recorded figure trustworthy after a failure; it does not put
  anyone in front of a running wall, which is the only thing that moves
  *observed*. A spec that improved a measurement and then upgraded the column
  would be exactly the discharge-nobody-earned §IV warns about.
- **#1714 is unaffected** and stays open.

---

## Decision record — why fix rather than accept

#2314 explicitly asks for a decision. Recorded here in full, because the
"accept" case is strong on the issue's own framing and a future reader deserves
to see it argued rather than skipped.

### The case for accepting (rejected)

1. A widened window yields an **arithmetically valid** per-frame mean, not a
   corrupt one.
2. The consumer is a **histogram**, read as a distribution. One smoothed sample
   per outage is noise.
3. The only production callback is a `Map.set` that **cannot throw**.
4. The reset **costs a sample**: after a one-off throw, today's behaviour
   reports a widened figure at tick N+1, whereas the reset reports nothing at
   N+1 and resumes at N+2. Trading a slightly-smoothed sample for no sample is
   not self-evidently an improvement.
5. `reportKioskLatency`'s own doc says a lost measurement is fine: *"a lost
   measurement is a missing sample from a distribution over many frames"*.

### Why it is fixed anyway

1. **The pin is unbounded, not one tick** (finding 4 above). The failure mode is
   not "one 4 s window"; it is "one mean over however long the fault lasted",
   delivered the moment things look healthy again. Argument (1) — that the mean
   is valid over any window — is what makes this *worse*, not better: the number
   looks entirely reasonable and nothing downstream can tell.
2. **It breaks the module's own written contract.** `lagBetween` and
   `decodeElapsedBetween` both say, in prose, that a cumulative ratio is
   forbidden because it flattens the excursion a budget is about. The current
   structure produces exactly that ratio on recovery. A rule the code states and
   then violates under a failure path is the shape of defect this repo has had
   to correct repeatedly.
3. **The histogram is not the only consumer.** The same figure sets a real
   jitter-buffer target and ages overlay labels (finding 5). Argument (2) covers
   the histogram and nothing else.
4. **Argument (4) has a hole.** The one-lost-sample cost applies only to a
   *single* throw. Under a repeating throw — which is what actually happens when
   something is broken — nothing is reported either way, so the reset costs
   nothing and prevents the bad recovery figure. The fix is cheap exactly where
   it matters and costs one sample where it does not.
5. **Argument (3) is about today.** `stats()` is in the same `.catch`, is not
   our code, and #2189 exists because it throws.

### What was considered and deliberately NOT done

**Moving `reportKioskLatency('presentation_buffer', …)` *before* the callback**,
so a caller's throw cannot cost us our own measurement. Tempting, and it would
have removed the one-lost-sample cost entirely. **Rejected**: under a
persistently throwing callback it would emit clean, in-budget
`presentation_buffer` samples every 2 s from a wall whose alignment controller
is receiving nothing — a healthy-looking figure for a leg §IV marks
`isWholeLeg: true` against 200 ms. That is the overclaim §IV and ADR-0122 forbid
everywhere else. Keeping the report coupled to the callback succeeding means a
broken wall goes quiet rather than reporting a perfect score.

**Resetting `previous` on the two non-throwing early returns** (`report === null`
and `current === null`). Those widen the window too, by the same mechanism.
**Left alone, and recorded as a residual**: they are the documented normal state
of a session that has not started producing video (`missingLagFieldIn`'s own
doc), they already have their own instrument (`stats-field-missing`), and
resetting there would drop a sample on every mount. Different case, different
frequency, not this issue's decision. Worth revisiting only if a receiver is
seen intermittently dropping counters mid-session.

**An ADR.** None is needed; see "Locked tech choices" above.

---

## Out of scope

- Inter-display sync / PTP (ADR-0128 — unbuilt, out of scope, not a §IV row).
- The overlapping-tick hazard: `window.setInterval` with an async body can
  re-enter if `stats()` outlives the interval. Pre-existing, orthogonal, not
  filed. Noted so the next reader of this effect does not think it was missed.
- Any change to `wallAlignment.ts` or `kioskLatency.ts`. Both are **read** by
  this spec and **not edited**; the arithmetic is right, the caller was wrong.
- Upgrading any cell of §IV's table.

---

## Success criterion (stated before code, per ADR-0036)

A vitest case in `apps/shared/src/ui/composites/` that, on a fixture whose buffer
rate **steps up across a throwing tick**, asserts the first
`presentation_buffer` figure reported after the throw equals the **fresh-window**
value and not the widened blend — observed **red** against today's
`CameraViewer.tsx`, quoted verbatim in the PR, and green after one line changes,
with the whole existing `CameraViewerAlignment.test.tsx` suite — #2189's four
cases included — still green and unedited.

**The fixture is the load-bearing part.** The suite's existing
`advancingVideoStat()` advances by *constant* increments (+30 frames, +0.05 s
buffer per call), so its per-frame mean is 1.667 ms **at every window width**. A
widening test written on that fixture passes identically before and after the
fix and proves nothing. A new stepped fixture is required, and the red must be
observed to prove it is not another assertion that cannot fail.

---

## Definition of done

- [ ] A stepped-rate fixture exists and the widening is **observed red** with the
      figure quoted.
- [ ] `previous` is reset in the lag sampler's outer `.catch` (US1).
- [ ] The same in the decode sampler's outer `.catch` (US2).
- [ ] `CameraViewerAlignment.test.tsx`'s #2189 cases pass **unmodified**.
- [ ] `pnpm lint`, `pnpm typecheck`, `pnpm test` clean.
- [ ] PR cites both §IV legs, states no duration changes, and states that §IV's
      table is unchanged.
