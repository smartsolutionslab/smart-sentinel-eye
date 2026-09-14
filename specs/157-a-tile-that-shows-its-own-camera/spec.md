# Spec 157 — a tile that shows its own camera

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2370](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2370) · **Branch:** `fix/2370-a-tile-that-shows-its-own-camera`
**Lane:** autonomous (ADR-0144) — `#2370` carries `agent:ready`, board status *In Progress*.
**ADRs:** ADR-0075 (Redux Toolkit + RTK Query — the semantics at the centre of this),
ADR-0074 (two apps; the composite is shared), ADR-0123 (the composite+render leg is
the operator's wait), ADR-0128 (playout alignment — the actuator a teardown resets),
ADR-0117 (§VII binds implemented legs), ADR-0109 (`[P]` marking), ADR-0036 /
ADR-0139 (enforce rules, advise preferences), ADR-0144 (lane).
**Constitution:** §IV (latency budget and the four-state leg table), §VII
(observability), §Testing (two obligations).

**Latency budget (§IV): three legs cited, no cell moves, no measurement owed.**
See *§ Latency-budget impact* — the claim is earned there, not asserted here.

---

## The finding, corrected against the code

A wall cell reassigned from camera A to camera B keeps showing **camera A's
picture under camera B's identity**. On a 24/7 fab wall that is the one failure
an operator cannot detect by looking: the label, the overlay and the highlight
are all B's, and nothing on screen says A.

#2370's chain is right in two of three links, and the third is wrong in a way
that changes the fix. Both the correction and the original were re-derived from
`develop` at `3b4da87b`.

### Link 1 — confirmed. The tile is not remounted

`apps/kiosk-web/src/features/cell/CellPage.tsx:574-582` keys cells on
`positionKey(row, col)` alone, and `:350-355` renders `<Tile key={cell.key}>`. A
revision that puts a different camera at `(0,0)` therefore changes **only the
`cameraIdentifier` prop** of a mounted `CameraViewer`.
`apps/shared/src/ui/composites/CameraViewer.tsx:113-126` records this as intended
(spec 095), and it stays intended. Nothing here proposes keying tiles on the
camera.

### Link 2 — confirmed. The stream query answers with the previous camera

`apps/shared/src/ui/composites/CameraViewer.tsx:100`:

```ts
const { data: stream, error: queryError } = useGetStreamQuery(cameraIdentifier, {
  pollingInterval: 5000,
});
```

RTK Query's `data` is the last successful result for **any** argument this hook
instance has been called with; `currentData` is the one that resets on an
argument change. So the instant the prop changes, `stream` — and therefore
`stream.whepUrl` — is still A's.

### Link 3 — **wrong as filed.** The effect does re-run; it re-dials the wrong camera

#2370 says the session effect *"does not fire, and the existing WHEP session to
camera A is never torn down"*, on the grounds that
`apps/shared/src/ui/composites/useWhepSession.ts:304` is keyed
`[whepUrl, offlineMessage, retryNonce, transitionTo]` and carries no camera.

It carries one, indirectly. `transitionTo` is

```ts
const transitionTo = useCallback(/* … logResilienceEvent(…, { cameraIdentifier }) … */, [cameraIdentifier]);
```

— `useWhepSession.ts:135-144`. Its identity changes when the camera does, and it
**is** in the dep array at `:304`. So on a reassignment the effect *does* re-run:
the cleanup at `:295-303` closes A's `WhepClient`, and the setup at `:283-293`
immediately builds **a new `WhepClient` against A's still-stale `whepUrl`** and
connects to it.

The outcome #2370 describes is exactly right. The mechanism is not "a session to
A that was never torn down" but **"a fresh session deliberately re-negotiated to
A after B was requested"** — the system re-dials the wrong camera. Three things
follow, and each changes the work:

1. **The renegotiation this fix is suspected of adding is already being paid
   today**, on every camera swap. That is the whole of the §IV answer below, and
   it is a code fact rather than a judgement.
2. **Adding `cameraIdentifier` to `:304` is behaviourally redundant on today's
   code.** It is still required — as an explicit dependency rather than a
   redundant one — for the reason in FR-004. It is not the fix, and this spec
   does not sell it as one.
3. **The third part of the decision carries the whole load**, and in a way
   #2370 does not state. See the next section.

### What `currentData` alone actually produces — the gap is not empty, it is A

Trace the swap with FR-001 applied and nothing else:

| Step | What happens |
|---|---|
| 1 | Prop A→B. `currentData` → `undefined`, so `whepUrl` → `undefined`. |
| 2 | Effect re-runs. Cleanup closes A's client — `teardownLocally` stops the receiver tracks and calls `pc.close()`. |
| 3 | `WhepClient` **never clears `videoEl.srcObject`** (`WhepClient.ts:273-277`; stated as an invariant at `useWhepSession.ts:229-233`). The element keeps A's `MediaStream` with ended tracks, so it **freezes on A's last decoded frame**. |
| 4 | Effect setup hits `if (!whepUrl \|\| !videoEl) return undefined;` (`:151`) and returns **without calling `transitionTo`**. `status` stays `'live'`. |
| 5 | `CameraViewer.tsx:357` renders `ViewerOverlay` only when `status !== 'live'`. Nothing is drawn. |

The tile therefore shows **camera A's frozen picture, labelled Live, under camera
B's overlay** — which is the alternative the decision explicitly rejected
("freeze the last frame with a reconnecting badge"), minus even the badge.

So FR-001 alone does not deliver the decision; it changes an indefinite wrong
*video* into an indefinite wrong *still*. FR-003 and FR-005 are what make the
wall stop vouching for a picture it cannot vouch for, and they need two
mechanisms, not one: **leave `live` when there is no session**, and **clear the
element so no frame of A survives**.

### When the wrongness is indefinite, and when it is brief

Worth stating precisely, because the severity claim rests on it:

- **B's stream read succeeds** (the ordinary case): the argument change triggers
  an immediate refetch, so `data` becomes B's within one gateway round-trip. The
  wrong-camera window is that round-trip, during which a whole WHEP negotiation
  to A is started and then abandoned. Visible as a brief wrong picture plus a
  wasted renegotiation.
- **B's stream read fails** (`GET /streams/{id}` 4xx/5xx/network): RTK retains the
  last successful result on error, so `data` stays A's **forever**, and
  `pollingInterval: 5000` re-errors every five seconds without ever dislodging
  it. The tile holds a live, actively-maintained session to camera A under camera
  B's identity with no timeout. This is #2370's severity claim and it holds.
- **B is Offline** (read succeeds, `state: 'Offline'`): `offlineMessage` is set,
  `useWhepSession.ts:152-162` transitions to `offline`, A is torn down and the
  tile says so. Already correct today; FR-003 must not regress it.

### How a reassignment reaches a running wall — narrower than it looks

`CellPage` wires `onArchived`, `onOverlayPublished`, `onOverlayArchived`,
`onOverlayHighlightChanged` and `onReconnected` — **not `onPublished`**, and
there is no `LayoutPublished` handler anywhere under `apps/`. The kiosk's
`useGetLayoutQuery` provides `{ type: 'Layout', id }`, which only management-web
mutations invalidate, in a different app and a different store.

So on a mounted kiosk wall a published revision lands **only** via
`onReconnected: () => { void refetch(); }` (`CellPage.tsx:299-301`) — a hub
reconnect. That is routine on a 24/7 wall (any network blip), so the defect is
reachable in production; but it means a tile does not swap the moment an
operator publishes.

**That is a second defect and this spec does not take it** — a wall that keeps
showing a retired layout until the hub happens to reconnect. It is recorded here
because it shapes the end-to-end procedure below, and it should be filed. See
*§ What this spec does not take*.

---

## User stories

### US1 (P1) — an operator never sees a picture the wall cannot vouch for

**As** a control-room operator watching a fab wall,
**I want** a reassigned tile to show *nothing but a state* until the new camera's
own video arrives,
**so that** every frame on the wall belongs to the camera named beside it.

This is the whole slice. It is independently shippable and independently
observable: reassign a tile, watch it, and it never paints the previous camera.

**Independent end-to-end test procedure** (no other story required):

1. Boot the Aspire stack (`AspireFixture` / `dotnet run` on `AppHost`).
2. In `management-web`, author a 2×2 layout with camera A at cell (0,0) and
   publish it. Open the kiosk wall for that layout; wait for (0,0) to read
   **Live** over moving video.
3. In `management-web`, edit the layout so cell (0,0) carries camera **B**, and
   publish the new revision.
4. Force the kiosk's hub to reconnect (stop and restart the layout hub's
   service, or drop the browser's network for >1 hub retry interval) so
   `onReconnected → refetch()` delivers the new revision to the mounted page.
5. **Observe cell (0,0) continuously from step 4.** It must go from A's video to
   a *state* — a black tile reading **Connecting…** — and from there to B's
   video. At no instant may it show A's picture, moving or frozen, while the
   tile's overlay names B.
6. Repeat with camera B's stream read made to fail (stop
   `stream-distribution`, or point B at a camera with no provisioned stream).
   The tile must settle on an **explicit error**, not on A's picture and not on
   "Connecting…" forever.

