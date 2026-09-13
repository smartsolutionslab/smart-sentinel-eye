# Spec 140 — A sampler that fails loudly

**Issue:** #2189 — *Both kiosk latency samplers end in `.catch(() => undefined)`, so a
tile can stop measuring two §IV legs and drop out of wall alignment in silence*
**Branch:** `fix/2189-a-sampler-that-fails-loudly`
**Status:** Phase 3 complete — awaiting gate
**Lane:** autonomous (ADR-0144)
**Engineer:** `frontend-engineer`
**ADRs:** 0037 (phases and gates), 0144 (the lane; phase 4a colour **red**), 0036
(smallest change; no drive-by error handling; surface assumptions), 0122 (browser
measurements enter observability through a service — the reason item 3 is deferred),
0117 (dashboards bind implemented legs), 0118 (one sink per environment — and it is not
reachable from a browser), 0128 (playout alignment, the controller this feeds), 0129
(the label is aged, not frame-synced), 0074/0075 (the two React apps), 0109 (`[P]`
disjoint-file parallelism), 0028 (GitFlow), 0086 (no `Co-Authored-By`), constitution §IV
(the budget) and §VII (observability).

**Spec number 140.** Derived from the highest number on **any** ref, not the working
tree. `specs/` on this branch tops out at `137-one-copy-of-the-admin-helpers`;
`138-the-aggregate-enforces-its-own-grid` lives on `origin/fix/2187-…` and
`139-loud-when-broken-quiet-when-absent` on `origin/fix/2188-…`. Both were found by
walking every ref (`git for-each-ref refs/heads refs/remotes` → `git ls-tree … specs/`),
not by reading `origin/develop`. 139 + 1 = **140**. `create-new-feature.ps1` was **not**
run: the branch already exists and is checked out, and the script cuts a branch.

---

## Phase 1 — every claim in the issue and in the brief, checked against the tree

### The defect is present. The line numbers in the issue are stale; the brief's are right

The issue cites `:135` and `:204`. Both have moved — spec 095 landed
`reportMissingStatsField` between them. Verified on this branch, in
`apps/shared/src/ui/composites/CameraViewer.tsx` (382 lines):

```
185:      })().catch(() => undefined);
258:      })().catch(() => undefined);
288:    } catch {
```

`:185` closes the **decode** sampler's async IIFE (`DECODE_SAMPLE_INTERVAL_MS`, 5 s,
effect at `:158–189`). `:258` closes the **lag** sampler's (`LAG_SAMPLE_INTERVAL_MS`,
2 s, effect at `:208–262`). `:288` is the third catch and is **not** this defect — see
below. The brief's `:185`/`:258` are exact; the issue's `:135`/`:204` are not.

### What each swallowed region actually contains — enumerated, not summarised

**Decode sampler (`:165–185`)**, the `SFU → kiosk decode` fragment:

| Call | Can it throw? |
|---|---|
| `await stats()` | **Yes.** `useWhepSession.stats` (`:323`) forwards to `WhepClient.stats()`, which returns `null` for an engine with no `getStats` (`WhepClient.ts:114–124`) — but a `getStats()` that *rejects* propagates. |
| `decodeSampleFrom(report as unknown as Map<…>)` | **Yes.** The cast is unchecked; `report.values()` is a `TypeError` on anything that is not iterable. |
| `missingDecodeFieldIn(…)` | **Yes**, same cast, same reason. |
| `decodeElapsedBetween(previous, current)` | No — arithmetic over two `DecodeSample`s. |
| `reportKioskLatency('receive_to_decoded', …)` | No. Documented "Never throws and never rejects" (`kioskLatency.ts:59–62`), and the body bears it out: three numeric guards, one `console.info`, and `void send(…)` whose entire body is inside the `try` at `kioskLatency.ts:98–115`. |

**Lag sampler (`:216–258`)**, the `presentation buffer` leg **and** wall alignment: the
same five, with `lagSampleFrom` / `missingLagFieldIn` / `lagBetween` /
`bufferDelayBetween` in place of the decode pair — **plus one the decode sampler does
not have**:

