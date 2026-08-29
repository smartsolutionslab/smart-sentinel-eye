# Feature Specification: The overlay and the picture it annotates

**Feature Branch**: `046-overlay-matches-its-frame`

**Created**: 2026-08-29

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
layered over the video, not composited into the frame. It updates the instant
its value changes; the picture underneath it is older. An operator reads a number
describing *now* over a frame from slightly before.

### What it costs today, measured

On a real two-tile wall (spec 045 verification §3a):

| | reading |
|---|---|
| Frame age (buffer + processing) | **~25–40 ms** |
| Of which the presentation-buffer leg | **10–26 ms** |

**~30 ms is below what an eye resolves.** Nobody has complained, and nobody
would notice. That is the honest starting position of this feature and the spec
does not pretend otherwise.

### Why it is worth settling now anyway

**Spec 045 made the gap grow rather than shrink.** Aligning a wall works by
*adding buffer*, so the better the tiles agree with each other, the further the
whole wall sits behind the label describing it. On this hardware that is a few
milliseconds. On a fab network needing real buffer it is however much buffer it
needs — the mechanism has no ceiling short of the leg's 200 ms budget.

So the direction is set by a feature that just shipped, and the SLO makes a
claim the system does not honour. One of those has to change.

---

## What is written down, and why none of it can be built

**ADR-0021 (Locked)**: *"Overlay engine renders at the frame whose presentation
timestamp matches `used_ts`."*

**Both halves are missing, and neither is a small gap.**

**1. There are no presentation timestamps.** ADR-0128 withdrew that claim: no
component of ours is in the media path — the SFU is an unmodified third-party
MediaMTX — so nothing can stamp a frame. That ADR recorded ADR-0021 as *"left
unsettled and is not settled here … needs its own amendment when a feature
attempts it."* **This is that feature.**

**2. `used_ts` never reaches the kiosk.** The realtime message carrying a
resolved overlay value is `{ overlay, resolvedText, version }` — **no timestamp
of any kind**. The kiosk cannot know *when* the event happened, only that the
text changed. So even with frame timestamps, there would be nothing to match
them against.

**So frame accuracy is not one change away.** It needs an instant on the
realtime contract *and* an instant per frame — and the obvious source of the
second is a component in the media path, which ADR-0128 rejected as out of all
proportion. That rejection stands (FR-017).

### The route that might avoid the rejected one

Browsers hand a video element per-frame metadata, including an RTP timestamp
and — where the sender forwards the absolute-capture-time RTP header extension —
the instant the frame was captured. **If that extension survives camera → SFU →
browser, frames arrive already carrying what this feature needs, and nothing of
ours goes near the media path.**

Whether it survives is unknown and is the first question Phase 2 must answer.
Spec 045 learned this the expensive way: the statistic its design depended on
turned out not to exist in Chromium, and only a probe against a real MediaMTX
found that out. **The same probe discipline applies here before any task list
is written.**

### What spec 045 left behind that helps

The kiosk now measures, per tile, how old its picture is — jitter-buffer delay
plus decode processing, as a live delta. **It did not know that before.** Under
this scope it is not the mechanism, but it is the cross-check: an independently
derived frame age that any frame-instant claim should agree with, and disagreeing
with it is a signal the instrument is wrong.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — An operator can trust the label against the picture (Priority: P1)

An operator reads a value on a tile and acts on it. **The value describes the
moment shown in the picture beneath it** — not the moment it happened to arrive.
The SLO's promise of frame-synchronisation becomes true rather than being
withdrawn.

**Why this priority**: It is the whole of the issue, and the scope chosen
(FR-012) is the only one of the three that makes the existing promise honest
instead of rewriting it.

**Independent Test**: Cause a value to change at a known instant, and confirm it
appears against the frame representing that instant rather than against whatever
frame was on screen when the message arrived.

**Acceptance Scenarios**:

