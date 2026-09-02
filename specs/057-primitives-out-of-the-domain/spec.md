# Feature Specification: Primitives out of the domain, guards onto `Ensure`

**Feature Branch**: `057-primitives-out-of-the-domain`

**Created**: 2026-09-02

**Status**: Draft

**Input**: User description: "Remove primitive types from domain models and standardize all argument guards on `Ensure.That`, with the rules amended and enforced so neither can drift back."

## Context

Three rules already exist in this repository. None of the three is enforced,
and each has drifted:

| Rule | Where it is written | How it drifted |
|---|---|---|
| Argument guards use `Ensure.That(...)` | ADR-0105 (2026-06-01), which converted ~277 sites | 2 production sites already reverted to `ArgumentNullException.ThrowIfNull` |
| Value objects are the default; primitives do not cross domain boundaries | Constitution §II | 13 `string` and 26 `DateTimeOffset` properties remain on aggregates |
| Domain logic is TDD red-green-refactor | Constitution §Testing | Phase 4's gate checks only that tests are **green**, never that any test was seen **red** |

This is the same defect class CLAUDE.md already documents against §IV and
against the Phase 3 board gate: **a rule nobody checks against what is
actually happening**. The feature is therefore not only the cleanup — it is
the enforcement that makes the cleanup permanent. A cleanup without the
enforcement would be the fourth instance of the same failure.

ADR-0105 also turns out to be **narrower than it reads**. It converted null
guards only. ~24 sites guard strings and numeric ranges with the BCL helpers
(`ArgumentException.ThrowIfNullOrWhiteSpace`,
`ArgumentOutOfRangeException.ThrowIfLessThan`) and were never in its scope at
all. Those are exactly the `""` cases that motivate this work.

## User Scenarios & Testing *(mandatory)*

The "users" of this feature are the engineers and reviewers working in this
repository, plus the CI system acting on their behalf.

### User Story 1 — The rules refuse to drift again (Priority: P1)

An engineer writes a guard the old way, or reverts one. The build fails
immediately, at their desk, naming the rule and the ADR. They cannot open a
PR that reintroduces the idiom, and no reviewer has to remember to catch it.

The same change records the amended rules where the next engineer will look:
the primitive list is named exhaustively in the constitution rather than left
to a three-example illustration, the guard rule is widened from null-only to
all argument preconditions, and the testing rule is split so it can actually
be followed (see Story 6).

**Why this priority**: This is the only story that prevents recurrence.
Every other story is a one-time cleanup that, without this, decays exactly as
ADR-0105's did. It is also independently valuable: shipped alone, it closes
the two live violations and stops the bleeding.

**Independent Test**: Reintroduce `ArgumentNullException.ThrowIfNull` into
any production or test file and confirm the build fails with the rule's
message. Revert, and confirm the build passes.

**Acceptance Scenarios**:

1. **Given** the enforcement is in place, **When** an engineer writes
   `ArgumentNullException.ThrowIfNull(x)` in a Domain, Application, Api,
   Infrastructure, `Shared.*` or test file, **Then** the build fails with a
   message naming the replacement and the governing ADR.
2. **Given** the enforcement is in place, **When** an engineer writes
   `ArgumentException.ThrowIfNullOrWhiteSpace(s)`, **Then** the build fails
   the same way.
3. **Given** the enforcement is in place, **When** the EF tooling regenerates
   a migration file containing the banned idiom, **Then** the build still
   passes, because generated migrations are exempt and cannot carry a
   suppression across regeneration.
4. **Given** the enforcement is in place, **When** the composition root uses
   the banned idiom, **Then** the build passes, because it cannot reference
   the guard helper by design.
5. **Given** the amended rules, **When** an engineer reads the constitution,
   **Then** the primitives are named exhaustively and the exemptions are
   listed with their reasons, so no reader has to infer the boundary.

---

### User Story 2 — An empty string cannot enter the domain (Priority: P2)

Today a caller can put `""` into a rule's trigger source, a dead letter's
topic, or a stream's last error. Nothing rejects it, because the property is
a bare `string` and validation, where it exists at all, lives at whichever
call site happened to think of it.

After this story each such concept is a named type that validates once, in
its own factory. `""` and `"   "` are rejected at construction, everywhere,
for every caller, forever.

**Why this priority**: This is the story that addresses the stated `""`
concern structurally rather than per-call-site. It is second because it
depends on nothing but Story 1's rule text, and delivers the most correctness
per unit of change.