```ts
onLagMeasuredRef.current?.(cameraIdentifier, lag, buffered);   // :242
```

That is a **consumer callback supplied by the caller** — `useWallAlignment`'s recorder on
the kiosk. It is arbitrary caller code and it is the one call in either sampler that has
no claim to being non-throwing. Today a throw from it discards the rest of that lag tick,
every tick, forever, and says nothing.

### The third catch carries a justification, and it is genuinely a different thing

`:286–291`, around `setPlayoutTarget`, is the catch the issue calls "the neighbouring
`CameraViewer.tsx:224` catch". It is **out of scope and correctly so**, and the reason is
stronger than "it has a comment": it is *not silent*. The `catch` leaves `applied` false,
and `:302–305` then reports:

```ts
if (!applied && !reportedNoPlayoutRef.current) {
  reportedNoPlayoutRef.current = true;
  logResilienceEvent('stream', 'playout-target-unsupported', { cameraIdentifier });
}
```

with the comment *"A throw is not an application, so it falls into the report below
rather than out of this effect."* A throw there is already loud. **One observation, not
a finding:** it is reported under a transition that names *unsupported*, so a receiver
that exists and explodes is filed as a receiver that has no `jitterBufferTarget`. The
comment says that conflation is intended. Not changed here, and not proposed.

`kioskLatency.ts:110` — untouched, per the issue and per its own comment. It wraps a
network POST, which is the one thing in this area that genuinely must not surface.

### The convention this fix follows already exists, twice, in two shapes

**Shape A — `reportMissingStatsField` (`CameraViewer.tsx:137–145`, spec 095).** A
component-scope `useRef<Set<string>>`, keyed by field name, **shared by both samplers**,
reporting each field once for the life of the mounted tile. Its 27 lines of comment
(`:109–136`) argue three things this spec must respect: a ref not state (a write would
re-render live video); component scope not effect scope (the effects are keyed on
`status`, so a flapping tile would re-report on every recovery); and per **mounted tile**
not per camera (`CellPage` keys tiles on `positionKey(row, col)`).