1. **Given** a value that changes at a known instant, **When** it is displayed,
   **Then** the frame beneath it represents that instant within the stated
   tolerance.
2. **Given** a wall whose tiles are aligned (spec 045), **When** alignment adds
   buffer and the picture falls further behind, **Then** the label moves with it
   and the pairing is unchanged — **the property spec 045 currently erodes**.
3. **Given** two tiles with different frame ages, **When** both carry the same
   value, **Then** each shows it against its own matching frame rather than at a
   shared moment.

---

### User Story 2 — The gap is a number, not an argument (Priority: P2)

The pairing is measured on a running wall
rather than argued for, so a later change that breaks it is visible.

**Why this priority**: This feature exists *because* spec 045 quietly changed
the gap's direction and only a code reading noticed. An unmeasured gap will drift
again, and the next feature to add buffer will not know it did so.

**Independent Test**: Read the gap for a tile on a running wall and compare it
against the same tile's frame age.

**Acceptance Scenarios**:

1. **Given** a live tile, **When** the gap is measured, **Then** a figure exists
   attributed to that tile.
2. **Given** a change that adds playout buffer, **When** the wall runs, **Then**
   the recorded gap reflects it.

---

### Edge Cases

- **A tile with no overlay.** Nothing to match; the feature must not delay or
  alter a tile that carries no label.
- **A released tile** (spec 045 FR-012a). Its picture is deliberately *not*
  aligned with the wall, so its matching frame differs from its neighbours'. That
  is consistent under this scope — each tile matches its own frame — but it means
  two tiles can legitimately show different values at the same wall-clock moment,
  and an operator should not read that as a fault.
- **The matching frame has already gone past.** A value arriving later than the
  frame it describes cannot be shown against it. This is the common case, not an
  exotic one, and FR-016 exists for it.
- **A value that changes faster than frames arrive.** Two updates can fall inside
  one frame interval; the operator must not see them out of order, and must not
  miss one entirely.
- **A safety-relevant value.** Matching makes the operator see an alarm *later*
  than arrival. For an alarm, freshest-wins is plausibly correct and matching is
  actively wrong — so a blanket "everything matches" may be the wrong rule.
- **A tile whose frame instants cannot be read.** The extension is absent, the
  metadata is unavailable, or the session restarted; the feature must degrade
  visibly rather than guess an offset.
- **Clocks that step.** Fab clocks are PTP-stepped, and any comparison of an
  event instant to a frame instant spans two clocks. A step can put one before
  the other; spec 045 hit the same hazard and used monotonic time for exactly
  this reason.

---

## Requirements *(mandatory)*

### Governance — the gate

- **FR-001**: ADR-0021 MUST be amended before any mechanism is built. It cannot
  be implemented as written: there are no presentation timestamps (ADR-0128) and
  `used_ts` does not reach the kiosk. The amendment MUST record the scope chosen
  (FR-012) and why the two alternatives — amending the wording, and age-matching
  — were declined.
- **FR-002**: The SLO's **"frame-synced"** wording **survives, but only once it
  is true.** This scope earns the phrase rather than removing it, which is its
  central justification — and until FR-015 holds on a running wall, the wording
  is an overclaim exactly as it is today.

  So §IV's leg record MUST NOT report this as delivered ahead of evidence. **The
  precedent is spec 045**, which recorded its leg as *"recorded, not yet
  observed"* rather than *measured*, and pinned that with a test so it could not
  be tidied up. Constitution §IV cannot change without an ADR.

### The claim the system makes (US1)

- **FR-003**: The system MUST NOT state a synchronisation guarantee it does not
  deliver, in the constitution, in an ADR, or in the UI.
- **FR-004**: The relationship between a label and the frame beneath it MUST be
  written down in one place a reader will find, including the direction in which
  playout buffering moves it.

### Measurement (US2)

- **FR-005**: The overlay-to-frame gap MUST be measurable on a running wall,
  attributed per tile.
