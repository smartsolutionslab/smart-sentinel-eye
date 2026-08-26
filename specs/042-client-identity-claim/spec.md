# Feature Specification: The configuration stops discarding what it says

**Feature Branch**: `042-client-identity-claim`

**Created**: 2026-08-26 *(rewritten the same day — see Assumptions)*

**Status**: Draft

**Issue**: 1885 *(written without a `#` deliberately — this repo's automation
closes a merely-mentioned issue on merge)*

**Input**: Every identity in the development directory lists four permissions
that do not exist. The sign-in service discards them and says so, thirty-two
times on every start. One identity depends on what was discarded, and cannot say
who is using it.

---

## Why this exists

On every start, the sign-in service reads the directory, throws away four
entries from **each of the eight identities**, and reports each one. Thirty-two
notices. The configuration file goes on listing them, so anyone reading it — a
person or an agent — sees four things applied that are not.

**That silence has now hidden two defects in two weeks.**

The first cost a screen that had never worked: the wall display could not list
anything, because its identity was missing a piece that would have arrived with
one of the discarded four. Finding it took a day.

The second is already in place and nobody has hit it yet. The **replacement
identity for the operator console** — created for that purpose, described in the
directory as replacing the one in use, still unused — **cannot say who is using
it**. The system refuses to record a change it cannot attribute, deliberately, in
seventeen places. Adopting that identity would refuse every operator change on a
screen whose entire job is making changes.

### Everything else is fine, for a reason nobody wrote down

The other identities do say who is using them. Two because of things that happen
to be true: one inherits the piece from a broad administrative permission it
holds, the other carries a hand-added copy from last week's narrow fix. The rest
— the background workers — get it from the *way* they sign in rather than from
any configuration at all.

**None of that is by design, and none of it is written down.** A background
worker that ever acts on a person's behalf would lose it silently, and the two
that work by accident would lose it the moment either accident was tidied away.

### Nothing checks any of it

The failure is invisible three times over: the notice at start-up goes unread,
signing in succeeds regardless, and the fault appears only at the first change.
No check exists that an identity can name its holder, and none that the
permissions an identity claims are real.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The configuration describes what actually happens (Priority: P1)

Reading the configuration tells you what the system does. Nothing in it is
thrown away on the way in.

**Why this priority**: It is the mechanism. Two defects have hidden behind these
notices; fixing the identity that is currently broken while leaving the four
fictional entries on each of the eight would repair one instance and preserve the
thing that produces them.

**Independent Test**: Start the system, read the log. Nothing is discarded.

**Acceptance Scenarios**:

1. **Given** the system starts,
   **When** the directory is loaded,
   **Then** **nothing** is reported as named-but-missing — down from thirty-two.
2. **Given** the configuration,
   **When** any identity's permissions are read,
   **Then** every one of them exists.

---

### User Story 2 — An identity a person uses can say who they are (Priority: P1)

Every identity a person signs in with carries the piece that names its holder,
and does so because it was configured to, not because of something else.

**Why this priority**: It is the live defect, and the one waiting to be walked
into.

**Independent Test**: Sign in with each identity a person can use; each names its
holder.

**Acceptance Scenarios**:

1. **Given** every identity a person can sign in with,
   **When** each issues a credential,
   **Then** **each one** names its holder — checked one at a time, not sampled.
2. **Given** the replacement identity for the operator console,
   **When** a change is made with it,
   **Then** the change is recorded against the person who made it, rather than
   refused. *(Adopting that identity is not part of this feature; being able to
   is.)*
3. **Given** an identity that names its holder only because of a broad
   permission it happens to hold,
   **When** that permission is removed,
   **Then** it still names its holder.

---

### User Story 3 — The next one cannot hide (Priority: P1)

Adding an identity that cannot name its holder, or one that claims a permission
that does not exist, fails a check.

**Why this priority**: Equal to the others. Both failures are silent today, and
the only reason either was found was someone chasing an unrelated symptom.

**Independent Test**: Break each, watch the check go red.

**Acceptance Scenarios**:

1. **Given** the directory as it should be,
   **When** the check runs,
   **Then** it passes.
2. **Given** an identity added without the means to name its holder,
   **When** the check runs,
   **Then** it **fails** — demonstrated by causing it, not by assuming it.
3. **Given** an identity claiming a permission the directory does not define,
   **When** the check runs,
   **Then** it **fails**, rather than being discarded at start-up as today.

---

### User Story 4 — One notion of naming, not three (Priority: P2)

There is a single place that makes an identity able to name its holder, and
every identity that needs it uses that.

**Why this priority**: P2 because the system works either way today. But three
different mechanisms currently supply one fact — a permission that happens to
carry it, a hand-added copy, and a side effect of how a background worker signs
in. Three sources of one fact is how they drift apart, and two of the three are
accidents.