**Shape B — `countReportableSkew` (`CellPage.tsx:496–530`, #2084).** This is the shape
the issue points at, and here it is verbatim rather than reconstructed:

```ts
function countReportableSkew(
  counter: { current: number },
  frameFab: string | undefined,
  wallFab: string | undefined,
): number | null {
  if (wallFab === undefined) return null;
  if (typeof frameFab === 'string' && frameFab !== '') return null;
  counter.current += 1;
  const count = counter.current;
  let decade = 1;
  while (decade < count) decade *= 10;
  return decade === count ? count : null;
}
```

called as `logResilienceEvent('hub', 'resolved-text-without-fab', { overlay, count })`
(`CellPage.tsx:195`), against **two separate refs** declared at `:104–105` under this
comment:

> Counted per route, never shared: a server that carries `fab` on one frame type and not
> the other is exactly the skew this reports, and one counter would hide it.

Its doc comment states the cadence rule: *"Bounded at the first occurrence and every
power of ten thereafter: a wall runs for weeks, so a line per frame evicts the first —
the diagnostically valuable — occurrence, and a line per session is emitted before anyone
is looking."*

### The channel

`logResilienceEvent(subsystem, transition, detail)` →
`console.info('[resilience]', { subsystem, transition, ...detail })`
(`apps/shared/src/observability/resilienceLog.ts:9–14`). `subsystem` is a closed union
(`'stream' | 'hub' | 'session' | 'crash'`); `transition` and `detail` are open. Existing
`'stream'` transitions in this file: `stats-field-missing`, `playout-target-unsupported`.
**It reaches `console` and nothing else** — which is the whole of why item 3 is deferred.

### Claims from the brief that did **not** verify

- **"`CameraViewerMedia.test.tsx` … confirm it's a suitable home."** It is **not the best
  home, and the throw is hard to construct there.** That file uses the **real**
  `WhepClient` over a fake `RTCPeerConnection` (`:70–115`) that has **no `getStats`** —
  and `WhepClient.stats()` answers `null` rather than throwing for exactly that case
  (`WhepClient.ts:120–122`), so the sampler returns at `if (report === null) return;`
  before reaching anything that can throw. Making it throw there means adding a rejecting
  `getStats` to that double. Meanwhile `CameraViewerAlignment.test.tsx` **already** mocks
  the whole `WhepClient` module with `stats = () => statsBehaviour()` whose `beforeEach`
  default is `statsThrows`, already owns a `resilienceLines(transition)` helper and a
  `flapThroughReconnect()` harness, and already contains *"Keeps showing video when
  reading the tile lag throws"*. **The red tests go there.** This is a correction to the
  brief, not a disagreement about the colour.
- **"the shape #2084 landed for the missing-`fab` case" being the same mechanism as
  `reportMissingStatsField`.** They are two different mechanisms — a decade-counting
  `number` ref (#2084) and a once-ever `Set<string>` ref (spec 095). The issue asks for
  the first; the brief asks whether to reuse the second. The answer below is **neither
  reuse nor extension: a parallel mechanism**, argued in §*Decision*.
- **ADR-0084's 300-LOC file limit.** It exists (S104) but binds the **backend** analyzers;
  the frontend row of that ADR names ESLint `max-lines-per-function`, and **no ESLint
  config in this repo sets it** — `apps/shared/eslint.config.js` configures only
  `no-unused-vars` on top of the recommended sets. `CameraViewer.tsx` is already 382
  lines. So the file limit is **not** a constraint on this change, and this spec does not
  pretend it is.

---

## Decision

### 1. Narrow the catch to what must not throw — and after this, **nothing is swallowed**

Both `.catch(() => undefined)` handlers are **kept as catches and made to speak**. They
are not deleted, and the reason is specific rather than defensive: the IIFE runs inside a
`setInterval` callback, so a rejection that escapes it becomes an **unhandled promise
rejection** — a red console error, and on a kiosk a candidate for the crash reporter,
every 2 s forever. A wall must not do that.

What changes is that the rejection is no longer *discarded*. Everything inside the region
is either measurement (now reported) or already-swallowing at its own documented boundary
(`reportKioskLatency`). **Answer to the issue's item 1: nothing remains silently
swallowed in either sampler.**

### 2. A **parallel** mechanism, not a reuse or an extension of `reportMissingStatsField`

Two refs, one per sampler, at component scope:

```ts
const decodeSampleFailuresRef = useRef(0);
const lagSampleFailuresRef = useRef(0);
```

reported through one `useCallback` keyed on `[cameraIdentifier]`, mirroring
`reportMissingStatsField`'s signature style and taking the counter as an argument exactly
as `countReportableSkew` does:

```ts
logResilienceEvent('stream', 'decode-sampler-failed', { cameraIdentifier, count, reason });
logResilienceEvent('stream', 'lag-sampler-failed',    { cameraIdentifier, count, reason });
```

**Why not reuse `reportMissingStatsField`.** Its ref is keyed by *field name* and
deliberately **shared across both samplers**, because a missing `totalProcessingDelay` is
one fact about the browser engine that both samplers would otherwise report twice. A
throw is not that fact. The lag sampler's region contains `onLagMeasuredRef.current?.()`
— the wall's own code — which the decode sampler never runs; the two can fail
independently and for unrelated reasons, and they are **two different §IV legs**. Folding
them into one counter would mean a lag-sampler failure suppresses the decode sampler's
first report of its own, unrelated failure. That is #2084's own rule, in its own words:
*"one counter would hide it."*

**Why not extend it.** Widening a `Set<string>` keyed by stats-field name to also hold
exception identities gives one ref two meanings and one report-once policy where the two
cases want different cadences — a missing field is permanent (report once, ever), a throw
may be transient (report on a decade curve, so a recovery-then-relapse is still visible).

**The scoping rules are inherited unchanged**, because the reasoning transfers verbatim:
a ref not state (same re-render argument), **component scope not effect scope** (both
effects are keyed on `status`, so an effect-local counter resets on every reconnect and a
tile flapping through the night reports the same permanent fault hundreds of times), and
**once per mounted tile, not per camera** — `cameraIdentifier` in the detail names the
tile that noticed and does not scope the claim.

### 3. The cadence is #2084's decade curve, and the arithmetic is duplicated on purpose

A module-private `countReportableFailure(counter): number | null` in `CameraViewer.tsx`,
shaped after `countReportableSkew` minus the fab predicate: increment, then answer the
count at 1, 10, 100, 1000 and never otherwise.

**The one cost, recorded rather than hidden.** Three lines of decade arithmetic will then
exist in two files, in two apps. Extracting a shared
`apps/shared/src/observability/reportCadence.ts` and adopting it in `CellPage.tsx` was
considered and **rejected for this slice**: it makes a bug fix also a cross-app refactor
of a file this issue has no business in, against CLAUDE.md's *"A bug fix changes the bug,
nothing else"*, and `CellPage.test.tsx` carries characterisation for #2084 that would
then be at risk in a PR that is not about it. Recorded here so the gate can overrule, and
so that whoever adds the **third** site extracts it instead of copying it again.

### 4. Two transitions, not one

`decode-sampler-failed` and `lag-sampler-failed`. They name the legs that went quiet —
`SFU → kiosk decode` and `presentation buffer` + wall alignment respectively — and a
reader of a kiosk console needs to know which. Naming style matches the file's existing
`stats-field-missing` / `playout-target-unsupported` and #2084's
`resolved-text-without-fab` / `highlight-without-fab`.

`detail` carries `{ cameraIdentifier, count, reason }`. `reason` is the thrown value's
`message` (or `String(error)` for a non-`Error`), because without it the line says only
that *something* threw — and the whole point is to tell a `getStats` shape change from a
wall callback that blew up.

### 5. Item 3 — **explicitly deferred, not silently dropped**

> *"Distinguish 'sampler is broken' from 'no samples yet' wherever dashboards read these
> metrics."*

**Not reachable from the frontend, and out of scope for this issue.** The chain is:
`reportKioskLatency` → `POST /stream-distribution/streams/kiosk-latency` →
`StreamEndpoints.RecordKioskLatency` (`:143`) → `LatencyBudget.Record` → the
`SmartSentinelEye.Latency` `Histogram<double>` (`src/ServiceDefaults/LatencyBudget.cs:51–53`)
→ the Aspire dashboard, which ADR-0118 makes the single sink. `logResilienceEvent` writes
`console.info` and enters **none** of that.

For a dashboard to tell *broken* from *quiet*, a signal has to reach that meter. That
means either a new value in `KioskMeasurement` — a **closed set shared with the server**
whose own doc comment warns that *"a name added here before the server knows it produces
a kiosk that reports nothing and looks entirely healthy"*, requiring a matching arm in
`RecordKioskLatency`'s `switch` (`:178–184`) — or a new endpoint and counter in
StreamDistribution. Either is backend work in another bounded context and a change to the
browser→service telemetry contract **ADR-0122 governs**. That is also the honest reason
it needs its own issue: a contract crossing a process boundary is the part of this that
might need an ADR, and folding it in here would smuggle that decision into a defect fix.

**What is delivered instead** is the half a browser owns: the `[resilience]` line makes
"this instrument is broken" nameable from a kiosk remote-debug session and assertable
from Playwright, which is where every other resilience signal in this app is read
(`WhepClient.ts`, `useWhepSession.ts`, `CellPage.tsx`, and `resilienceLog.ts`'s own
comment calling the prefix an observable contract).

**Recommended follow-up issue** (to be filed at phase 7, not created by this spec):
*"A kiosk sampler that has stopped measuring should be visible at the sink, not only in
the tile's console"* — referencing #2189, #1714, ADR-0122, ADR-0117.

---

## Does this need an ADR? — **No.** The argument, so the gate can overrule it

Nothing here is a decision; every part is an application of one already made.

- **The channel** is spec 011 FR-017's `logResilienceEvent`, unchanged in shape.
- **The cadence** is #2084's, already landed and in production use.
- **The scoping** is spec 095's, already landed in this very component.
- **No contract crosses a process boundary.** The change adds `console.info` lines in an
  existing, already-Playwright-visible format. `transition` and `detail` are open by
  construction, and two new `'stream'` transitions landed under spec 095 with no ADR.
- **No new dependency, no new module, no new file in `src/`.**

**The one thing that *would* need an ADR is item 3** — a new browser→service telemetry
signal, which ADR-0122 governs. That is precisely why it is deferred rather than folded
in. ADR-0144 forbids the autonomous lane from writing an ADR; the lane's correct move is
therefore to deliver items 1–2 and file item 3, which is what this spec does.

**Overrule this if** the gate holds that the `[resilience]` line is a versioned contract
whose transition vocabulary must be recorded — in which case stop at this gate and raise
an ADR, because the lane may not write one.

---

## Latency budget impact — the observers of two legs, and no cost to either

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| **SFU → kiosk decode** | ≤ 120 ms | **Observed** by the `:158` sampler. Budget unchanged. |
| **Presentation buffer** | ≤ 200 ms | **Observed** by the `:208` sampler, which also feeds ADR-0128's alignment controller. Budget unchanged. |
| Event → overlay state | ≤ 200 ms | No |
| Composite + render | ≤ 50 ms | No |

**No leg moves.** The samplers run at 5 s and 2 s, three orders of magnitude off the path
they observe (the reasoning at `CameraViewer.tsx:25–27` and `:43–47`). The new code runs
**only on the failure branch**: one increment, a `while` loop bounded by
`log10(count)`, and at most one `console.info` per decade. The healthy path gains nothing
at all — not even a branch, since the `.catch` handler already existed.

**What does change is §IV's record.** The presentation-buffer leg is *recorded, not yet
observed*, and #1714 is open because nobody has yet read a figure off a running wall.
Until this lands, a wall could be read as "the budget holds" from a tile whose instrument
stopped — a discharge nobody earned, which is the exact failure the constitution names for
this section. After it lands, a tile that stopped measuring says so.

---

## User stories

### US-1 (P1) — A sampler that throws says so, once per decade, naming its leg

**As** an engineer measuring the §IV legs off a running kiosk,
**I want** a tile whose latency sampler throws to name the failure on the console,
**so that** a quiet dashboard means no traffic rather than a broken instrument.

Independently shippable and independently observable: open a kiosk's devtools, break the
instrument, see the line. It is the whole slice.

### US-2 (P3 — delivered only as guard coverage, files nothing)

**As** the same engineer, **I want** a healthy sampler to stay silent, **so that** the
new signal does not become the firehose it exists to avoid. Green before and after; it
bounds the fix rather than establishing behaviour.

---

## Functional requirements

- **FR-001** — A rejection from the decode sampler's IIFE (`CameraViewer.tsx:165–185`)
  MUST be reported through `logResilienceEvent('stream', 'decode-sampler-failed', …)`
  rather than discarded.
- **FR-002** — A rejection from the lag sampler's IIFE (`:216–258`) MUST be reported
  through `logResilienceEvent('stream', 'lag-sampler-failed', …)` rather than discarded.
- **FR-003** — Each report MUST carry `{ cameraIdentifier, count, reason }`, where
  `count` is the running failure count for that sampler and `reason` is the thrown
  value's message.
- **FR-004** — Reports MUST be bounded at the **first** occurrence and every **power of
  ten** thereafter, per sampler (#2084's cadence).
- **FR-005** — The two counters MUST be **independent**. A failing lag sampler MUST NOT
  suppress the decode sampler's first report, and vice versa.
- **FR-006** — The counters MUST live at **component scope**, so a tile that goes
  `live → reconnecting → live` does not re-report from one.
- **FR-007** — A sampler that does **not** throw MUST produce no `*-sampler-failed` line.
- **FR-008** — Behaviour other than the logging MUST be unchanged: the sample is still
  dropped, the interval still fires, the tile still shows video, and
  `reportMissingStatsField` still governs the missing-field case on its own terms.
- **FR-009** — A rejection MUST NOT escape as an unhandled promise rejection.
- **FR-010** — `kioskLatency.ts`'s inner `catch` (`:110`) and `CameraViewer.tsx`'s
  `setPlayoutTarget` `catch` (`:286–291`) MUST be left exactly as they are.

## Non-functional requirements

- **NFR-001** — `apps/shared/src/ui/composites/CameraViewer.test.tsx` stays
  **byte-identical** and green. It asserts `toEqual` over the **complete** array of
  `[resilience]` lines (`:314–328`), so any line leaking into it is a failure. It cannot:
  its fake has no `getStats`, `WhepClient.stats()` answers `null` for that, and `goLive()`
  advances no timers. Verified at phase 1; pinned by a guard test at phase 4a.
- **NFR-002** — No new file under `apps/shared/src/observability/`, no change to
  `Shared.Contracts`, no backend change, no new dependency.
- **NFR-003** — `pnpm --filter @smart-sentinel-eye/shared lint` and `typecheck` clean;
  Prettier clean.

---

## Acceptance scenarios (Gherkin)

### Happy — the instrument breaks and says so

```gherkin
Given a kiosk tile that is Live and reporting its lag to a wall
When its receiver statistics read throws on every tick
Then the console carries one [resilience] line with transition "lag-sampler-failed"
  And that line carries cameraIdentifier, count 1, and the thrown message
  And the tenth consecutive failure carries a second line with count 10
  And the second through ninth carry no line at all
  And the tile is still showing video
```

### Conflict — two instruments, two independent verdicts

```gherkin
Given a tile whose statistics read succeeds
  And a wall callback that throws every time it is called
When twenty seconds pass
Then the console carries at least one "lag-sampler-failed" line
  And no "decode-sampler-failed" line at all
```

### Bad request — the healthy path stays silent

```gherkin
Given a tile whose statistics read succeeds and whose wall callback returns
When twenty seconds pass
Then the console carries no "decode-sampler-failed" line
  And no "lag-sampler-failed" line
```

### Conflict (2) — a flap is not a new fault

```gherkin
Given a tile whose statistics read throws
When the tile goes live, flaps through reconnect, and goes live again
Then the failure count continues from where it was
  And the second live window emits no line until the next decade boundary
```

### Auth — not applicable, and why

Nothing here crosses a trust boundary. The signal is a `console.info` in the operator's
own browser; no token is read, no request is made, no server is contacted. The only
outbound call in the region — `reportKioskLatency`'s POST — is untouched and still
bearer-authenticated as before. **`reason` is a message from a browser API or from this
app's own callback; it carries no credential and no operator data.**

---

## Independent end-to-end test procedure (phase 5)

Unit tests are not the discharge here; a `console.info` is observable in a real browser
and should be observed in one.

1. `aspire run` the stack; open `kiosk-web` on a cell with at least two tiles; wait for
   two tiles to reach Live.
2. Open devtools → Console, filter `[resilience]`.
3. In the console, break the instrument on the live page:
   `RTCPeerConnection.prototype.getStats = () => Promise.reject(new Error('forced'));`
4. **Within ~5 s** a `decode-sampler-failed` line appears with `count: 1` and
   `reason: 'forced'`; **within ~2 s** a `lag-sampler-failed` line appears likewise.
5. Wait 20 s. **Exactly one further** `lag-sampler-failed` line appears, `count: 10`. No
   further `decode-sampler-failed` line (4 ticks in 20 s — its 10th is at ~50 s).
6. **The tiles keep showing video throughout.** This is the ADR-0128 FR-013 obligation and
   the single most important observation of the run.
7. Restore `getStats` and confirm no further lines and that latency reporting resumes
   (`[latency]` lines return).
8. Record the lines verbatim in the verification note.

---

## Locked tech choices

React 19 + TypeScript, `apps/shared` composite consumed by both apps (ADR-0074);
`logResilienceEvent` as the channel (spec 011 FR-017); Vitest + Testing Library +
`vi.useFakeTimers()`; the existing `vi.mock('@smart-sentinel-eye/shared/streaming/WhepClient')`
double. No new library, no new module, no new pattern.