- **FR-006**: The measurement MUST reach observability by the existing
  report-through-a-service path — no new sink, no telemetry SDK in the kiosk
  bundle.
- **FR-007**: Measurement MUST NOT itself delay the overlay or the video.

### Bounds on any mechanism, should one be built

- **FR-008**: Any delay applied to an overlay MUST be bounded and MUST be counted
  against the same 800 ms budget the video legs spend from. A label held back to
  match a stale frame is a *later* label, and lateness is what the budget bounds.
- **FR-009**: Overlay updates MUST NOT be reordered or dropped by any delay.
- **FR-010**: A tile whose frame age cannot be read MUST fall back to showing the
  label immediately, never to a guessed delay.
- **FR-011**: Failure of the mechanism MUST NOT stop video or stop overlays.
  *(Spec 040's rule, and spec 045's: an observer that can break what it observes
  is worse than no observer.)*

### Scope

- **FR-012**: This feature is scoped to **full frame accuracy**: an overlay value
  is shown against the frame that was captured at the moment the value describes.
  Not age-matching, not a wording fix — the SLO's *frame-synced* claim is to be
  **made true** rather than withdrawn.

  Two things must therefore exist that do not exist today, and both are
  requirements of this feature rather than assumptions:

- **FR-013**: The instant a value describes (`used_ts`, per ADR-0021) MUST reach
  the kiosk. The realtime message carrying a resolved overlay value currently
  carries no timestamp of any kind, so this is a contract change.
- **FR-014**: The kiosk MUST be able to establish, for a displayed frame, the
  instant it represents — on a common reference with FR-013's timestamp.
- **FR-015**: A value MUST be shown against the frame representing its instant,
  within a stated tolerance, rather than at the moment the value arrives.
- **FR-016**: Where a frame carrying the matching instant is not available —
  it has already been displayed, or never arrives — the behaviour MUST be
  defined and visible, not silently approximate.

### The constraint this scope runs into

**FR-014 is the whole difficulty, and ADR-0128 has already ruled on the obvious
route.** It rejected owning the media path — forking MediaMTX or writing an SFU
— as *"out of all proportion"*, and nothing about this feature changes that
judgement.

- **FR-017**: This feature MUST NOT put a component of ours into the media path.
  If frame instants cannot be obtained without that, the scope is not
  achievable as specified and MUST be brought back for a decision rather than
  quietly downgraded to age-matching. **Age-matching wearing the name "frame
  accuracy" is the outcome to prevent**, because it would restate the very
  overclaim this feature exists to remove.

**There is a candidate that does not touch the media path**, and confirming or
killing it is the first thing Phase 2 must do: browsers expose per-frame
metadata to a video element, including an RTP timestamp and — where the sender
forwards the absolute-capture-time RTP header extension — a capture instant.
If that extension survives the path from camera to SFU to browser, frames arrive
already carrying what FR-014 needs and nothing of ours is in the media path.

**If it does not survive, this feature is blocked on FR-017**, and that is a
finding to raise rather than engineer around.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A value that changes at a known instant is displayed against a
  frame representing that instant, within a stated tolerance, on a running wall.
  **This is the feature; everything else supports it.**
- **SC-002**: Adding playout buffer to a tile does **not** change the pairing —
  the picture falls further behind and the label falls with it. Demonstrated by
  inducing buffer, not by argument. *(Spec 045's lesson: the spread looking small
  is not evidence when the thing under test is a relationship.)*
- **SC-003**: The pairing for a live tile can be read as a figure by someone who
  did not write the feature.
- **SC-004**: The end-to-end path still meets 800 ms, with any waiting this
  feature introduces counted inside it rather than beside it.
- **SC-005**: A tile carrying no overlay is unchanged — no added waiting, no
  changed timing.
- **SC-006**: Where a matching frame is unavailable, the displayed behaviour is
  the one FR-016 defines, and it is distinguishable from a successful match
  rather than looking identical to one.
