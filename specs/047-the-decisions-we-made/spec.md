# Feature Specification: The decisions we made, against the system we built

**Feature Branch**: `047-the-decisions-we-made`

**Created**: 2026-08-29

**Status**: Draft

**Issue**: 1969 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**Input**: Three features in a row discovered that a founding decision did not
describe the system. Each found it only when trying to build on it.

---

## Why this exists

`docs/adr/0000-initial-decisions.md` holds decisions **001–027**, and the
constitution calls them *"Locked"*. They are the oldest and most-cited documents
in the repo — and **at least nine of the twenty-seven do not describe the system
that exists**.

This was not found by an audit. It was found three times, by accident, by
features that tried to build on a decision and could not:

| Spec | What it found |
|---|---|
| 040 | Two latency legs recorded as unbuilt while their code ran on every kiosk |
| 045 | **ADR-014** unbuildable: it assigns work to a component that does not exist |
| 046 | **ADR-021** unbuildable: it needs two things that were never built |

**Three coincidences is a pattern.** Nobody has ever checked these rows against
the code, and each feature that trips over one pays for the discovery.

### Why a wrong record is not merely untidy

**The record decides whether obligations exist.** ADR-0117 established that
§VII's dashboard rule binds *implemented* legs — *"a leg not yet built is not
yet subject"*. So a row that wrongly says "unbuilt" **silently exempts working
code from a constitutional rule**. §IV says this in its own warning sentence,
and spec 040 is the instance it now points at.

The same mechanism applies to §IX, which mandates strategy interfaces *"in v1"*.
If the record is wrong there, a requirement quietly does not apply.

---

## What a reconnaissance already found

Not the audit — a spot-check of the most falsifiable claims, done in minutes.

### Claims contradicted by code that exists

- **019 — the expression language.** The row names **CEL (Common Expression
  Language) via a .NET implementation**. What is built is **AEL**: a hand-written
  lexer, parser and interpreter (`AelLexer`, `AelParser`, `AelInterpreter`).
  Not a gap — a different thing, working, under another name.
- **008 — kiosk authentication.** The row says *device-bound credential → OIDC
  `client_credentials` → short-lived token*. The kiosk client is a **public
  client using the authorization-code flow**, with no service account and direct
  grants disabled.
- **015 — the SLO's "frame-synced" clause.** Nothing is frame-synced; spec 046 is
  correcting it separately.

### Components the decisions assign work to, which do not exist

- **005, 012, 013 — StreamKeeper.** A vendor-adapter host, a shard coordinator
  with Raft/etcd-class consistency, and the dual-NIC bridge between the OT and IT
  VLANs. **The name appears nowhere in the code.** A subset of that work is done
  by an unmodified MediaMTX plus the `StreamDistribution` context.
- **020 — `IRuleEngine`.** Mandated by §IX as a v1 strategy interface. Absent.
- **023 — `IAuthorizationDecisionPoint`.** Mandated by §IX, and named in the
  house rules as the seam a policy engine plugs into for v2. Absent.

### Constitution §IX, where three of four rows are stale

Its **observability sink** row still reads *"Both Aspire + Grafana"* — a state
**ADR-0118 abandoned**. §VII was updated by that ADR; §IX was not. So an accepted
decision and a constitutional principle currently disagree.

### And some rows are simply right

**016** names nine bounded contexts and there are exactly nine. **024**, **027**,
**007**, **010** hold. This is a correction, not a demolition, and the spec is
careful about the difference.

### Two that are defensible rather than wrong

- **009 — Marten** *"only where a context's invariants justify it"*. Nothing uses
  it, and *"not yet justified anywhere"* is a fair reading. **But CLAUDE.md's
  stack table states it more strongly than the decision does**, which is its own
  small defect.
- **011 — GPU transcode** *"only when forced"*. Never forced, never built.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A reader can trust the founding decisions (Priority: P1)

Someone consulting ADR-0000 or §IX — to build on a decision, or to check whether
a rule applies — finds statements that describe the system as it is, or that say
plainly they do not.

**Why this priority**: It is the whole feature, and it is what the three
accidental discoveries each needed and did not have.

**Independent Test**: Take any row, and check its claim against the code. Today
several fail. Afterwards none does.

**Acceptance Scenarios**:

1. **Given** any of decisions 001–027, **When** its claims are checked against
   the repository, **Then** either they hold, or the row says which do not and
   why.
2. **Given** §IX's table, **When** compared with the accepted ADRs, **Then** no
   row contradicts one — in particular not the observability row that ADR-0118
   already settled.
3. **Given** a decision naming a component, **When** a reader looks for it,
   **Then** they find it, or the row says it does not exist.

---

### User Story 2 — Unbuilt reads as unbuilt (Priority: P2)

A decision describing something not yet built is distinguishable, at a glance,
from one describing something that runs today.

**Why this priority**: This is the mechanism by which a wrong record does harm.
§VII binds implemented things; "unbuilt" is load-bearing, not descriptive. A row
in the present tense about something absent is how a rule silently stops
applying.

**Independent Test**: Read the record and sort every decision into built /
partly built / not built without consulting the code. Then check against the
code.

**Acceptance Scenarios**:

1. **Given** a decision not yet realised, **When** read, **Then** its status is
   apparent from the row rather than inferred from tense.
2. **Given** a decision realised differently than written, **When** read, **Then**
   the divergence is stated rather than left for a reader to discover.

---

### User Story 3 — It cannot drift back (Priority: P3)

Corrections stay corrected, and a later change that re-introduces a false claim
fails rather than merges.

**Why this priority**: The corrections are prose, and prose rots. This repo
already has the mechanism — `LatencyLegRecordTests` guards §IV's leg table, and
**it caught spec 045 changing that table**. Without an equivalent, this audit is
a snapshot that starts decaying the day it lands.

