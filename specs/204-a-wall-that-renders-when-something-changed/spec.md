# Spec 204 — A wall that renders when something changed

- **Issue:** #2303 (`bug`, `agent:ready`, Project #13 → Todo — verified
  2026-09-21 via `gh issue view 2303 --json labels,projectItems`; no
  `agent:blocked`)
- **Branch:** `2303-dead-skew-state`
- **Worktree:** `D:/Github/sse-2303`
- **Base:** `origin/develop` @ `9ff472fe`
- **ADRs:** ADR-0037 (workflow), ADR-0128 (a wall is aligned by receiver
  playout — this is the hook that implements it), ADR-0129 (a label is *aged*
  to match its picture — the correctness property at risk here), ADR-0123 (the
  composite + render leg is the operator's wait, and a wait has a cadence
  floor), ADR-0122 (browser measurements reach observability through a
  service — why the skew *metric* stays), ADR-0139 / constitution §Testing
  (new behaviour starts red), ADR-0109 (`[P]` disjoint-file parallelism),
  ADR-0036 (smallest possible change; no speculative generality).
  Constitution **§IV** — `Overlay composite + render ≤ 50 ms`.
- **No ADR is needed.** Everything here is a defect *inside* the design
  ADR-0128 and ADR-0129 already fixed. Nothing about how a wall aligns, what a
  skew figure means, or how a label is aged changes.

## Problem

`apps/kiosk-web/src/features/cell/useWallAlignment.ts:103` declares
`const [skewMilliseconds, setSkew] = useState<number | null>(null)`. The
settle loop writes it at `:181`, and the hook publishes it at `:252`.

**Nothing on the wall reads it.** It reaches `WallAlignment.skewMilliseconds`
and stops there — `CellPage.tsx` never mentions it, and `grep -rn
skewMilliseconds apps/` finds the declaration, the write, the publish, the
interface member, and four assertions in the hook's own test. No pixel.

It is also **the one unguarded write in the settle cycle**. `setReleased`
(`:161`) and `setHeld` (`:170`) compare membership through `sameMembers`
before allocating a new `Set`; `setTarget` (`:182-193`) has the ±33 ms
deadband. `setSkew` publishes whatever `skewAcross` returned. `skewAcross` is
`max(lag) − min(lag)` over live per-frame measurements, so it is a float that
essentially never repeats — which means every settle cycle re-renders every
tile of the wall, every two seconds, for a number nobody paints.