**Why this cannot be proved by a remount.** Steps 3-5 must leave the `<video>`
element identity unchanged. A procedure that reloads the kiosk page proves
nothing: the whole defect is that the tile is *not* remounted.

---

## Functional requirements

**FR-001 — the stream read answers for the camera that was asked for.**
`CameraViewer.tsx:100` reads `currentData`, not `data`. A change of
`cameraIdentifier` clears `stream` — and therefore `whepUrl` — immediately, and a
failed read for the new camera never resolves to the previous camera's stream.

**FR-002 — the same shape is closed in `FrameGrabber`.**
`FrameGrabber.tsx:37` reads `currentData`. **This is hardening, not a live
instance**: `FrameGrabber` is mounted for the duration of one capture and
unmounted when it settles (`FrameCapture`), so its `cameraIdentifier` never
changes on a mounted instance and the two spellings are indistinguishable today.
It is in scope because leaving a known instance of the shape live in the same
file family is how the shape reaches six sites. Its colour is **characterisation,
green** — see *§ Phase 4a*.

**FR-003 — a camera change ends the previous camera's session and its picture.**
On `cameraIdentifier` changing on a mounted `useWhepSession` (never on first
mount):

1. the previous camera's WHEP session is closed;
2. `videoRef.current.srcObject` is set to `null`, so **no frame decoded from the
   previous camera survives on the element**;
