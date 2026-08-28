# Feature Specification: A wall shows one instant

**Feature Branch**: `045-wall-shows-one-instant`

**Created**: 2026-08-28

**Status**: Draft

**Issue**: 1714 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: User description: "The presentation buffer leg — PTP-coordinated
playout so a wall shows one instant. Issue 1714's last remaining item: the only
leg of the 800 ms budget still recorded as unbuilt."

---

## Why this exists

Constitution §IV breaks the 800 ms budget into six legs. Five are built. One is
not, and it is **200 ms — a quarter of the whole budget**:

| Leg | Budget | Implemented |
|---|---|---|
| Presentation buffer (PTP) | ≤ 200 ms | **no** |

It is also the only leg still exempt from §VII by *not yet built* rather than by
discharge. Spec 040 found two legs wrongly recorded as unbuilt and corrected
them. This one is genuinely unbuilt, and it is the last.

### What an operator sees today

The kiosk renders a wall: `CellPage` lays the published revision's tiles out as a
CSS grid and gives each populated cell its own `CameraViewer`. Every tile opens
its **own** streaming session and therefore has **its own jitter buffer**, which
decides on its own when to start playing and how far behind live to sit.

Nothing coordinates them. Two tiles watching the same machine from two angles can
be showing moments that are tens or hundreds of milliseconds apart, and **nothing
on the screen says so**. An operator comparing two tiles is comparing two
different instants without being told.

That is the complaint. It is not cosmetic in a fab: a wall exists so that a
person can look across it and reason about one moment in one process.

### The second thing this unblocks

The decode leg is stuck at **"in part"** in §IV, and its own instrument says
exactly why:

> a browser cannot see the sending end without a clock shared with the SFU —
> establishing one **is** the presentation-buffer leg, which is not built.

So `receive_to_decoded` is recorded under a name that deliberately does not claim
the leg, with no budget attached. Building this leg is what allows that leg to be
measured whole. Two rows of §IV move on one feature.

---

## What the locked decision says, and where it does not meet the code

ADR-014 (**Locked**) specifies this leg:

> Video-wall sync = frame-accurate via PTP (IEEE 1588) + coordinated playout.
> StreamKeeper emits presentation timestamps; kiosks buffer ~100–200 ms and
> present at T. Inter-display skew target < 5 ms. Deployment prerequisite:
> PTP-aware switches in the fab OT network.

ADR-0021 (**Locked**) depends on it: *"Overlay engine renders at the frame whose
presentation timestamp matches `used_ts`."*

**Three facts in the built system contradict it.** They are stated here rather
than designed around, because §IV and the ADRs cannot be quietly reinterpreted by
an implementation.

**1. There is no StreamKeeper in the media path.** The SFU is MediaMTX — an
unmodified third-party container (`bluenviron/mediamtx:latest-ffmpeg`), given a
config file and nothing else. Our `StreamDistribution` context provisions paths
and authorizes WHEP sessions; **it never touches a frame**. "StreamKeeper emits
presentation timestamps" names a component that does not exist in that role, and
nothing we own is positioned to stamp a frame.

**2. No browser has a PTP-aware time API.** Constitution §Frontend states target
browsers as *"evergreen Chromium-based; WebRTC and PTP-aware time APIs
required"*. **No such API is exposed to a web page in any shipping browser.** The
requirement as written cannot be satisfied by any browser choice.

**3. There is no PTP hardware here, and ADR-014 says there must be.** A
grandmaster and PTP-aware switches in the fab OT network are ADR-014's own stated
*deployment prerequisite*. Everything in this repository has been verified on a
single developer workstation. Any claim this feature makes about
**inter-display** skew would be unverifiable here.

### Consequence: ADR-014 cannot be implemented as written

This is the finding, and the spec states it rather than absorbing it. **An ADR
amendment is a prerequisite of Phase 2**, not an afterthought — Phase 2 must not
pick a mechanism that silently redesigns a Locked decision, and §IV cannot change
without one (governance).

What the amendment has to settle is *which parts of ADR-014 survive contact with
a third-party SFU and a browser with no PTP clock*, and what replaces the parts
that do not. This spec deliberately does not answer that; it bounds it.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Tiles on one wall show one instant (Priority: P1)

An operator looks at a kiosk wall of several tiles. What each tile shows is
coordinated: the tiles are presenting the same moment as one another, within a
stated bound, rather than each drifting to wherever its own buffer settled.

**Why this priority**: It is the complaint, and it is the part that is
**achievable without PTP hardware**. Every tile on one wall is decoded by one
browser on one machine, and every tile is served by the **same SFU**. One
machine, one serving clock — a shared reference exists here without a
grandmaster. This is the whole of what can be honestly built and verified today.

