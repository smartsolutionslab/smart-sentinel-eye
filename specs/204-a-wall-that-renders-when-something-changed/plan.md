# Plan — Spec 204, a wall that renders when something changed

Frontend-only. No bounded context, no domain model, no messaging, no
migration, no contract. The layering statement this plan owes is therefore
about the **React data-flow boundary** between the wall and its tiles, which
is where the defect lives.

## 1. The two directions in #2303, decided

### Rejected — direction 1: surface skew on the badge

#2303 offers it as "it was added for #1931". **Investigated, and the premise
does not hold.**

- **#1931 is CLOSED**, and it was not about a badge. Its title: *"Kiosk latency
  cannot be read per tile: the camera is validated by the endpoint and then
  dropped before the metric."* Its body is entirely about
  `LatencyBudget.Record` having no `camera` tag, so `overlay_draw` and its
  siblings could not be read per tile.
- **Its intent is already served, in this very file.**
  `useWallAlignment.ts:195-208` calls
  `reportKioskLatency('wall_skew', camera, skew, getTokenRef.current)` for the
  **laggiest held tile**, and the comment above it cites #1931 by number:
  *"a wall reporting one blended figure hides the tile that is out (#1931),
  and naming the tile that set the number is what makes it actionable."* The
  skew is attributed per tile, through a service, exactly as #1931 asked. The
  React state was never the thing #1931 wanted.
- **No requirement asks for it on screen.** Spec 045 FR-008 requires the skew
  be *measured*; FR-009 requires it to reach observability **through a
  service** (ADR-0122, which also forbids a telemetry SDK in the kiosk
  bundle). FR-012 requires a tile the wall *could not hold* to be visible —
  that is `TileAlignmentBadge`, which exists and is untouched. Nothing asks an
  operator to read a millisecond spread off a video wall.
- **It would make the defect permanent.** Painting skew converts an
  accidental every-cycle wall re-render into a *justified* one, on the leg
  ADR-0123 already measured over budget, on a path that runs forever. The
  cheapest way to lose this issue is to give the dead write a reason to live.
- ADR-0036 / constitution: no speculative generality; smallest possible
  change. Unrequested UI on a 24/7 production wall is the opposite.

### Chosen — direction 2, in its smallest honest form: **the tile pulls its own age**

Three edits, none of them a new mechanism:

1. **Delete the dead state.** `useWallAlignment.ts`: remove `const
   [skewMilliseconds, setSkew] = useState<number | null>(null)` (`:103`), the
   write at `:181`, the interface member at `:70`, and the publish at `:252`.
   **`const skew = skewAcross(heldLags)` at `:180` stays** — it is the input
   to the `wall_skew` report at `:206`, which is the half that was ever
   load-bearing.
2. **Hand the tile the getter, not the value.** `CellPage.tsx:369` changes
   from `frameAgeMilliseconds={alignment.frameAgeFor(cell.key)}` to passing
   `frameAgeFor={alignment.frameAgeFor}` plus `tileKey={cell.key}`, and
   `Tile` calls `frameAgeFor(tileKey)` during **its own** render, feeding the
   result into the unchanged `useLabelDelay(liveText, age, reportHeld)` at
   `:498`.
3. Nothing else.

#### Why a getter, and not a dedicated timer

The issue's own direction 2 suggests "a dedicated interval/timer". **That
would be worse, and at fab scale considerably worse.**

- A per-tile timer on a 250-camera fab is 250 timers doing exactly what the
  one they replace did: waking the main thread to re-render for a value that
  usually has not changed the outcome. The constitution's scale NFR is the
  argument against it, not for it.
- A single shared wall-level timer is the mechanism that already exists — the
  settle interval — so "add a shared timer" is "keep the defect, rename it".
- **And no timer is needed at all**, because the age has exactly two consumers
  and both already re-render the tile on their own:
  - `useLabelDelay` computes `delay = labelDelayFor(frameAgeMilliseconds)` and
    schedules the hold inside the effect keyed on `[text]`
    (`useLabelDelay.ts:79-112`), reading `delayRef.current`, which the
    preceding effect (`:66-73`) refreshes on **every** render. A `text` change
    arrives through `useGetOverlaySnapshotQuery` / `useGetOverlayQuery` —
    both of which live **inside `Tile`** (`CellPage.tsx:449`, `:476`). So the
    tile is already re-rendering at the exact instant the age is consumed.
  - `holding` gates the early-release effect (`:120-129`). See §4.

So the fix replaces a **push from a parent with no reason to re-render** with
a **pull at the moment of use**. That is strictly less machinery than today,
not more, and it is why this direction also fixes the 1×1 wall (SC-5) that
never had a push at all.

## 2. The data-flow boundary this restores

```
useWallAlignment ── lagsRef (per-tile samples, written every ~2 s)
      │                 │
      │ published       └── frameAgeFor(tileKey)   ← stable useCallback, [] deps
      │ state                                        reads the ref at call time
      │
      ├── released  ─ guarded by sameMembers  ─┐
      ├── held      ─ guarded by sameMembers  ─┤ these re-render the wall,
      └── target    ─ guarded by the ±33 ms   ─┘ and each one has a reason to
                       deadband
```

**The invariant, stated so a reviewer can check it:** *every* state published
by `useWallAlignment` is change-guarded, and a wall re-render means an
operator-visible decision changed — a badge, a playout target, or a tile's
membership of the wall's claim. A measurement is **not** a decision, and
measurements travel by ref.