3. `status` leaves `'live'`, so `CameraViewer` draws its state overlay over the
   now-empty element;
4. the retry ladder's attempt counter is reset, so the new camera does not
   inherit the previous camera's backoff position (see FR-006).

**FR-004 — the teardown coupling is explicit, not accidental.**
`cameraIdentifier` appears in the session effect's dependency array at
`useWhepSession.ts:304`.

*It is behaviourally redundant today and is required anyway.* The teardown
currently rides on `transitionTo`'s `[cameraIdentifier]`, which exists so a log
line can name the camera. This file already holds three collaborators behind refs
for exactly the reason someone would do it to a fourth — `getTokenRef`
(`:130-133`), `onLagMeasuredRef` (`CameraViewer.tsx:235-238`), `accessTokenRef`
(`CellPage.tsx:46-53`) — and the moment `cameraIdentifier` follows them, the
teardown silently stops happening and #2370 returns in the form it was *filed*
as. The dependency makes the coupling a stated intention. T009 pins it.

**FR-005 — a failed stream read reads as an error, never as connecting and never
as the last good stream.**
When the read for the current camera has failed and no stream for that camera has
been received, the tile presents an error state — an error-toned label and the
existing hint — rather than "Connecting…", "Idle", or any picture.

**FR-006 — the new camera starts its own retry ladder.**
`attemptRef` (`useWhepSession.ts:122`) is component-scoped and is reset only by
`confirmMedia` (`:203`) and the no-instrument `connected` path (`:259`). Today a
swap mid-ladder hands camera B the previous camera's attempt count, so B's first
backoff can start at the 15 s cap. FR-003 makes B's connection actually happen,
which makes this reachable. **This is a deliberate small addition to the smallest
change** (one line, in the effect FR-003 already adds) and is flagged as such: a
reviewer may strike it and file it separately without affecting FR-001 or FR-003.