That sits on constitution §IV's **`Overlay composite + render ≤ 50 ms`** leg,
which is the leg this repository has already measured **over budget** — p50
**54.2 ms**, p95 79.2 ms (ADR-0123, #1891).

### And removing it breaks something real

`CellPage.tsx:369` computes `frameAgeMilliseconds={alignment.frameAgeFor(cell.key)}`
*during CellPage's render*, and `frameAgeFor` (`:243`) reads `lagsRef.current`
— a ref, not state. The value is handed to `Tile` as a **prop**, and from
there to `useLabelDelay` at `CellPage.tsx:498`. A prop only changes when the
parent re-renders. So the *only* thing keeping a tile's label age current is
the wall re-render that `setSkew` accidentally causes.

Delete the dead state as an obvious cleanup and label ageing (ADR-0129)
silently stops updating. A correctness property rides on a performance defect.

### Premise check — re-verified at HEAD `9ff472fe`, and corrected

| Claim in #2303 | Verdict |
|---|---|
| `useWallAlignment.ts:181` is `setSkew(skew)` | **Confirmed**, verbatim |
| consumed at `:252` as `skewMilliseconds` | **Confirmed**, verbatim |
| `CellPage.tsx` never reads `skewMilliseconds` | **Confirmed** — zero hits |
| `setReleased` / `setHeld` / `setTarget` are change-guarded | **Confirmed** — `sameMembers` at `:161`, `:170`; deadband at `:192` |
| `frameAgeFor` is re-sampled at `CellPage.tsx:297` | **Line moved to `:369`**; the code is identical |
| Neutering `setSkew` fails exactly 1 kiosk test — `useWallAlignment`'s own | **Reproduced.** 1 failed, 162 passed (the suite is 163 now, not 141): `useWallAlignment > Drives an induced spread to the slowest tile` |
| `CellPage.test.tsx` and `useLabelDelay.test.ts` are blind to the coupling | **Confirmed** by the same run |

**Correction 1 — "every 2 seconds" needs one more word to be true.** React
bails out of a `useState` write when `Object.is(next, current)`, so a skew
that came back *identical* re-renders nothing. Measured: a converged two-tile
wall fed a **constant** 10 ms spread re-rendered its tiles **0 times across 5
settle cycles**; the same wall fed a spread jittering by fractions of a
millisecond re-rendered them **8 times across 5 cycles** — 4 cycles × 2 tiles,
i.e. every tile on all but one cycle. Real `jitterBufferDelay` deltas are
floats. So the defect is real and continuous in production, but the mechanism
is "a live float practically never repeats", not "React re-renders on every
write". **Correction, phase-6 review:** the requirement is not "no round
numbers" — varying integers reproduce the defect fine, as long as the spread
genuinely changes cycle to cycle and stays inside the deadband so the render
is attributable to `setSkew` rather than a legitimate `setTarget` move. What a
phase-4a test must avoid is a *constant* spread, which hits React's own
`Object.is` bail-out and asserts nothing regardless of whether the values are
round.

**Correction 2 — the coupling is narrower, and sharper, than the issue says.**
A second lag report reaches `useLabelDelay` today for **two** reasons, and
`setSkew` is only one of them. Measured with `setSkew` neutered:

- second report moving the target **outside** the ±33 ms deadband (100→150) —
  the age still arrives, carried by `setTarget`'s own re-render. **Passes.**
- second report moving the target **inside** the deadband (100→105) — the
  wall does not re-render at all and `useLabelDelay` is never called again.
  Ages seen after the first settle: `[null, null, 100, 110]`. After the
  second: `[]`. **Fails.**

The second case is the steady state — the deadband exists precisely so the
target stops moving. So the coupling holds, and it holds exactly where the
controller is working correctly.

**Correction 3 — a one-tile wall never had this crutch.** Below two tiles the
settle interval is never created at all (`:114`, `:116-135`), so on a 1×1
layout nothing in `useWallAlignment` has *ever* re-rendered the page, and the
tile's label age has only ever refreshed on an incidental render. The existing
label-ageing tests in `CellPage.test.tsx` run on a 1×1 layout and force their
re-render with a synthetic highlight frame, with a comment saying why. So
"label ageing depends on the dead state" is true of multi-tile walls only, and
the single-tile wall is a second, quieter instance of the same design fault:
**the age is pushed from a parent that has no reason to re-render.**

### Evidence commands (re-runnable)

```sh
# counterfactual — patch :181 to `setSkew((current) => current);`
cd apps/kiosk-web && npx vitest run
#  → Tests  1 failed | 162 passed (163)
#     FAIL  src/features/cell/useWallAlignment.test.ts
#           > useWallAlignment > Drives an induced spread to the slowest tile
```

## What this is not

**It is not "surface skew on the badge" (#2303's direction 1).** That
direction is rejected with a reason, in `plan.md` §1. The short form: #1931 is
**closed**, and it was never about a badge — it was about the kiosk latency
*metric* dropping its camera tag. Its intent is already served, at
`useWallAlignment.ts:200-208`, by `reportKioskLatency('wall_skew', camera,
skew, …)` attributing the spread to the laggiest held tile — and the comment
there cites #1931 by number. Spec 045's FR-008 and FR-009 require the skew to
be *measured* and to reach observability *through a service* (ADR-0122); no FR
asks for it on screen. Painting it would be unrequested UI that also
*entrenches* the every-cycle wall re-render, on the one leg already over
budget.

**It is not a change to how a wall aligns.** `settleAlignment`, `skewAcross`,
the deadband, the hysteresis, the release rule and the `wall_skew` metric are
all untouched.

**It is not a change to `useLabelDelay`'s contract.** Its signature, its
15 existing tests, and `labelDelay.ts` stay exactly as they are.

**It is not inter-display synchronisation.** Out of scope, unbuilt (ADR-0128).

## Locked tech choices

React 19 + TypeScript + Vite (ADR-0074), vitest + Testing Library, no new
dependency, no new file outside tests. `kiosk-web` only; `apps/shared` and
`management-web` are untouched. No backend, no contract, no migration.

## User stories

### US1 (P1) — A wall does no work when nothing changed

*As the fab floor,* the wall in front of me spends its main thread on
decoding and compositing video, not on re-rendering sixteen tiles every two
seconds to publish a number nobody shows me.

Independently shippable and independently observable: the render count is
assertable in vitest and the wall keeps behaving identically. This is the
whole feature — there is no US2. Label ageing is not a second story; it is
US1's **precondition**, and SC-3 and SC-4 below are what stop US1 from being
shipped by breaking it.

## Acceptance scenarios

### SC-1 — the steady-state wall is quiet (happy, render count)

```gherkin
Given a two-tile wall whose tiles have converged and are held
  And each tile reports a lag whose spread jitters by a fraction of a millisecond
 When five settle cycles elapse with no label change, no highlight,
     no release, and a target that stays inside the ±33 ms deadband
 Then no tile is re-rendered
```

**Red today**: measured at 8 tile renders over those 5 cycles. The assertion
is a **count**, not "the right label is still shown".

### SC-2 — a wall that has something to say still says it (happy, no over-fix)

```gherkin
Given a converged two-tile wall
 When one tile's lag rises far enough to move the wall's target outside the
      ±33 ms deadband, or to earn it the out-of-alignment badge
 Then the tiles re-render
  And the badge and the new playout target reach the tile as they do today
```

The fix must silence the *dead* write, not the live ones. `setReleased`,
`setHeld` and `setTarget` keep their existing behaviour unchanged.

### SC-3 — a label is held for the age most recently reported (happy, ageing)

```gherkin
Given a tile bound to an overlay, on a converged wall, whose last settled
      lag report was 40 ms
 When the tile reports a 180 ms lag
  And the overlay's label text changes before the next settle cycle
 Then the new label is still withheld 40 ms later
  And the new label is shown 180 ms after the change
```

**Red today**: the tile is handed the age its *parent* last rendered with, so
it schedules a 40 ms hold and the label appears 140 ms early.

This is the spec's answer to #2303's "add a `CellPage` test asserting a second
lag report reaches `useLabelDelay`". **The issue's literal wording is not
implementable against the fix, and that is the point**: after the fix a second
lag report causes no re-render at all, so "`useLabelDelay` is called again" is
false *by design* while the feature is more correct than before. The property
worth asserting is the one the call was a proxy for — that the age the hold is
computed from is the age most recently measured. Deviation recorded here
deliberately; see "Assumptions, marked".

### SC-4 — the coupling cannot come back (regression, ageing)

```gherkin
Given a two-tile wall whose second lag report leaves the target inside the
      ±33 ms deadband
 When that report is made and a settle cycle elapses
  And the label text then changes
 Then the hold uses the second report's age, not the first's
```

The exact scenario measured in "Correction 2". Today this is green only
because of `setSkew`. After the fix it must be green *without* it — which is
the difference between removing a defect and removing a defect and a feature.

### SC-5 — a one-tile wall ages its label too (edge)

```gherkin
Given a 1×1 layout, where the settle loop never runs
 When the tile reports a 150 ms lag
  And its label text then changes
 Then the label is withheld for 150 ms
```

**Red today** (Correction 3), and green after the fix for free, because the
tile stops depending on a parent render it never gets. Recorded as an
acceptance scenario rather than a bonus so the fix is not narrowed to
multi-tile walls.

### SC-6 — an unreadable age still shows the label at once (bad input)

```gherkin
Given a tile that has never reported a lag, or whose sample has aged out of
      the controller's 15 s staleness window
 When its label text changes
 Then the label is shown immediately, with no timer
```

FR-011 of spec 046, unchanged. `labelDelayFor(null)` → `null` → not held.
This is the *fail-open* property, and a fix that made the age unreadable
would pass SC-1 while quietly disabling ADR-0129 — so it is asserted.

### SC-7 — the skew metric still reaches observability (auth / telemetry)

```gherkin
Given a converged wall with a held, laggiest tile
 When a settle cycle completes
 Then `reportKioskLatency('wall_skew', <that tile's camera>, <the spread>, getToken)`
      is called exactly once, with the kiosk's bearer token
```

Spec 045 FR-008/FR-009 and ADR-0122. This is the leg of the skew figure that
was ever load-bearing, and the one #1931 asked for. Removing the React state
must not remove the report. There is no other auth surface in this change —
nothing crosses a trust boundary, no endpoint, no scope.

## Independent end-to-end test procedure

Nothing here needs the Aspire stack, a camera, or a running SFU. The whole
change is inside the kiosk bundle.

1. `cd D:/Github/sse-2303 && pnpm install --frozen-lockfile`
2. `cd apps/kiosk-web && npx vitest run` — 163 existing tests green before,
   all green after (the four `skewMilliseconds` assertions in
   `useWallAlignment.test.ts:51,111,257,271` are replaced, see `tasks.md`
   T004).
3. `npx vitest run src/features/cell` — the new render-count and ageing tests.
4. `pnpm --filter @smart-sentinel-eye/kiosk-web typecheck && pnpm --filter
   @smart-sentinel-eye/kiosk-web lint`
5. **Observed, not only asserted** (phase 5): boot the wall against the Aspire
   stack with a published two-tile layout, open DevTools → React Profiler,
   record 20 s of a settled wall. Before: a commit roughly every 2 s naming
   every tile. After: no commits between genuine events. Then change a system
   variable and confirm the label still appears, held.

## Latency budget

**Leg: `Overlay composite + render ≤ 50 ms`** (constitution §IV).

This change **reduces** work on that leg. It does not move a budget line and
needs no re-baselining of the leg's own figure, because `overlay_draw`
measures a *label change*, and this change does not add or remove label
changes — it removes renders that produce no label change at all and are
therefore invisible to that instrument.

The argument that it matters anyway is ADR-0123's, not a new one. ADR-0123
decided the leg is the **operator's wait**, frame wait included, and derived
from that a **cadence floor**: the median breaches below 30 Hz and the tail
below 40 Hz. The wall #1891 measured was running at **27 Hz** with four
decodes. React reconciliation of every tile's subtree runs on the same main
thread as the compositor that has to sustain that cadence. So gratuitous
renders are charged to this leg *through the frame rate*, which is exactly
the mechanism ADR-0123 §3 tells an investigator to read first.

**What is honestly claimable, and what is not.** In vitest the render
reduction is exact and deterministic (SC-1). On a real wall the cadence
improvement from removing one reconciliation pass every two seconds on a
two-tile developer wall is very likely **below the noise floor** — the
measurement-runs-need-repeating lesson applies, and a single before/after
`overlay_draw` p50 on this hardware will not resolve it. **The spec does not
claim a measured millisecond, and phase 5 must not manufacture one.** The
claim is: work removed from the cadence-critical thread, scaling with tile
count, on a leg already over budget, with the render count as the hard
evidence.

**No other leg is touched.** The presentation-buffer leg's control loop keeps
its 2 s cadence, its deadband, its hysteresis and its `wall_skew` report
unchanged; ADR-0129's label ageing keeps its cap, its fail-open behaviour and
its `label_delay` report unchanged. The §IV leg table needs no edit — no
Implemented / Measured / Dashboard cell changes.

## Out of scope

- Painting skew, or any other alignment figure, in the UI (rejected, plan §1).
- Memoising `Tile` with `React.memo`. It would be a second, larger change
  with its own prop-identity hazards, and it is not needed once the dead write
  is gone. If a later profile says the remaining renders matter, that is its
  own issue.
- Changing `SETTLE_INTERVAL_MS`, the deadband, the staleness window, or any
  controller arithmetic.
- `useLabelDelay`'s signature, `labelDelay.ts`, and the 15 tests over them.
- `management-web` and `apps/shared`.
- #1714 (the whole-path re-measurement with alignment active). Untouched and
  still open.

## File contention

Three source files, all under `apps/kiosk-web/src/features/cell/`, plus their
tests. **No other agent may hold these during phase 4**, and no `[P]` marker
is applied across them:

- `useWallAlignment.ts` / `useWallAlignment.test.ts`
- `CellPage.tsx` / `CellPage.test.tsx`
- (read-only) `useLabelDelay.ts`, `apps/shared/src/observability/wallAlignment.ts`

`CellPage.test.tsx` is 1,187 lines and is the single busiest test file in the
kiosk app; a conflicting branch touching it is the main integration risk.
Checked at dispatch: no open PR branch touches `apps/kiosk-web/src/features/cell/`.

## Success criteria

- **SC-A**: A converged, jittering two-tile wall re-renders **zero** tiles
  across five settle cycles. Asserted as a count. Red before, green after.
- **SC-B**: A label whose text changes between settle cycles is held for the
  **most recently reported** age. Red before, green after.
- **SC-C**: The same holds on a 1×1 wall, where the settle loop never runs.
- **SC-D**: `reportKioskLatency('wall_skew', …)` still fires once per cycle
  with the laggiest held tile's camera.
- **SC-E**: All 163 pre-existing kiosk tests pass; `typecheck` and `lint`
  clean; `pnpm format:check` clean.
- **SC-F**: `grep -rn 'skewMilliseconds' apps/` returns **nothing** after the
  change.

## Assumptions, marked

1. **The deviation from #2303's literal test wording is deliberate.** The
   issue asks for "a `CellPage` test asserting a **second** lag report reaches
   `useLabelDelay`". After the fix, a second lag report *correctly* reaches
   nothing, because nothing needs to re-render. SC-3/SC-4 assert the stronger
   property the call count was standing in for: the age the hold is computed
   from is the age most recently measured. Flagged for the reviewer rather
   than silently substituted.
2. **`holding` flipping on a live age change is not a behaviour this fix
   preserves, and losing it is not a regression worth a line of code.** The
   early-release effect at `useLabelDelay.ts:120-129` settles a pending hold
   when the age crosses the 200 ms cap. A hold lasts at most 200 ms; the
   settle cycle is 2 s; so today that path fires during a hold roughly one
   time in ten already, and after the fix it fires on a tile-local render
   instead. Marked as a guess about *significance*, not about mechanism —
   if the reviewer disagrees, the counter-proposal is a `React.memo`-free
   ref-subscription, which is out of scope above.
3. **`frameAgeFor` reading a ref during render is not new and is not flagged
   by lint.** `CellPage.tsx:369` does it today with no `eslint-disable`; the
   fix moves the identical call from the parent's render into the tile's.
   `react-hooks/refs` does not see through the opaque getter. Verified by the
   fact that the current line carries no suppression — phase 4b must confirm
   `lint` stays clean rather than assume it.
4. **The 163-test baseline is this worktree's, at `9ff472fe`.** #2303 quotes
   141, from 2026-09-13. Both figures are correct for their date; phase 4a
   must re-read the baseline rather than quote either.