**Independent Test**: For each new type, attempt construction from `""` and
from whitespace and confirm both are refused; confirm the aggregate can no
longer be built with a bare string at all.

**Acceptance Scenarios**:

1. **Given** a domain concept previously typed as `string`, **When** any
   caller attempts to construct it from `""` or whitespace, **Then**
   construction is refused.
2. **Given** the conversion is complete, **When** the persisted schema is
   compared before and after, **Then** it is unchanged — the column type and
   nullability are identical.
3. **Given** an aggregate whose string property was converted, **When**
   existing rows are read back, **Then** they load and round-trip unchanged.

---

### User Story 3 — A timestamp says which moment it is (Priority: P3)

`CreatedAt`, `RegisteredAt`, `RejectedAt`, `ProvisionedAt` and 22 others are
bare instants. Two of them, in one context, are already value objects that
carry their meaning — the pattern exists and was simply never propagated.

After this story every domain instant is a named type, so a value meaning
"when the plant rejected this delivery" can no longer be passed where "when
the operator created this rule" is expected.

**Why this priority**: It is the largest mechanical chunk and delivers real
type safety, but it is lower-risk and lower-yield per edit than Story 2, and
nothing depends on it.

**Independent Test**: Confirm each converted instant is a distinct type, that
two different instants cannot be substituted for one another, and that
ordering and range queries over the persisted values still return the same
rows as before.

**Acceptance Scenarios**:

1. **Given** two different timestamp concepts, **When** one is passed where
   the other is expected, **Then** it is refused before the system runs.
2. **Given** a converted timestamp, **When** a listing orders or range-filters
   by it, **Then** the results are identical to before the conversion.
3. **Given** the conversion is complete, **When** the persisted schema is
   compared before and after, **Then** it is unchanged.

---

### User Story 4 — Untrusted input becomes typed at the edge (Priority: P4)

Roughly a dozen command and query shapes still carry raw text and raw
identifiers inward from the API. Validation therefore happens somewhere
downstream, or not at all, and the same value is re-validated at several
depths.

After this story the value is converted once, where the untrusted input
arrives, and everything past that point is typed. This matches the existing
house rule that validation belongs at trust boundaries only.

**Why this priority**: It closes the loop — Stories 2 and 3 type the domain,
and this stops untyped values reaching it. It is fourth because it is the
most call-site-heavy per unit of correctness gained, and depends on the types
introduced by Stories 2 and 3 existing first.

**Independent Test**: Submit malformed input to each affected endpoint and
confirm it is refused at the boundary with a client error, not deeper in.

**Acceptance Scenarios**:

1. **Given** malformed input, **When** it is submitted to the endpoint,
   **Then** it is refused at the boundary and the refusal is reported as a
   client error, not a server fault.
2. **Given** a well-formed request, **When** it is processed, **Then** the
   observable response is unchanged from before the conversion.

---

### User Story 5 — The concurrency token is a domain concept (Priority: P5)

The aggregate version is a bare integer shared by every aggregate, threaded
through commands as `ExpectedVersion`, and reaching the persistence layer as
an optimistic-concurrency token. It is the most widely-spread primitive in
the codebase.

After this story it is a named type: an arbitrary integer can no longer be
passed where a version is expected, and the value that arrives on the
`If-Match` header is converted once at the boundary.

**Why this priority**: Last, deliberately. It is the largest surface (92
sites, 38 files, 10 persistence mappings) and carries the one genuine
technical risk in this specification — a converted value that is *also* an
optimistic-concurrency token is a known rough edge in the persistence layer.
Sequencing it last means that if it proves unworkable, the other five stories
are already banked and the decision to stop is cheap.

**Independent Test**: Confirm a stale write is still refused, per aggregate,
before and after conversion.

**Acceptance Scenarios**:

1. **Given** two concurrent writers, **When** the second submits a stale
   version, **Then** the write is refused exactly as before — same refusal
   name, same client-visible status.
2. **Given** an arbitrary integer, **When** it is passed where a version is
   expected, **Then** it is refused before the system runs.
3. **Given** the conversion is complete, **When** the persisted schema is
   compared before and after, **Then** it is unchanged.
4. **Given** one aggregate has been converted, **When** its concurrency
   behaviour is verified, **Then** the remaining aggregates are converted
   only after that verification passes.