**FR-007 — behaviour outside a camera change is unchanged.**
Not a wish — the thing the characterisation suites assert. Specifically: the
retry ladder and its jitter, the media watchdog and its `mediaBaseline`
(`:223-241`), the disconnect grace window, the Degraded→Healthy re-dial
(`:306-318`), the decode and lag samplers and their `logResilienceEvent`
cadences, `setPlayoutTarget`, and the `offline` path all behave exactly as they
do today when `cameraIdentifier` does not change.

---

## Acceptance scenarios (Gherkin)

### Happy path

```gherkin
Scenario: a reassigned tile shows the new camera and nothing else
  Given a mounted tile is Live on camera A
  When the cameraIdentifier prop changes to camera B, with the same DOM element
  Then the WHEP session to camera A is closed
  And no new WHEP session is opened against camera A's URL
  And the video element carries no media stream
  And the tile is not Live
  When the stream read for camera B answers with camera B's WHEP URL
  Then exactly one WHEP session is opened, against camera B's URL
  And the tile returns to Live only once a frame decoded from camera B arrives
```

### Conflict — the read for the new camera fails

```gherkin
Scenario: a failed read for the new camera never resolves to the old one
  Given a mounted tile is Live on camera A
  When the cameraIdentifier prop changes to camera B
  And every stream read for camera B fails
  Then the tile shows an explicit error
  And the tile is never Live
  And the video element carries no media stream
  And no WHEP session is ever opened against camera A's URL again
  And the five-second poll re-erroring does not restore camera A's stream
```

### Bad request — the new camera is offline

```gherkin
Scenario: an offline new camera still says offline, not connecting
  Given a mounted tile is Live on camera A
  When the cameraIdentifier prop changes to camera B
  And the stream read for camera B answers state Offline with a reason
  Then the tile shows "Stream is offline" with that reason
  And no WHEP session is opened for either camera
```

### Authorisation — the gateway refuses the read for the new camera

```gherkin
Scenario: a 403 on the new camera's stream read does not fall back to the old one
  Given a mounted tile is Live on camera A
  When the cameraIdentifier prop changes to camera B
  And the gateway answers 403 for camera B's stream read
  Then the tile shows an explicit error
  And the video element carries no media stream
  And camera A's video is not shown under camera B's identity
```

> A WHEP **POST** refused with 401/403 is a different refusal, it is **#2355**,
> and it is not taken here. See *§ What this spec does not take*.

### Regression — the shape that is not a defect

```gherkin
Scenario: an unchanged camera keeps its session across an unrelated re-render
  Given a mounted tile is Live on camera A
  When the parent re-renders with a new getToken closure and the same camera
  Then the WHEP session to camera A is not closed
  And the video element still carries its media stream
  And the tile is still Live
```

---

## Success criteria

| # | Criterion | How it is observed |
|---|---|---|
| SC-001 | A camera change never leaves a frame of the previous camera on the element | `videoEl.srcObject === null` through the gap (T005) |
| SC-002 | A camera change never opens a second session against the previous camera's URL | WHEP POST URLs over the swap (T005) — **the assertion that is red today** |
| SC-003 | A failed read for the new camera shows an error, indefinitely and honestly | T006 |
| SC-004 | The tile is not Live at any instant between the two cameras | T005 |
| SC-005 | Every existing `CameraViewer` / `useWhepSession` / `FrameGrabber` assertion passes **unmodified** | T001 + the full `pnpm -r test` |
| SC-006 | Lint stays clean at `--max-warnings 0` | `pnpm lint` |

---

## Latency-budget impact (constitution §IV)

**Legs cited: Overlay composite + render (≤ 50 ms), Presentation buffer /
playout alignment (≤ 200 ms), SFU → kiosk decode (≤ 120 ms).** All three are
`Implemented: yes`. **No §IV cell moves and no measurement is owed before merge.**

The claim has to be earned rather than asserted — spec 142's grounds ("no effect
is added or re-keyed") are *not* available here, because this spec re-keys an
effect on a §IV-path hook. Four grounds, in descending order of weight:

