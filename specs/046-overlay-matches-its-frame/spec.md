# Feature Specification: The overlay and the picture it annotates

**Feature Branch**: `046-overlay-matches-its-frame`

**Created**: 2026-08-29 · **Re-scoped**: 2026-08-29, after the Phase-0 probe

**Status**: Draft

**Issue**: 1967 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: The SLO promises the overlay is *frame-synced* to the video. It is
not, and spec 045 made that cost something measurable.

---

## Why this exists

Constitution §IV states the end-to-end SLO as:

> external event arrival → overlay rendered, **frame-synced** ≤ 800 ms

**Nothing is frame-synced.** The overlay is an absolutely-positioned DOM label
layered over the video, not composited into the frame. It updates the instant its
value changes; the picture underneath it is older. An operator reads a number
describing *now* over a frame from slightly before.

### What it costs today, measured

On a real two-tile wall (spec 045 verification §3a):

| | reading |
|---|---|
| Frame age (buffer + processing) | **~25–40 ms** |
| Of which the presentation-buffer leg | **10–26 ms** |

**~30 ms is below what an eye resolves.** Nobody has complained, and nobody
would notice. That is the honest starting position and the spec does not pretend
otherwise.

### Why it is worth settling now

**Spec 045 made the gap grow rather than shrink.** Aligning a wall works by
*adding buffer*, so the better the tiles agree with each other, the further the
whole wall sits behind the label describing it. Here that is a few milliseconds;
on a fab network needing real buffer it is however much buffer it needs, with no
ceiling short of the leg's 200 ms budget.

So the direction is set by a feature that just shipped, and the SLO makes a claim
the system does not honour.

---

## What was tried first, and why it is not this

This feature was originally scoped to **full frame accuracy** — pair each value
with the frame whose instant it describes, making the SLO's promise true rather
than correcting it. **[research.md](./research.md) established that it cannot be
built here.** Three blockers, and they get worse:

1. **The capture-time RTP header extension does not survive.** Chromium does not
   offer it, MediaMTX does not offer it, and **forcing it into the offer does not
   work** — the SFU declines to echo it and per-frame metadata still carries no
   capture instant. Obtaining one means an SFU we own, which ADR-0128 rejected.
2. **The browser cannot relate its clocks.** Per-frame metadata gives an RTP
   timestamp — *which* frame — while every wall-clock field is local to the
   machine. The RTCP repair is unavailable: `getStats` exposes the sender
   report's NTP instant with no accompanying RTP timestamp.
3. **And this one survives fixing both.** A capture instant would be on the
   *camera's* clock; a value's instant is on a *server's*. Relating them is PTP
   across the OT network — ADR-014's deployment prerequisite, absent here.
   ADR-0128 already said it: *nothing downstream can recover a moment that was
   never recorded.*

**So this is not a missing API. There is no shared clock between the thing that
saw the event and the thing that filmed it.** Frame accuracy needs hardware, not
software, and this feature does not pretend otherwise.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The system stops claiming what it does not do (Priority: P1)

Someone reading the constitution, ADR-0021 or the code learns what the
relationship between a label and its picture actually is, and finds no promise of
synchronisation that nothing delivers.

**Why this priority**: It is shippable alone, it is the part that is certainly
correct, and it is the failure that started this — a written guarantee nobody
had checked against behaviour. **It does not depend on User Story 2.**

**Independent Test**: Read what the system promises and compare with measured
behaviour. Today they disagree; afterwards they must not.

**Acceptance Scenarios**:

1. **Given** §IV, ADR-0015 and ADR-0021 read together, **When** compared with
   the measured gap, **Then** no statement claims a synchronisation the system
   does not perform.
2. **Given** a reader wanting frame accuracy later, **When** they consult the
   record, **Then** they find why it is not done and what it would require,
   rather than re-running the probe.

---

### User Story 2 — A label is as old as the picture under it (Priority: P2)

An operator comparing a label with the scene beneath it is comparing two things
that describe roughly the same moment, instead of a current number over an older
picture — and that stays true as walls buffer more.

**Why this priority**: It removes the systematic offset that spec 045 widens. It
is second because it is the part that costs latency and cannot be demonstrated
to a person, so it must not hold up User Story 1.

**Independent Test**: Induce playout buffer on a tile and confirm its label is
held back correspondingly, rather than continuing to appear immediately.

**Acceptance Scenarios**:

1. **Given** a tile with a measured frame age, **When** its label changes,
   **Then** the label appears about that long after the change rather than at
   once.
2. **Given** buffer is added to a tile (spec 045 alignment), **When** the wall
   runs, **Then** the label's delay follows the tile's new frame age.
3. **Given** two tiles with different frame ages, **When** both carry the same
   value, **Then** each holds its label by its own age, not a shared one.

---

### Edge Cases

- **A tile with no overlay.** Nothing to hold back; it must be untouched.
- **A tile whose frame age cannot be read.** Statistics unavailable, or the
  session restarted — show the label immediately rather than guess.
- **A value that changes faster than the delay.** Two updates can fall inside one
  delay window; the operator must not see them out of order and must not miss
  one. Spec 045's overlay path already carries a monotonic version guard.
- **A safety-relevant value.** Holding a label back makes an alarm arrive
  *later*. For an alarm, freshest-wins is plausibly correct — so "hold
  everything" may be the wrong rule and the spec does not assume it is right.
- **A released tile** (spec 045 FR-012a). Its picture is deliberately not aligned
  with the wall, so its own age is the right delay for it — which means two tiles
  can legitimately show a value at different moments.
- **Clocks that step.** Fab clocks are PTP-stepped; any delay must be scheduled
  on monotonic time, as spec 045's highlight timers already are.

---

## Requirements *(mandatory)*

### Governance — the gate