**Independent Test**: Show one source in two tiles of one wall — the same camera,
or two cameras pointed at one visibly changing thing. Skew is observable directly
by a person and measurable by the instrument in US2. Delivers a wall an operator
can reason across, with no other story implemented.

**Acceptance Scenarios**:

1. **Given** a published wall with several tiles all live, **When** an operator
   watches a change that is visible in more than one tile, **Then** that change
   appears in those tiles within the stated skew bound.
2. **Given** a wall whose tiles are coordinated, **When** one tile's session
   drops and reconnects, **Then** it rejoins the wall's common instant rather
   than settling wherever its fresh buffer lands.
3. **Given** a single-tile (N=1) wall, **When** it is opened, **Then** it behaves
   as it does today — coordination among one tile is not a reason to add delay.
4. **Given** a wall being coordinated, **When** the end-to-end path is measured,
   **Then** it still meets the 800 ms budget: **this leg spends from a budget it
   is a leg of.**

---

### User Story 2 — The leg has a number, and a person can read it (Priority: P2)

The time this leg contributes, and the skew it achieves, are measured and reach
observability. §IV's row stops saying "no", and says something true instead.

**Why this priority**: Constitution §VII makes a dashboard mandatory for every
**implemented** leg — so the moment US1 ships, this leg acquires an obligation it
did not have while unbuilt. Building the capability without the number would move
the row from *honestly unbuilt* to *built and unmeasured*, which is strictly
worse: it is the exact clerical drift §IV's own warning sentence is about.

**Independent Test**: Run a wall, then read the leg's figure and the achieved
skew for that wall in the telemetry sink, per tile, without attaching a debugger
to a kiosk.

**Acceptance Scenarios**:

1. **Given** a wall presenting coordinated tiles, **When** an engineer looks in
   the sink, **Then** the delay this leg added and the skew achieved are both
   readable, attributed per tile.
2. **Given** the leg is measured, **When** §IV is read, **Then** its
   Implemented / Measured / Dashboard columns match what the code does — with the
   same honesty as the existing "in part" and "recorded, not yet readable"
   entries, rather than rounding up.
3. **Given** the decode leg previously stood at "in part" for want of a shared
   clock, **When** this feature ships, **Then** that row is revisited explicitly
   — either measured whole, or restated with the reason it still is not.

---

### User Story 3 — A wall that cannot hold the instant says so (Priority: P3)

When tiles cannot be coordinated within the bound — a source too far behind, a
tile that never converges, a delay that would breach the leg's budget — the wall
makes that visible instead of looking correct.

**Why this priority**: This is the failure mode that costs the most and shows the
least. **A wall that is silently misaligned looks exactly like a wall that is
aligned**, and an operator has no way to tell. Worse, the fix for misalignment is
*adding delay*, so a naive implementation buys alignment by making every tile
late — spending a budget it is a leg of, invisibly. Both failures are silent by
construction, which is why they need saying out loud.

**Independent Test**: Force a tile out of alignment (a source with a large,
unrecoverable lag) and confirm the wall reports it rather than displaying a
confidently wrong composite.

**Acceptance Scenarios**:

1. **Given** a tile that cannot be brought within the bound, **When** the wall
   renders, **Then** that condition is visible to the operator and recorded for
   an engineer.
2. **Given** coordination would require delay beyond this leg's budget, **When**
   that is detected, **Then** the budget is not silently exceeded.