**1. The renegotiation is already being paid.** The session effect already
re-runs on a camera swap, via `transitionTo`'s `[cameraIdentifier]` already being
in its dep array (`useWhepSession.ts:135-144`, `:304`). A swap already closes the
peer connection and already negotiates a new one. This change **redirects** that
negotiation to the camera that was asked for; it does not create one. Whatever a
teardown-and-rebuild costs, the wall pays it today, on exactly the same trigger,
for a session to the wrong camera. That is a property of the code, checkable by
reading two line ranges, and it is the answer to the question this spec was asked
to price.

**2. No steady-state path changes.** Between a tile going Live and its camera
changing, nothing this spec touches executes: no interval, timer, actuator,
render, statistics read or network call is added, removed or re-timed. The decode
sampler, the lag sampler, `measureOverlayDraw` and `setPlayoutTarget` are
untouched. FR-007's characterisation assertions are the guard against that claim,
not this paragraph.

**3. The only added work is on the swap commit, and it is bounded.** One
`srcObject = null` write and one status transition, per tile, once per
reassignment. `GridDimensions.MaxTiles = 4`
(`src/LayoutComposition/Domain/Layout/GridDimensions.cs:18`), so the worst case
is four — **not 250**; the documents that say 250 are #2363 and are wrong. A
`srcObject` write invokes the media-element load algorithm, which `ontrack`
already performs once per session (`WhepClient.ts:85`, `:97`), so the added cost
is bounded above by work this same path already does on every connect and every
reconnect. Against ADR-0123's reading of this leg — a figure whose floor is set
by display cadence, 37.0–74.1 ms at 27 Hz, and which is *read as cadence first,
compositing second* — this is not a measurable quantity.

**4. What a teardown *does* cost, stated rather than waved past.** A WHEP session
is a negotiation: `createOffer` → `setLocalDescription` → ICE gathering →
`POST` offer → `setRemoteDescription` → `ontrack` → first frame. Its budget is
**not** one of §IV's six legs; it is spec 002 FR-013's *click → first decoded
frame ≤ 3 s p95*, the same 3 s that `MEDIA_WATCHDOG_MS` encodes
(`useWhepSession.ts:58`) and that `e2e/click-to-first-frame.spec.ts` asserts in
CI at `P95_BUDGET_MS = 3000`. A teardown also resets the tile's
`jitterBufferTarget`, so the wall's alignment controller must re-converge that
tile — the presentation-buffer leg's *value*, not its budget. **Both are true
today**, on the same trigger, for the same count of tiles; ground 1 applies to
them unchanged.

**Nothing rate-limits a swap, and nothing needs to.** The trigger is an operator
publishing a revision — a human action — delivered to a mounted wall only on a
hub reconnect (see above). Four tiles is fewer than the four a wall negotiates on
every page load, and `jitteredRetryDelay`'s ±20% spread applies to *retries*, not
to first connects, so four simultaneous first-connects are no more staggered than
they are today at page load. The existing `click-to-first-frame` budget is the
guard, and it already runs on every PR.

**No *before* figure is cited, deliberately.** §IV records the presentation
buffer as *recorded, not yet observed* and #1714 is open precisely because nobody
has read that number off a running wall. Inventing a baseline would be the
clerical failure this series exists to make visible.

### The §VII condition this spec cannot discharge, raised rather than resolved

ADR-0117 §1 reads: *"A leg whose code path exists MUST have a latency measurement
and a dashboard showing it against its ADR-015 budget **before further work ships
on that leg**."* §IV's table records the Dashboard column as **`no` for all five
implemented legs**, and §IV itself says every leg is now subject.

Read literally, that blocks this PR — and it equally blocked every spec since
045 that touched this path, none of which stopped. **#1940 is open on exactly the
unresolved question** (whether the column is satisfied by a figure being readable
in the sink, or demands a purpose-built view), and §IV states in terms that spec
045 "does not resolve it and must not be read as having done so".

