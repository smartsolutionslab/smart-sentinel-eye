# ADR-0178: The audit's residue is closed by ruling, not left to drift

**Status:** **Proposed** (awaiting the architecture lead's approval; see *Implementation Notes*: the record edits are a separate, second step)
**Date:** 2026-10-21
**Amends:** `docs/adr/0000-initial-decisions.md` rows 005, 017 and 022; constitution §IX (the camera-adapter row's wording only), §Stack (the *Camera protocols* bullet) and §Availability (one sentence)
**Relates to:** ADR-0130, ADR-0131, ADR-0133, ADR-0134, ADR-0144, spec 047, issues 1969, 1970, 1971, 1973, 1975, 1976, 1987, 1988, 2225, 1974

## Context

ADR-0130 audited the founding decisions against the code and recorded the unbuilt claims,
each with an owner issue. It deliberately did **not** decide what to do
about them: *"None is legitimised by silence"* (ADR-0130 §Decision 3). Seven weeks
later six of those owner issues are still open, and most have been blocked for the
same reason: **every available resolution is an edit to a locked record**, which
the autonomous lane may not make (ADR-0144) and which no individual feature owns.

| Issue | What the record promises | What exists | Why it is stuck |
|---|---|---|---|
| 1971 | Six variable types (decision 017) | `String`, `Number`, `Boolean` (verified: `VariableType.cs`) | Building the other two is a design with no consumer; dropping them is a record edit |
| 1973 | RTSP **and ONVIF Profile S/T "on day one"**, plus a vendor-adapter seam (decision 005) | RTSP only; no ONVIF code | Building discovery raises two security questions nobody has ruled on; dropping the claim is a record edit |
| 1975 | Camera Catalog edits are *"staged config changes applied together"* (decision 022) | Camera edits are live and audited, like the contexts decision 022 says edit live | The staged workflow has no meaning for a camera (see below); narrowing it is a record edit |
| 1970 | `IRuleEngine` and `IAuthorizationDecisionPoint` "in v1" (§IX) | Neither exists | Building the first turns `FoundingDecisionRecordTests` red until the §IX row changes, and the lane may not change it |
| 1976 | Kiosks reboot unattended; the title said they could not | Restart recovery shipped (specs 051, 052; ADR-0133, ADR-0134) | The issue's title is stale; the §Availability bullet still opens with a sentence ADR-0134 overtook |
| 1969 | The umbrella for all of the above | n/a | Has nothing of its own to do once the children are ruled on |

### Why a ruling, and why now

ADR-0130 §Context explains why a wrong record is not merely untidy: **the record decides
whether obligations exist.** The converse holds too. A promise that is neither built
nor withdrawn keeps a locked row asserting something that is not true, and the next
feature to rely on it finds out the way specs 040, 045 and 046 did. Closing these by
ruling is the same maintenance ADR-0130 performed, on the part it left standing.

### What this ADR does **not** do

Two of the six issues contain decisions that are **real architecture**, not record-keeping, and they stay out:

- **#1973's security questions.** Camera discovery would put a probe on the OT camera VLAN
  (constitution §Security: cameras are reached only by the SFU; decision 013) and a probe has
  no fab. Whether and how that is permitted is its own ADR, written when discovery is scheduled.
- **#1976's device-runtime question.** A credential a browser cannot hold needs a device
  runtime. That belongs to #1987 and #1988, which were ruled on separately on 2026-10-21 (no
  device runtime for now; the enrolment endpoint is kept but gated) and need their own ADR.

Likewise **#2225** (how `IAuthorizationDecisionPoint` relates to `IFabAuthorizationGuard`) and **#1974**
(SFU sharding, which conflicts with ADR-0128's one-sender-clock-per-wall) are separate decisions with
separate ADRs.

## Decision

**1. Variable types (#1971, decision 017): v1 is `String`, `Number`, `Boolean`.**

- **`json` is dropped.** A variable's value is already carried as text that the client
  evaluates (decision 017 itself says JSON variables were *"stored and forwarded as opaque
  strings"*), so a `Json` type would be a `String` with a label and would add a type the
  overlay expressions cannot distinguish. The use it was reserved for is served by `String`.
- **`datetime` is deferred, not dropped.** It has no format or time-zone model, and a datetime
  variable changes how overlay expressions evaluate on the event-to-overlay path. It returns
  only through a spec that **owes a latency figure for that leg** (constitution §IV, §VII) and
  states the format and zone it commits to.
- The merge of `integer` and `decimal` into `Number` was already legitimised by ADR-0130 and
  is unchanged.

**2. Camera protocols (#1973, decision 005): v1 is RTSP; ONVIF is deferred until a customer needs discovery.**

- The "on day one" claim is withdrawn. Cameras are registered by URL, which is what operators do today.
- **The vendor-adapter seam is *not* decided here.** Decision 005's "adapter pattern … for
  vendor-specific drivers" and §IX's *Camera adapter* strategy-interface row remain **an unbuilt
  obligation** on #1973. Withdrawing ONVIF from v1 does not discharge §IX's rule that the seam
  exists *in v1* so that vendor SDKs can land without breaking changes; whether the seam is built
  now or the §IX obligation is itself narrowed is a separate ruling, because it is the
  same kind of decision as #1970's and should not be settled as a side effect of this one.
- The two security questions above are **explicitly not answered.** When discovery is
  scheduled, an ADR answers (a) who may touch the camera network and (b) how a probe is
  attributed to a fab, *before* a spec is written.
- The name *StreamKeeper* in decision 005 is not corrected here; ADR-0130 already records that
  the name appears nowhere in the code.

**3. Camera Catalog edits (#1975, decision 022): atomic, not staged.**

Decision 022's clause *"Camera Catalog (staged config changes applied together)"* is narrowed
to: **a camera edit is one atomic command guarded by one `If-Match`, covering every editable
field, with the audit trail every other live-edit context has.** There is no draft, preview or
publish for a camera. The reasons are properties of the thing edited, recorded by the spec 111
finding (branch `feat/1975-camera-edits-staged-together`, commit `06d4800d`): a camera's `fab`
is immutable, a camera renders and evaluates nothing (so a preview has no meaning and a
"dry-run" has nothing to run), and the one-field edit limit is a recorded deferral, not a design.
What operators actually need from "applied together" is that a multi-field change cannot
half-apply, which atomicity gives them. Overlays, Layouts and Automation rules keep the full
draft → validate → preview/dry-run → publish pipeline, unchanged.

**4. `IRuleEngine` (#1970): the obligation stands and is to be built; the authorization seam is separated.**

- §IX's rule-engine row is **not** withdrawn. The strategy interface is built (spec 110, branch
  `refactor/1970-the-seams-section-ix-mandates`, phases 1–3 complete at `e6cfcab5`), and **the
  implementing PR updates the §IX row in the same change** (`FoundingDecisionRecordTests` goes
  red until it does; that is the guard doing its job, and *"this guard pins the record against
  drift, never against progress"*). The row text to use is in *Implementation Notes*.
- `IAuthorizationDecisionPoint` is **taken out of #1970** and decided in #2225, which has its own
  owner ruling (wrap the existing guard now, shaped to absorb it later) and its own ADR to write
  first. This ADR does not pre-empt it.

**5. Unattended restart (#1976): the browser-only posture stands; one sentence of the record is corrected.**

- Restart recovery, the identity-outage retry and the session-ceiling fix shipped (ADR-0131, ADR-0133,
  ADR-0134). The §Availability bullet's opening claim, *"Not met, and the attempt to meet it was
  withdrawn before merge"*, describes spec 050 and was overtaken by specs 051 and 052, whose
  paragraphs follow it in the same bullet. The opening is corrected (text in *Implementation Notes*).
- **The target remains undischarged for the reason the bullet already gives:** twenty screens have
  never been exercised (four is the most, once) and a real power cut has never been tested. That
  is evidence nobody has gathered, not a missing mechanism, and no ruling here changes it.
- Issue 1976 is retitled to what remains buildable: *an e2e check that a wall display comes back
  from a genuine process death* (no ADR needed).
- Device-bound credentials stay with #1987 and #1988, decided and recorded separately.

**6. The umbrella (#1969) stays open as a tracker** until #1970, #1971, #1973, #1974, #1975 and
#1976 are resolved, and carries no lane work. This ADR does not close it.

## Consequences

**Positive: several locked rows stop asserting things that are not true,** and each stuck issue gets a
buildable remainder or a close: #1971 closes, #1975 becomes one small command (a
`ChangeCameraConfigurationCommand` with a single `If-Match`), #1970 resumes, #1976 is
retitled, #1973 is narrowed to its two unanswered questions.

**Positive: the autonomous lane can work again.** Most of the six were blocked only because their
resolution was a record edit.

**Negative: ONVIF's absence is now a decision, not an oversight.** A customer who needs camera discovery
will find it deferred and will need the two security ADRs first. Recording it plainly is better than a
locked row promising it "on day one", but it is a real narrowing of what v1 offers.

**Negative: `datetime` is promised back, with a precondition.** A deferred type is a debt on the record;
the latency-figure precondition is what stops it returning as a cheap feature on a hot path.

**Negative: §IX's camera-adapter obligation is left open on purpose.** Leaving it unbuilt while ONVIF
is withdrawn is honest but untidy, and it will need its own ruling.

**Neutral: this is still a snapshot.** Like ADR-0130, it is true on its date. The guard keeps the
corrected rows honest; the other founding claims are not re-audited here.

## Alternatives Considered

**One ADR per issue: REJECTED.** All of items 1–5 are the same kind of ruling ADR-0130 made and
share one set of record edits and one guard; six ADRs would repeat that context six times and
make the later consistency check harder. The two items that are real architecture (#1973's security
questions, #1976's device runtime) *are* kept out for exactly this reason.

**Build everything the rows promise: REJECTED.** `datetime` has no consumer, `json` has no meaning
distinct from `String`, ONVIF discovery has two unanswered security questions, and a staged camera
workflow has nothing to preview. Building them to match a stale record would be speculative
generality (ADR-0036) in the one place the project most often over-builds.

**Leave the rows as "unbuilt" indefinitely: REJECTED.** That is what has been true since 2026-08-29
and it has blocked the lane for weeks. ADR-0130's own method (*record what is true, annotate, never
overwrite*) supports closing the question rather than carrying it.

**Drop #1975's clause entirely (no combined-edit command): REJECTED by the owner (2026-10-21).**
Atomic multi-field edits are a real operator need that the narrowed clause keeps at the cost of
one small command; dropping the clause would also have dropped that.

**Also drop the camera-adapter seam together with ONVIF: REJECTED here, not decided.** §IX's
obligation is about where v2 can land without breaking changes, not about ONVIF; it deserves
its own ruling (see Decision 2).

**Amend the constitution in this ADR's own PR: REJECTED.** Governance requires an approved ADR
first (constitution §Governance, *Amendments*, item 1), and one of the edits (the §IX rule-engine row)
is correct only on the day the interface lands. See *Implementation Notes*.

## Implementation Notes

**This ADR is proposed as a document only.** Nothing in `0000-initial-decisions.md` or the
constitution is edited by it. After the architecture lead approves it, a second PR applies the record
edits below, bumps the constitution to **1.11.0** and adds an amendment-history line. The exact
proposed text:

1. **`0000-initial-decisions.md`, annotate (never overwrite) rows 005, 017 and 022** in the pattern
   of rows 014 and 026: prefix *"**Amended by ADR-0178.**"* and keep the original text after
   *"Originally:"*. Row 017 **must keep the two sentinel phrases `` `datetime` does not exist `` and
   `` `json` does not exist ``** that `FoundingDecisionRecordTests.The_variable_type_row_agrees_with_the_types_that_exist`
   matches on, adding after them: *"`json` is dropped (ADR-0178); `datetime` is deferred until a spec
   states its format and zone and carries a latency figure."* With that wording **no test change is needed**.
2. **§Stack, *Camera protocols* bullet:** replace *"RTSP. ONVIF Profile S/T was decided "on day one" and
   is absent (ADR-005, ADR-0130, issue 1973)."* with *"RTSP. ONVIF is deferred until a customer needs
   discovery (ADR-005, ADR-0178); its two security questions (who may touch the camera network, how a
   probe is attributed to a fab) are unanswered and need their own ADR first."*
3. **§IX, *Camera adapter* row:** change *"RTSP only — ONVIF absent (ADR-005, ADR-0130)"* to *"RTSP only —
   ONVIF deferred (ADR-005, ADR-0178)"*. The strategy-interface column stays **Absent (issue 1973)**.
4. **§Availability, first sentence of the unattended-restart bullet:** replace *"**Not met, and the
   attempt to meet it was withdrawn before merge.**"* with *"**Not discharged: the mechanism exists** (specs
   051 and 052; ADR-0133, ADR-0134), **and the target's own terms have never been exercised.**"* The
   paragraphs that follow it already say the rest.
5. **§IX, *Rule engine* row, on the day `IRuleEngine` lands:** *"**`IRuleEngine`** (spec 110, ADR-0178)"* in the
   interface column, in the **same PR** as the interface, bumping the version then. Until then the row
   stays *absent* and the guard stays green.
6. **Row 022:** *"Camera Catalog: one atomic edit command guarded by one `If-Match`, with the audit trail of the
   live-edit contexts; no draft, preview or publish (ADR-0178)."*

**Guard consequences (`tests/Architecture.Tests/FoundingDecisionRecordTests.cs`):** with edit 1 as written, no
fact changes. Edits 2–4 and 6 touch prose that no current fact asserts (grep for *"issue 1973"* finds none).
Row 005's and row 022's amended text is covered by `Every_amended_row_still_shows_what_was_originally_decided`
(each amended row must keep its original wording), which these annotations satisfy.

**Issues, after approval:** #1971 closes with a link to this ADR; #1975 is rewritten as the
`ChangeCameraConfigurationCommand` task (behaviour-changing, red first); #1973 is narrowed to the adapter
seam ruling plus the two security questions, both not `agent:ready`; #1970 resumes on its branch and is
scoped to `IRuleEngine`; #1976 is already retitled; #1969 gets a comment linking this ADR and stays open.

**What was not verified:** the evidence for decision 017's *"opaque"* wording is the row's own text and #1971's
analysis, not a fresh read of the overlay evaluation code; the claim that a camera "renders and evaluates
nothing" is the spec 111 finding's, on a branch not merged to `develop`. The approver should check both.