`frameAgeFor` already honours that invariant. It was the *call site* that
broke it, by evaluating the getter one component too early and freezing the
result into a prop.

**Layer boundary:** `useWallAlignment` (the wall's controller) decides;
`Tile` (the tile) reads its own measurement. No new coupling is introduced in
either direction — `Tile` already receives `onLagMeasured` and
`playoutTargetMilliseconds` from the same hook, so `frameAgeFor` + `tileKey`
joins an existing prop channel rather than opening one.

## 3. The one thing phase 4a must not get wrong

**SC-3 and SC-4 are red only if the label change re-renders the *tile* and not
the *page*.**

`CellPage.test.tsx`'s existing label-ageing block forces its re-render with a
synthetic `onOverlayHighlightChanged` frame, and says why in a comment. That
frame sets page-level state, so it re-renders **CellPage**, which re-evaluates
`alignment.frameAgeFor(cell.key)` at `:369` and hands down a *fresh* age. A
test built that way **passes today** and proves nothing.

The label change must arrive the way production delivers it: through the RTK
snapshot cache. That path is already established in this file — the #2012
block uses `useSnapshotFromTheRealCache` plus
`store.dispatch(systemVariablesApi.util.upsertQueryData(...))`, and the
subscription re-renders only the `Tile` that binds the overlay. Reuse it.

Stated as an acceptance condition on phase 4a: **run SC-3 against unmodified
`useWallAlignment.ts` and `CellPage.tsx` and quote the failure.** A green
SC-3 before the fix means the wrong re-render trigger was used, not that the
defect is absent.

## 4. What is knowingly given up, and the sum

`useLabelDelay.ts:120-129` settles a pending hold early when `holding` goes
false — the age went unreadable, or crossed the 200 ms cap. `holding` is
derived per render, so today an age crossing the cap *during a hold* is
noticed on the next wall re-render.

After the fix it is noticed on the next **tile** render. A hold lasts at most
`LABEL_DELAY_CAP_MS` = 200 ms; the settle cycle is 2 s. So even today that
path catches a mid-hold cap crossing roughly one time in ten, and the
remaining nine were already settled by the timer reporting its full scheduled
delay — the exact inflation that effect's comment describes. The fix does not
make that worse in any way a measurement could distinguish, and buying the
last tenth costs a subscription mechanism larger than the defect.

**Recorded as given up rather than argued away.** Spec.md assumption 2 carries
it for the reviewer, and SC-6 asserts the property that actually matters here
— fail-open — still holds.

## 5. Re-anchoring the four `skewMilliseconds` assertions, without weakening a gate

`useWallAlignment.test.ts` asserts `skewMilliseconds` at `:51`, `:111`,
`:257`, `:271`. Deleting the member deletes their subject. **They must be
re-anchored, not removed** — ADR-0144 forbids reaching green by deleting a
test, and three of the four assert a real FR-004/FR-013 property.

All four move onto the `reportKioskLatency` spy, which the neighbouring test
*"Reports the induced skew, naming the tile that set it"* already sets up —
so there is no new harness:

| Line | Asserts today | Re-anchored to |
|---|---|---|
| `:51` | the induced spread is 100 ms | `reportKioskLatency` called with `'wall_skew'`, `'cam-c'`, `100` on that cycle |
| `:111` | a 1-tile wall publishes no skew | `reportKioskLatency` **not** called at all |
| `:257` | a wall shrunk to 1 tile publishes no skew | `reportKioskLatency` not called after the shrink |
| `:271` | a wall with no lags publishes no skew | `reportKioskLatency` not called |

This is a **strengthening**: each now asserts the figure *leaves the browser*
(spec 045 FR-008/FR-009, ADR-0122) rather than that it reached a React state
nothing read. The reviewer's check is that the count of meaningful assertions
did not drop.

## 6. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Phase 4a writes SC-3 with a page-level re-render trigger, sees green, and reports a "characterisation" test | §3 makes the red observation an explicit gate; `tasks.md` T002 quotes the required failure |
| R2 | The engineer "fixes" SC-1 by memoising `Tile`, which would mask the dead write rather than remove it | SC-F (`grep -rn skewMilliseconds apps/` returns nothing) fails if the state survives; `React.memo` is out of scope in spec.md |
| R3 | The four re-anchored assertions get deleted instead | §5 table is the checklist; phase 6 reads it |
| R4 | `react-hooks/refs` lint fires on the getter call inside `Tile` | The identical call exists unsuppressed at `CellPage.tsx:369` today; T006 runs `lint` and, if it does fire, the suppression carries the same reasoning already written at `CellPage.tsx:51-53` |
| R5 | A phase-5 verifier manufactures a millisecond improvement from a two-tile developer wall | spec.md "Latency budget" forbids the claim in advance; the render count is the evidence |
| R6 | `CellPage.test.tsx` (1,187 lines) conflicts with another branch | No open PR branch touches `apps/kiosk-web/src/features/cell/` (checked at dispatch); re-check before opening the PR |

## 7. What this plan will not do

No ADR is written or amended (ADR-0144 forbids it in this lane, and none is
needed — §1 shows the decision is already made by ADR-0122, ADR-0128 and
ADR-0129). No constitution §IV cell changes. No new file outside tests. No
dependency added.