---

### User Story 6 — "Red first" becomes checkable (Priority: P1, with Story 1)

Today the testing rule says domain logic is red-green-refactor, but the only
gate asks whether tests are green. A test written after the code passes that
gate identically to one written before it. The rule is unfalsifiable, so it
is unenforced by construction.

It is also *too narrow in one direction and impossible in another*: it binds
"domain logic" only, and a blanket red-first reading cannot be satisfied by
behaviour-preserving work at all — this very specification would be the first
to fail it. Stories 3, 4 and 5 must be **green throughout**; a red test during
them is a regression, not a step.

After this story the rule states both obligations, and the Phase 4 gate asks
for the evidence that is actually available.

**Why this priority**: It ships with Story 1 because it is the same
governance record amending the same two documents. Splitting it would leave
one of the two amendments unrecorded.

**Independent Test**: Read the amended rule and confirm it prescribes a
different, checkable obligation for new behaviour than for refactoring, and
that this specification's own six stories can each satisfy exactly one of
them.

**Acceptance Scenarios**:

1. **Given** a change that adds new behaviour, **When** it is submitted for
   review, **Then** the failing test is quoted in the submission, and a
   submission without it does not pass the gate.
2. **Given** a behaviour-preserving change, **When** it is submitted, **Then**
   the obligation is that covering tests existed and stayed green, and a red
   test at any point is treated as a regression rather than as progress.
3. **Given** the amended rule, **When** it is applied to this specification,
   **Then** Stories 1 and 2 fall under the red-first obligation and Stories
   3, 4 and 5 fall under the green-throughout obligation.

### Edge Cases

- **A generated file contains a banned idiom.** Migrations are regenerated by
  tooling and cannot carry an inline suppression, so the exemption must be
  positional (by path) rather than per-occurrence.
- **The guard helper is unavailable.** The composition root does not depend on
  the module that provides it, by deliberate design. It stays exempt, as it
  already is.
- **The helper's own documentation names the banned idiom.** Prose mentions
  must not trip the enforcement; only real uses should.
- **A converted guard changes which exception is raised.** The BCL string
  helper raises different exception types for null versus whitespace. Any
  caller or test depending on the distinction must be identified before
  conversion, not discovered afterwards.
- **A value-converted property is also a concurrency token.** Verified on one
  aggregate before the remaining nine.
- **Coverage is thin where a refactor lands.** "Green throughout" guarantees
  nothing if nothing is watching. Where covering tests are absent on a path
  being retyped, they are added *first*, while the old shape still compiles.
- **A commit that only builds with its successor.** Rebase-merge lands commits
  individually, so each must build alone — Story 5 is committed per aggregate.

## Requirements *(mandatory)*

### Functional Requirements

**Rule and enforcement (Story 1, Story 6)**

- **FR-001**: The constitution MUST name the disallowed domain primitives
  exhaustively rather than by example.
- **FR-002**: The guard rule MUST cover all argument preconditions — null,
  emptiness, and range — not null alone.
- **FR-003**: The exemptions MUST be recorded with their reasons: the error
  contract's code and message, opaque captured payloads, generated
  migrations, and the composition root.
- **FR-004**: A banned idiom MUST fail the build, not a review.
- **FR-005**: Enforcement MUST cover production, shared library, and test
  code alike.
- **FR-006**: Generated migrations MUST be exempt positionally.
- **FR-007**: The governance change MUST be recorded in a decision record, as
  the constitution's own amendment procedure requires.
- **FR-008**: The testing rule MUST state a red-first obligation for new
  behaviour and a green-throughout obligation for behaviour-preserving change.
- **FR-009**: The red-first obligation MUST apply to domain, application and
  infrastructure alike.
- **FR-010**: The Phase 4 gate MUST require the observed failure to be quoted
  in the submission.

**Guard conversion (Story 1)**

- **FR-011**: Every non-exempt guard MUST use the sanctioned helper.
- **FR-012**: Conversion MUST preserve the raised exception type and message
  where any caller or test observes them.

**Domain typing (Stories 2, 3, 5)**

- **FR-013**: No aggregate MUST expose a disallowed primitive outside the
  recorded exemptions.
- **FR-014**: Each converted text concept MUST reject empty and whitespace
  input at construction.
- **FR-015**: Each converted instant MUST be distinct from every other, so
  they cannot be substituted.