**Independent Test**: One shared definition; no identity carries a private copy.

**Acceptance Scenarios**:

1. **Given** the change is complete,
   **When** the configuration is read,
   **Then** one shared definition provides the naming piece, and no identity
   carries its own duplicate.
2. **Given** the identity that inherits it from a broad administrative
   permission,
   **When** the configuration is read,
   **Then** that permission is no longer what supplies it.

---

### Edge Cases

- **Background workers, which act for no person.** They name their holder
  because of *how* they sign in, not because of configuration — so they are
  unaffected either way. They are included in the shared definition anyway, and
  the reason is recorded: relying on that side effect means every future identity
  needs a judgement about which kind it is, and an error in that judgement is
  invisible until the first change.
- **The identity carrying a hand-added copy.** Folded in, or it becomes a
  second source of one fact.
- **The identity that inherits it from a broad permission.** It must keep
  naming its holder for a reason that survives that permission being narrowed —
  which is a live prospect, since narrowing exactly that kind of permission is
  what the previous feature did.
- **An identity that signs in perfectly and then cannot do anything.** Today's
  symptom for the broken one, and why it is hard to spot: signing in proves
  nothing about whether a change can be attributed.
- **A permission name with a typo.** Currently discarded with a notice nobody
  reads. Should fail.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: No identity may claim a permission the directory does not define.
- **FR-002**: Loading the directory MUST report nothing as named-but-missing.
- **FR-003**: Every identity a person can sign in with MUST name its holder.
- **FR-004**: A change made through any such identity MUST be recorded against
  the person who made it.
- **FR-005**: The means of naming a holder MUST be defined **once** and shared.
- **FR-006**: No identity may carry a private copy of that means, and no
  *permission* may supply it as a side effect.
- **FR-007**: Something MUST detect an identity that cannot name its holder, and
  fail.
- **FR-008**: Something MUST detect an identity claiming a permission that does
  not exist, and fail rather than discard it.
- **FR-009**: Nothing beyond the holder's identifier may be added. Nothing in the
  product reads anything else about who the holder is.
- **FR-010**: Nothing about what any identity is *permitted to do* may change.
- **FR-011**: The record MUST state why identities that act for no person are
  unaffected. It is a property of how they sign in, it is currently undocumented,
  and it looks like luck.

### Key Entities

- **Identity**: what a person or a background worker signs in as.
- **Naming piece**: the part of a credential that says who holds it. Supplied
  today by three unrelated mechanisms, two of them accidental.
- **Permission list**: what an identity may do. Currently begins, on every
  identity, with four entries that do not exist.
- **Attribution**: recording a change against the person who made it. Refused
  outright when the holder cannot be named — deliberately, so the record is never
  wrong.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: **Zero** entries discarded on start-up, down from thirty-two.
- **SC-002**: **Every** identity a person can sign in with names its holder —
  counted one at a time, not sampled.
- **SC-003**: An identity that cannot name its holder **fails** a check —
  demonstrated by causing it.
- **SC-004**: An identity claiming a permission that does not exist **fails** a
  check — demonstrated by causing it.
- **SC-005**: A change is attributed to the person who made it, observed end to
  end rather than inferred from configuration.
- **SC-006**: **One** shared definition supplies the naming piece; **zero**
  private copies; **zero** permissions supplying it as a side effect.
- **SC-007**: What every identity is permitted to do is unchanged.

---

## Assumptions

- **This spec was rewritten after its first draft was measured and found
  wrong.** The draft claimed six of eight identities could not name their holder.
  That was read off the configuration file rather than from credentials, and it
  was wrong: **one** cannot. Background workers name their holder through the way
  they sign in, which no file records. The error is kept here rather than tidied
  away, because it is the same mistake the configuration itself makes — asserting
  from a file what only a measurement can settle — and it was made while writing
  a specification about that.
- **Only the holder's identifier is needed.** Nothing in the product reads a
  display name, an address, or a role. Restoring the other discarded groups would
  be building for needs that do not exist.
- **The refusal behaviour is correct and stays.** Refusing to record an
  unattributable change is deliberate. This removes the reason it fires; it does
  not soften it.
- **No production deployment exists**, so changing every identity coordinates
  with nothing. The directory is rebuilt from this configuration on a developer's
  machine.
- **Last week's narrow fix was right to be narrow.** It made one screen work
  without touching seven other identities. This is the general version, done on
  purpose.

---

## Out of Scope

- **Pointing the operator console at its replacement identity.** A separate
  decision and a separate change. This makes it possible; it does not do it.
- **What any identity is permitted to do.** Naming is not permission.
- **The operator console's missing video surface**, filed separately.
- **The overlay-timing measurement**, filed separately.
- **Any production rollout.** There is no production deployment.