This spec does not resolve it either, does not claim an exemption, and does not
invent one. It is recorded here so the condition is a visible claim rather than
an unnoticed absence, which is what §IV asks of exactly this situation. **The
lane may not write the ADR that would settle it** (ADR-0144), so it is flagged
for the human reviewer at the phase-2 gate.

---

## Locked technical choices consumed

| Concern | Choice | ADR |
|---|---|---|
| Frontend state / data fetching | Redux Toolkit + RTK Query — `data` vs `currentData` is the defect | 0075 |
| Two apps, one shared composite | `apps/shared/src/ui/composites` consumed by kiosk and management | 0074 |
| Real-time push | SignalR layout hub; unchanged | 0076 |
| Composite + render leg | the operator's wait, cadence-floored | 0123 |
| Playout alignment | `jitterBufferTarget`; a teardown resets it | 0128 |
| Test framework | vitest + jsdom + Testing Library; no store helper in `apps/shared` | 0052 |
| Parallelism marking | `[P]` = disjoint files | 0109 |

**No new architecture, no new ADR needed for the fix itself.** Two ADR-class
questions are raised and not answered — the ESLint rule below and #1940 above.

---

## The ESLint rule — this site is its precondition, and it is not built here

An ESLint rule banning `data:` destructuring from a query hook with a
non-constant argument **is adopted and ships after the sites are fixed.** This
file is one of those sites, and FR-001/FR-002 are a precondition for the rule
landing clean.

**The rule is not added in this spec**, for two reasons that both hold: the sites
must be clean first or the rule lands red, and a new enforced rule is ADR-class
under ADR-0036's enforce-rules-advise-preferences split, which ADR-0144 forbids
the lane deciding.

**Which sites remain between this spec and the rule — and none of them is
tracked.** #2370's body names five: `CameraViewer.tsx:100` and
`FrameGrabber.tsx:37` (this spec), `CellPage.tsx:435,469` and
`CameraDetailPage.tsx:42` (not this spec, see below). It claims *six* genuine
hazards; if the sixth is `LayoutEditorDialog.tsx:65`, that one is already fixed
(#2368 / PR #2373). **No open issue owns the remaining three.** #2378 does not —
it is the accessibility issue about focusable controls inside conditionally
rendered live regions, sixteen sites, none of them in
`apps/shared/src/ui/composites`. See *§ Contradictions*.

---

## What this spec does not take

| Not taken | Where it belongs | Why not here |
|---|---|---|
| `CellPage.tsx:435,469` — the previous overlay's label over the new tile | needs an issue; **#2378 is not it** | Disjoint file, different app, same trap. It is a real instance and leaving it untracked is the risk this table exists to name. |
| `CameraDetailPage.tsx:42` | same follow-up issue | Latent — management-web has no camera→camera navigation, so the argument cannot change on a mounted instance. |
| **#2355** — a WHEP 401/403 retried forever | #2355, `Todo`, no `agent:ready` | Adjacent, not overlapping: its edits are `useWhepSession.ts:290-292`, `scheduleRetry` (`:184-195`), the `'error'` status member (`:8`) and `CameraViewer`'s render. FR-006 writes `attemptRef` in a *new* effect and touches none of those lines. **Logical adjacency is real**: if #2355 lands a terminal refusal state, a camera swap must clear it — whichever lands second owns that. |
| The ESLint rule | its own ADR + spec | ADR-class; lane-forbidden. |
| The kiosk not refetching its layout on `LayoutPublished` | a new issue | Found here, genuinely separate: a wall that keeps a retired layout until the hub reconnects is a different defect with a different fix (wire `onPublished` → invalidate `{ type: 'Layout', id }`). Folding it in would double this spec and make the red test's shape ambiguous. |
| Keying tiles on the camera instead of the position | — | Explicitly rejected. `CameraViewer.tsx:113-126` and spec 095 depend on position keying; remounting per camera would multiply engine-level resilience lines by every camera that passes through a slot overnight. |
| Freezing the last frame with a badge; blacking the tile | — | Rejected in the brief. The first still paints A under B's name with a badge as the only thing preventing a misread; the second costs a visible gap on every layout change. The chosen shape shows the connecting state, which is neither. |

---

## Contradictions with the issue text, listed

Five. The first changes the work; the rest change the record.

1. **Link 3's mechanism is wrong** (*§ The finding*). The session effect *does*
   re-run on a camera change. Consequence: fix part 2 is redundant-today rather
   than load-bearing, fix part 3 is load-bearing in a way the issue does not
   state, and the renegotiation cost the brief asks to price is already being
   paid.
2. **"No timeout on the wrongness" is exact for the failed-read case and
   generous for the success case.** On a successful read the window is one
   gateway round-trip, during which a whole negotiation to A is begun and
   abandoned. The indefinite case — and the severity — is the failed read, which
   the issue itself identifies.
3. **`FrameGrabber.tsx:37` is the same shape but not a live instance.** The
   component is mounted per capture and never re-propped, so the two spellings
   are indistinguishable today. In scope as hardening; its test is
   characterisation, not red. The brief's "same defect" overstates it by one
   step.
4. **`CellPage.tsx:435,469` are not "#2378's census".** #2378 is *"Fourteen more
   places put a focusable control inside a conditionally-rendered live region"* —
   the accessibility twin of #2372. It names sixteen a11y sites, none in
   `apps/shared/src/ui/composites`, and does not mention #2370 or RTK `data:`.
   The RTK census lives in **#2370's own body**, so excluding those two sites
   here leaves them owned by nothing. **An issue should be filed.**
5. **#2355 is factually adrift in two places** (found while checking the
   interaction, not acted on): it says there is no `'error'` status at all — the
   union at `:8` has one, it is simply never produced; and it cites
   `FrameGrabber.tsx:84` as a dead `status === 'error'` branch, which no longer
   exists. Worth a comment on #2355 before someone implements from its text.