- **FR-016**: The aggregate version MUST be a named type across the shared
  abstraction, the commands, and the persistence mappings.
- **FR-017**: No conversion MUST alter the persisted schema.
- **FR-018**: Existing stored data MUST load and round-trip unchanged.

**Boundary typing (Story 4)**

- **FR-019**: Untrusted input MUST be converted to its named type at the
  boundary where it arrives.
- **FR-020**: A conversion failure MUST be reported as a client error.
- **FR-021**: Observable API behaviour MUST be unchanged for well-formed
  requests.

**Sequencing**

- **FR-022**: Each commit MUST build on its own.
- **FR-023**: The concurrency-token conversion MUST be proven on one
  aggregate before the rest.
- **FR-024**: Where a path being retyped lacks covering tests, they MUST be
  added before the retyping.

### Key Entities

- **Guard rule** — which argument-precondition idiom is sanctioned, over what
  scope, with which exemptions.
- **Primitive boundary** — which underlying types may not appear on a domain
  model, and the exemptions that survive.
- **Testing obligation** — the two-branch rule distinguishing new behaviour
  from behaviour-preserving change, and the evidence each requires.
- **Named domain type** — a validated concept replacing a primitive: text,
  instant, or aggregate version.
- **Enforcement** — the build-time mechanism that fails on a banned idiom.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Reintroducing any banned guard idiom fails the build in under a
  minute at the engineer's desk, without a reviewer or CI round-trip.
- **SC-002**: Zero non-exempt uses of the banned idioms remain; every
  remaining use is one of the four recorded exemptions.
- **SC-003**: Zero disallowed primitives remain on domain models outside the
  recorded exemptions.
- **SC-004**: No conversion produces a schema change — the migration diff is
  empty across all eleven persistence configurations.
- **SC-005**: Every existing test passes unchanged in observable behaviour;
  the full suite is green at every commit.
- **SC-006**: A stale write is refused identically before and after, for all
  ten aggregates with a concurrency token.
- **SC-007**: Every domain text concept rejects empty and whitespace input,
  verified by a test per type that was observed failing first.
- **SC-008**: The three drifted rules each name their scope, exemptions and
  evidence explicitly, so a reader can tell whether the rule is being followed
  without reading the codebase.
- **SC-009**: Each of the six stories can be shipped and reverted
  independently, and each maps to exactly one of the two testing obligations.

## Assumptions

- **Naming follows existing precedent.** New types are hand-written per
  bounded context, following the two timestamp types and the shared text base
  that already exist. No shared timestamp base is introduced, because the
  established pattern is per-context and no second caller demands otherwise.
- **Timestamps normalize rather than validate.** The existing timestamp types
  normalize to UTC and validate nothing. New ones follow suit, so no new
  guard overload is required. If a specific concept needs a bound, it is
  added for that concept only.
- **The unwrap operator is carried over.** The existing timestamp type
  documents that the persistence layer cannot translate member access in
  ordering and range predicates, and exposes an implicit unwrap for exactly
  that reason. That comment is load-bearing and applies to every new timestamp
  type on a queried column.
- **The exemptions are closed.** The four recorded exemptions are the complete
  list; anything else requires amending the rule, not a local judgement call.
- **Opaque payloads may still be typed.** They are exempt from having to be
  parsed or interpreted, which does not preclude a named type enforcing
  non-emptiness and a size bound. The narrower reading is the intent.
- **Guard conversion is behaviour-preserving.** The sanctioned helper was
  chosen in ADR-0105 precisely because it raises the same exception type and
  message as the idiom it replaces. Where the BCL string helper's null versus
  whitespace distinction is observed, it is identified before conversion.
- **No production deployment is at risk.** There is no production deployment
  yet, so the schema-neutrality requirement is a correctness property to
  verify rather than a migration to coordinate.
- **Existing analyzer machinery is reused.** The repository already fails the
  build on a banned idiom for a different rule; that mechanism is extended
  rather than replaced, and its scoping is widened because this rule binds
  more broadly than the existing one.

## Out of Scope

- Any behaviour change visible to an operator or a wall.
- The frontend applications; this specification is server-side only.
- Primitives in the cross-context message contracts, which are a wire format
  and deliberately carry primitives.
- The composition root's guards.
- Introducing event sourcing, or any other stack change, alongside the
  retyping.
- Retrofitting the red-first evidence requirement to already-merged work.