- **FR-001**: ADR-0021 MUST be amended before any mechanism is built. It cannot
  be implemented as written: there are no presentation timestamps (ADR-0128) and
  `used_ts` never reaches the kiosk. The amendment MUST record the scope chosen
  and why full frame accuracy was abandoned, citing the probe.
- **FR-002**: The SLO's **"frame-synced"** wording MUST be corrected. Constitution
  §IV cannot change without an ADR.

### Part 1 — correct the record (US1)

- **FR-003**: The system MUST NOT state a synchronisation guarantee it does not
  deliver — in the constitution, an ADR, the code, or the UI.
- **FR-004**: The corrected record MUST state what the system does, and MUST NOT
  imply a value is paired with a particular frame.
- **FR-005**: The record MUST name the three blockers to frame accuracy in one
  place, so the next feature that wants it does not re-run the probe.
- **FR-006**: Part 1 MUST be shippable without Part 2.

### Part 2 — age-match the label (US2)

- **FR-007**: A label's delay MUST be derived from **that tile's own measured
  frame age**, never a configured constant. Tiles differ, and a constant is an
  assumption wearing a measurement's clothes.
- **FR-008**: Age-matching MUST NOT be called frame accuracy, frame
  synchronisation or frame matching — in code, documents, UI or metric names.
  **It makes a label as old as the picture; it does not pair a value with a
  frame.** Restating the overclaim in a new form is the outcome this feature
  exists to prevent.
- **FR-009**: The delay MUST be bounded, and the bound stated where the latency
  budget is stated. A held label is a later label.
- **FR-010**: The delay MUST be counted against the 800 ms budget, not treated as
  outside it.
- **FR-011**: A tile with no readable frame age MUST show its label immediately.
- **FR-012**: Delayed labels MUST NOT be reordered or dropped.
- **FR-013**: A tile carrying no overlay MUST be unaffected.
- **FR-014**: Failure of the mechanism MUST NOT stop video and MUST NOT stop
  overlays — it falls back to showing labels immediately. *(Spec 040's rule and
  spec 045's: an observer that can break what it observes is worse than none.)*

### Measurement

- **FR-015**: The delay actually applied MUST be measurable per tile, and MUST be
  the achieved figure rather than the intended one. *(Spec 045 shipped a setpoint
  that could not be read back; the same trap applies here.)*
- **FR-016**: Measurement MUST reach observability through the existing
  report-through-a-service path — no new sink, no telemetry SDK in the kiosk
  bundle.

### Boundaries

- **FR-017**: No component of ours goes into the media path. ADR-0128's rejection
  stands and this feature does not reopen it.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No document states a synchronisation guarantee that measured
  behaviour contradicts — checked across §IV, ADR-0015 and ADR-0021 together.
- **SC-002**: A reader wanting frame accuracy finds, in one place, why it is not
  done and what it would need.
- **SC-003**: With a tile's frame age known, its label appears about that long
  after the value changes — measured, not asserted.
- **SC-004**: Inducing playout buffer on a tile changes its label delay
  correspondingly. **Induced, never observed passively**: a small difference
  between label delay and frame age proves nothing if neither was moved.
- **SC-005**: The end-to-end path still meets 800 ms with the delay counted
  inside it.
- **SC-006**: A tile carrying no overlay is unchanged — no added delay.
- **SC-007**: With the mechanism disabled or broken, labels still appear and
  video still plays.

---

## What cannot be demonstrated, and saying so up front

**A person cannot see this.** ~30 ms is below the threshold at which an eye
distinguishes a label from the frame under it, so there is no "look at the wall
and confirm" step — unlike spec 044 (visibly different clips) and spec 045 (a
visibly misaligned wall), where a person was the final authority.

The evidence is **instrumental**: the label's applied delay, the tile's frame
age, and the difference. Any claim that an operator *perceives* an improvement
would be unfounded, and this spec makes none.

**This is why Part 1 is P1 and Part 2 is P2.** Part 1's benefit is certain and
verifiable by reading. Part 2's benefit is real but imperceptible, and it is paid
for in latency on the same budget — so it earns its place on correctness, not on
anything a user will notice.

---

## Out of Scope

- **Full frame accuracy.** Established unbuildable here (research.md). It needs
  PTP across the OT network *and* an SFU we own. If that hardware ever exists,
  this becomes a different feature.
- **Compositing overlays into the video** rather than layering them over it.
- **Changing what a value is or when it is computed.** This concerns only *when a
  label is shown*.
- **Inter-display synchronisation.** Out of scope in spec 045 and unchanged.

---

## Assumptions

- **The offset is systematic, not jitter.** The label leads the picture by
  roughly the tile's frame age, consistently, because both are driven by the same
  buffering. If it proved noisy rather than systematic, delaying by a measured
  age would be the wrong mechanism and this would need revisiting.
- **Consistency is worth a little lateness — except possibly for alarms.** Held
  labels arrive later. The edge cases flag the alarm case as where this trade may
  need to be made the other way; the spec does not settle it.
- **Spec 045's per-tile frame age is reusable.** Already computed, already a
  delta rather than a lifetime average, already returns nothing rather than zero
  when unreadable.
- **The existing kiosk measurement path is reused** (ADR-0122); ADR-0118's
  one-sink rule stands.

---

## Dependencies

- **ADR-0021** — must be amended first (FR-001).
- **ADR-0128** — withdrew presentation timestamps, recorded ADR-0021 as
  unsettled, and rejected media-path ownership. This feature is that amendment
  and preserves that rejection.
- **ADR-0015 / constitution §IV** — the SLO wording corrected here.
- **Spec 045** — supplies the per-tile frame age, and is why the gap grows.
- **ADR-0122 / ADR-0118** — how a browser measurement reaches observability.