---

## Phase 4a colour

**RED**, and one task is deliberately the other colour.

- **FR-001, FR-003, FR-004, FR-005, FR-006 — RED.** Behaviour-changing. The
  discriminating test must be observed failing, with the output quoted in the PR
  (ADR-0139).
- **FR-002 (`FrameGrabber`) and T001 (the mock widening) — characterisation,
  observed GREEN.** Both are behaviour-preserving on today's code. Their covering
  tests are captured passing before the change and must pass **unmodified**
  after; an assertion that has to be edited is evidence behaviour moved, and is a
  block rather than an adjustment.

This is §Testing's two obligations applied to one spec, not an exemption from
either. The ambiguity-resolves-to-red rule is not engaged: FR-002 is not
ambiguous, it is demonstrably a no-op (the argument cannot change on a mounted
`FrameGrabber`).

**The discriminating test, and why the existing suite cannot be it.** Every
`useGetStreamQuery` mock in the repo returns `{ data, isLoading, error }` and
nothing else — eight files, listed in `tasks.md` T001. A mocked hook answers the
same value regardless of its argument, so **no mocked suite can express the
distinction between `data` and `currentData`**, and none of them would go red
against this defect. That is the same structural blindness PR #2369 found at
phase 6 on the overlay twin, and the reason its regression test drives the
**real** hook against a stubbed gateway
(`apps/management-web/src/features/overlays/OverlayEditorDialogChainRetention.test.tsx`).
This spec mirrors that shape. Details in `tasks.md` T005.

---

## Assumptions and decisions, marked

No `[NEEDS CLARIFICATION]` remains. Three judgements were made rather than
guessed, and each can be overturned at the gate:

1. **The gap reads "Connecting…", not a new "Switching camera…" state.** Reusing
   the existing status costs no new vocabulary, no new translation surface and no
   new branch in `labelFor`. A dedicated state would be speculative generality
   for a distinction an operator does not need to make.
2. **FR-006 (`attemptRef` reset) is included.** One line, inside the effect
   FR-003 already adds, and FR-003 is what makes it reachable. Flagged so a
   reviewer can strike it.
3. **The discriminating test lives in `apps/shared`, with a store built by the
   test.** `apps/shared` has no store today and `CameraViewer` must not start
   assuming one (**#2374**, open). Providing a store is the *caller's* job, and a
   test is a caller — so the test constructs one locally and the composite
   learns nothing new about Redux.