**Independent Test**: Re-introduce a corrected claim and confirm the build fails.

**Acceptance Scenarios**:

1. **Given** a corrected claim, **When** someone restores the old wording,
   **Then** a test fails naming what is wrong.
2. **Given** a decision whose component is absent, **When** that component is
   later built, **Then** the guard does not obstruct updating the row to say so.

---

### Edge Cases

- **A decision about the future.** *"v2 cloud control plane will federate…"* is an
  intention, not a claim about now, and must not be marked wrong.
- **A decision about deployment, not code.** The OT/IT VLAN split (013) cannot be
  checked in this repository at all. "Unverifiable here" is a third answer and
  must be available.
- **A decision realised under another name.** StreamKeeper's work is partly done
  by MediaMTX and `StreamDistribution`. Is that *not built*, or *built
  differently*? The audit must answer per row, not by rule.
- **A partly-true row.** 025 promises Aspire-generated Helm charts; `deploy/helm`
  holds one chart for Mosquitto. Neither "true" nor "false" fits.
- **A divergence that might be right.** AEL instead of CEL may be the better
  choice. Recording it is not the same as endorsing it.

---

## Requirements *(mandatory)*

### The audit (US1)

- **FR-001**: Every decision **001–027** MUST be checked against the repository,
  and the check MUST be recorded — what was looked for, and what was found.
- **FR-002**: Every row MUST be classified. The vocabulary MUST distinguish at
  least: **holds**, **diverges** (built differently), **not built**, and
  **unverifiable here** (deployment or hardware claims).
- **FR-003**: Constitution **§IX**'s table MUST be checked the same way, and its
  observability row reconciled with ADR-0118.
- **FR-004**: A row that **holds** MUST be recorded as checked, not left silent.
  An audit that only lists failures cannot be distinguished from one that stopped
  early.

### Correcting the record (US1, US2)

- **FR-005**: Corrections MUST be made by ADR, per governance — ADR-0000's rows
  and the constitution cannot be edited without one.
- **FR-006**: A corrected row MUST state the true position, and where it diverges
  from the original decision, MUST say so rather than silently restating.
- **FR-007**: The original decision text MUST remain legible. **The record of
  what was decided is not to be overwritten by what happened** — ADR-0118 and
  ADR-0128 both set this pattern, keeping the original and marking it amended.

### What the audit must not do

- **FR-008**: The audit MUST NOT re-litigate architecture. Where a divergence may
  be the wrong choice, it is **raised as an issue**, not decided here. *(AEL vs
  CEL is the clear case: recording that AEL is what exists is not endorsing it.)*
- **FR-009**: Each divergence MUST end in one of exactly two places — an ADR that
  makes it legitimate, or an issue that proposes correcting it. **Neither silence
  nor a note that does neither.**

### Keeping it true (US3)

- **FR-010**: Corrected claims MUST be guarded by an automated check, following
  `LatencyLegRecordTests`.
- **FR-011**: The guard MUST fail with a message saying what is wrong and why it
  matters, not merely that a string is missing.
- **FR-012**: The guard MUST NOT prevent a row being updated when the system
  changes. It pins claims against *drift*, not against *progress*.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 27 decisions and all 4 §IX rows carry a recorded verdict with
  evidence. **Coverage is the point** — a partial audit leaves exactly the
  uncertainty this feature exists to remove.
- **SC-002**: Someone who did not perform the audit can pick any row, follow its
  recorded check, and reach the same verdict.
- **SC-003**: No constitutional principle contradicts an accepted ADR.
- **SC-004**: Every divergence has an ADR or an issue against it — zero left as
  prose alone.
- **SC-005**: Restoring a corrected claim fails the build.
- **SC-006**: A reader can sort every decision into built / partly / not built
  from the record alone, and be right.

---

## Out of Scope

- **The other ~100 ADRs.** Bounded deliberately: ADR-0000 and §IX are the most
  cited and the ones three features have already tripped over. The same method
  would apply to the rest, and that is a later feature — **noting that ADR-0117's
  table and ADR-0026's abandoned stack suggest the problem is not confined here.**
- **Building anything a decision describes.** If `IRuleEngine` is absent, this
  feature records that; it does not write it.
- **Re-deciding architecture** (FR-008).
- **ADR-014 and ADR-021**, already amended by specs 045 and 046. They are the
  evidence, not the work.
- **Product code.** This feature changes documents and adds guards.

---

## Assumptions

- **A wrong record has already cost more than the audit will.** Three features
  paid for discovery; a fourth is likely. This is the argument for doing it now
  rather than opportunistically, and it is an argument from history rather than
  from principle.
- **Most rows are probably fine.** The reconnaissance found nine problems and
  several rows that hold exactly. The spec expects a correction, not a rewrite,
  and a result that condemned everything would itself be suspect.
- **Grep is evidence, absence is not.** A component may exist under another name
  — StreamKeeper's work partly does. Each "not found" needs a second search on
  different terms before it becomes a verdict. *(This repo has a standing rule
  about exactly that, learned the hard way.)*
- **Some rows cannot be settled from this repository**, and saying so is a
  verdict rather than a failure.

---

## Dependencies

- **ADR-0000** and **constitution §IX** — the subjects.
- **ADR-0117** — established that the record decides whether obligations apply,
  which is why this matters beyond tidiness.
- **ADR-0118**, **ADR-0128** — the pattern for amending a decision while keeping
  the original legible.
- **Spec 040, 045, 046** — the three discoveries that motivate this, and the
  evidence for rows 014, 021 and 015.
- **`LatencyLegRecordTests`** — the guard pattern FR-010 follows.