3. **Given** the coordination machinery fails outright, **When** the wall
   renders, **Then** **video keeps playing** — an observer that can break the
   thing it observes is worse than no observer (spec 040 FR-011's precedent).

---

### Edge Cases

- **A tile joins late.** An operator opens a wall and one camera takes longer to
  connect. Does the wall wait, re-converge, or let the newcomer catch up?
- **A tile reconnects.** Covered by US1 sc.2, and it is the common case: sessions
  drop on a fab network.
- **The source has no usable timing.** A camera or path that gives nothing to
  align against — coordination must degrade, not fail the tile.
- **A clock steps.** Fab clocks are PTP-stepped, and this repo has already been
  bitten: `CellPage` uses `performance.now()` rather than `Date.now()` precisely
  because a step can pin a highlight on forever. Any coordination that reasons
  about wall-clock time inherits that hazard.
- **The slowest tile sets the pace.** Aligning to the laggiest tile makes the
  whole wall as late as its worst member. One bad camera then degrades seven good
  ones — and does so invisibly. *Resolved by FR-012a: the wall waits only as far
  as the 200 ms budget, then releases and marks the outlier.* The boundary case
  worth testing is a tile sitting **just either side** of that cap, where the
  wall must not oscillate between holding and releasing.
- **A backgrounded or throttled tab.** Timers are throttled and frames are not
  painted; measurements taken across that gap describe the throttle, not the leg.
- **N=1.** A single-tile wall has nothing to coordinate with. It must not pay for
  the feature.
- **Tiles showing genuinely different moments.** Two cameras whose *capture*
  clocks differ cannot be aligned by anything downstream of them. This bounds
  what "one instant" can mean — see Assumptions.

---

## Requirements *(mandatory)*

### Functional Requirements

**Coordination (US1)**

- **FR-001**: Tiles rendered on one wall MUST present against a common time
  reference rather than each tile's independent buffer.
- **FR-002**: The system MUST hold inter-tile skew within **one frame interval —
  33 ms**, and that bound MUST be written down where a reader of the budget can
  find it.

  **Why one frame, and why 33 ms.** Tiles on a wall are painted by one
  compositor in one frame, so **two tiles cannot visibly differ by less than a
  frame interval**: a tighter bound would describe something an operator can
  never see and a browser can never demonstrate. 33 ms is one frame at 30 Hz,
  which is the cadence floor ADR-0123 already requires of a kiosk — so the bound
  is not a new number, it is the existing one read for this leg.

  **This is deliberately not ADR-014's `< 5 ms`.** That figure is an
  *inter-display* target for PTP-synced hardware and is out of scope here (see
  Out of Scope). Adopting it for intra-wall would import a target set for
  different hardware and make the leg pass or fail on measurement noise.
- **FR-003**: A tile that leaves and rejoins a wall MUST converge back to the
  wall's common instant.
- **FR-004**: A single-tile wall MUST behave as it does today, adding no delay it
  did not previously have.

**Budget (US1, US3)**

- **FR-005**: The delay this leg adds MUST be bounded by its ≤ 200 ms budget, and
  MUST NOT be exceeded silently.
- **FR-006**: The end-to-end path MUST still meet the 800 ms SLO with this leg
  active. **This leg spends from the budget it is a leg of** — a wall that is
  aligned but late has traded one breach for another.

**Measurement and visibility (US2)**

- **FR-007**: The delay contributed by this leg MUST be measured per tile, not
  assumed from a configured value. A configured buffer depth is an intention; the
  leg's figure is what actually happened.
- **FR-008**: The achieved inter-tile skew MUST be measured, not merely targeted.
- **FR-009**: Both figures MUST reach observability **through a service**, in
  keeping with ADR-0122 — the browser reports an elapsed figure it has already
  computed and never a start, and no telemetry SDK ships in the kiosk bundle.
- **FR-010**: Constitution §IV's leg table MUST be updated to state this leg's
  true Implemented / Measured / Dashboard status, using the same
  no-rounding-up honesty as its existing "in part" and "recorded, not yet
  readable" entries.
- **FR-011**: The **decode** leg's "in part" status MUST be revisited in the same
  change — either raised to measured-whole now that a shared reference exists, or
  restated with the reason it still cannot be.

**Failure is visible (US3)**

- **FR-012**: A tile that cannot be brought within the skew bound MUST be visible
  to the operator and recorded for an engineer.
- **FR-012a**: When one tile lags, the wall MUST wait for it **only as far as
  this leg's 200 ms budget allows**. Within that, the wall holds together and
  stays one instant. Beyond it, the wall MUST release the outlier, keep showing
  it, and mark it as out of alignment (FR-012).

  **The budget is the arbiter, not the worst tile.** Holding unconditionally
  would let one bad camera make every other tile late and push the leg past
  200 ms — the silent regression US3 exists to catch, and a breach of FR-005 and
  FR-006. Releasing unconditionally would let the wall stop being one instant in
  the ordinary case of a tile that is merely a little behind, which is the
  complaint this feature started from. The cap is what makes both refusals
  automatic rather than a judgement call at runtime.
- **FR-012b**: A released tile MUST keep playing. The wall gives up the *claim*
  about that tile, never the picture — an operator does not lose a camera because
  the system could not synchronise it.
- **FR-013**: Failure of the coordination machinery MUST NOT stop video. A wall
  that loses alignment keeps showing pictures.

**Governance**

- **FR-014**: ADR-014 MUST be amended before a mechanism is chosen, because it
  cannot be implemented as written: no StreamKeeper is in the media path, no
  browser exposes a PTP time API, and the PTP hardware it names as a deployment
  prerequisite is absent. The amendment MUST state which of its claims survive
  and what replaces those that do not — including the fate of its **inter-display
  < 5 ms** target and of constitution §Frontend's "PTP-aware time APIs required".
- **FR-015**: The spec MUST state, and the verification note MUST record, which
  claims were demonstrated on available hardware and which were **not verifiable
  here**. A leg that is "built" but unverifiable must say so rather than be
  recorded as done. *(Precedent: spec 044, which was explicit that its central
  claim needed a person.)*

### Key Entities

- **Wall**: the set of tiles a kiosk renders together from one published layout
  revision. The unit across which "one instant" is claimed.
- **Tile**: one camera's live view within a wall. The unit that is coordinated,
  measured and — when it cannot be — flagged.
- **Common time reference**: whatever the tiles of one wall are aligned against.
  What this can be is bounded by Assumptions and settled by FR-014's amendment,
  not by this spec.
- **Presentation buffer**: the delay deliberately added before showing a frame in
  order to hold alignment. The thing this leg's 200 ms budget is spent on.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a wall showing one visibly changing subject in two or more
  tiles, an operator watching the wall cannot identify which tile is ahead.
- **SC-002**: Measured inter-tile skew on a live wall stays within **33 ms** for
  every held tile over a sustained observation, not merely at start-up.
- **SC-002a**: A tile that would need more than 200 ms of waiting is released and
  marked rather than held — demonstrated by inducing one, and confirming the
  other tiles' latency does not rise with it.
- **SC-003**: The end-to-end event-to-overlay path still meets 800 ms with
  coordination active, demonstrated by measurement rather than by argument.
- **SC-004**: The delay this leg contributes stays within 200 ms, and a run in
  which it would not is reported rather than silently absorbed.
- **SC-005**: An engineer can read this leg's figure and the achieved skew for a
  running wall from the telemetry sink, per tile, without attaching a debugger to
  a kiosk.
- **SC-006**: A single-tile wall's time-to-first-frame and end-to-end latency are
  unchanged from before this feature, within measurement noise.
- **SC-007**: With coordination deliberately broken, every tile still shows live
  video.
- **SC-008**: Constitution §IV contains no row whose Implemented column
  contradicts the code — checked for all six legs, not only this one.

---

## Out of Scope

- **Inter-display synchronisation across separate kiosks.** ADR-014's < 5 ms
  inter-display target needs a clock shared between machines, which is PTP, which
  needs the grandmaster and PTP-aware switches ADR-014 itself names as a
  deployment prerequisite. **None of that hardware exists here**, so a claim about
  it could be neither built honestly nor verified. It stays ADR-014's target and
  becomes a later feature, on a fab network. This spec's "one instant" is
  **within one wall on one kiosk**.
- **Aligning to true capture time across cameras.** See Assumptions.
- **Choosing the mechanism.** No decision here between receiver playout hints,
  transport-level timing, or anything else, and no buffer depth. That is Phase 2,
  and it is gated on FR-014.
- **Overlay alignment to `used_ts`** (ADR-0021's dependent claim). Rendering an
  overlay at the frame matching its event's timestamp is a further feature that
  this leg is a prerequisite for, not part of it.

---

## Assumptions

- **"One instant" means aligned against a reference the system actually has, not
  true wall-clock capture time.** Cameras are independent devices with their own
  capture clocks and no synchronisation between them; nothing downstream can
  recover a moment that was never recorded. A wall can be made self-consistent —
  which is what an operator comparing tiles needs — without any claim that tile A
  and tile B captured at the same absolute microsecond.
- **All tiles on one wall are served by one SFU instance**, which is what makes a
  shared reference available without PTP. A future multi-SFU deployment
  (ADR-012's shard-by-camera) would reopen this and is out of scope.
- **The kiosk is one browser on one machine**, so tiles on a wall already share a
  compositor and a monotonic clock. This is the asset US1 spends and the reason
  US1 is buildable while inter-display is not.
- **Verification is on a developer workstation** running the whole Aspire stack,
  with simulated cameras. Spec 044 made the simulated wall show visibly different
  clips, which is what makes cross-tile comparison possible at all — that
  feature is a practical dependency of demonstrating this one.
- **The existing kiosk measurement path is reused** (ADR-0122): the browser
  computes an elapsed figure and reports it to a service, which records it
  against the latency meter. No new observability sink (ADR-0118 stands).
- **`performance.now()` semantics apply throughout.** Any coordination reasoning
  about time inherits the PTP-step hazard `CellPage` already documents.

---

## Dependencies

- **Constitution §IV** (leg table) and **§VII** (dashboard obligation) — this
  feature changes what §IV says and triggers what §VII requires.
- **ADR-014** — must be amended first (FR-014).
- **ADR-0021** — depends on this leg; not delivered here.
- **ADR-0117** (an unbuilt leg is not yet subject) — the exemption this feature
  ends for this leg.
- **ADR-0118 / ADR-0122** — one sink per environment; browser measurements enter
  through a service.
- **Spec 040** — built the kiosk measurement path this reuses, and is the
  precedent for a leg's record being wrong.
- **Spec 044** — the visibly different simulated clips that make a skew
  observable by a person.
- **Issue 1941** — the render leg's cadence question. Related, not blocking: both
  concern what a display can actually deliver.