- **SC-007**: No document states a synchronisation guarantee that measured
  behaviour contradicts — checked against §IV, ADR-0015 and ADR-0021 together,
  not one at a time, and **not marked delivered before SC-001 is observed**.

---

## What cannot be demonstrated, and saying so up front

**A person cannot see this unaided.** ~30 ms is below the threshold at which an
eye distinguishes a label from the frame under it, so there is no "look at the
wall and confirm" step — unlike spec 044 (visibly different clips) and spec 045
(a visibly misaligned wall), where a person was the final authority.

The routine evidence is therefore **instrumental**: the instant a value
describes, the instant the frame beneath it represents, and the difference.

**One real demonstration is possible, and it needs equipment this project does
not have.** Point a high-speed camera at a screen showing a scene with a known
external event — 240 fps resolves ~4 ms, comfortably inside the gap — and read
the label and the picture off the same captured frame. That is the only way a
human confirms frame accuracy rather than trusting the instrument that claims
it. **Whether it happens is a resourcing decision**, and the spec records it as
available-but-unfunded rather than pretending the instruments are sufficient or
that a person can squint at a wall.

**This matters more under the chosen scope, not less.** Age-matching would have
been imperceptible *and* obviously approximate. Frame accuracy is imperceptible
*and claims exactness* — which is precisely the combination where an instrument
that quietly measures the wrong thing goes unchallenged. Spec 045 shipped two
such instruments and both were caught by live video rather than by tests.

---

## Out of Scope

- **Amending the record only**, and **age-matching**. Both were considered and
  rejected in favour of making the claim true (FR-012). They remain the fallbacks
  if FR-017 blocks the chosen scope, and the ADR must record them as the
  alternatives it declined rather than pretending there was one option.
- **Compositing overlays into the video** rather than layering them over it. A
  rendering change with its own performance and accessibility consequences, and
  **not required by frame accuracy** — a label layered over the right frame is
  as frame-accurate as one drawn into it.
- **Changing how or when a value is computed** upstream. This feature concerns
  the instant a value is *displayed against*, never what the value is.
- **Inter-display synchronisation.** Out of scope in spec 045 and unchanged.
- **The kiosk's overlay content, geometry or highlight behaviour.** This feature
  concerns *when* a label is shown, never *what* it says.

---

## Assumptions

- **The gap is a systematic offset, not jitter.** The label leads the picture by
  roughly the tile's frame age, consistently, because both are driven by the same
  buffering. If it turned out to be noisy rather than systematic, age-matching
  would be a worse idea and the spec would need revisiting.
- **Consistency is worth more than freshness here.** Showing a value against the
  frame it describes was chosen over showing it as early as possible. An operator
  watching a fab
  might prefer the freshest number, and that reading was considered;
  it is a product judgement and it was made the other way. The alarm case in the
  edge cases is where it may need revisiting.
- **The existing per-tile frame-age measurement is reusable** (spec 045). It is
  already computed, already a delta rather than a lifetime average, and already
  returns nothing rather than zero when it cannot be read.
- **The existing kiosk measurement path is reused** — the browser computes an
  elapsed figure and reports it to a service (ADR-0122); ADR-0118's one-sink rule
  stands.
- **No safety-critical alarm currently depends on overlay timing at this
  resolution.** If one did, FR-008's trade would need settling before anything is
  delayed.

---

## Dependencies

- **ADR-0021** — must be amended first (FR-001).
- **ADR-0128** — withdrew presentation timestamps and recorded ADR-0021 as
  unsettled; this feature is the amendment it named.
- **ADR-0015 / constitution §IV** — the SLO wording this feature corrects.
- **Spec 045** — supplies the per-tile frame-age measurement, and is the reason
  the gap now grows rather than shrinks.
- **ADR-0122 / ADR-0118** — how a browser measurement reaches observability.
