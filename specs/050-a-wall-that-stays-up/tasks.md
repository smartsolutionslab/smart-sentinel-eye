# Tasks — 050 a wall that stays up

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)

Twelve tasks. **No application code.** If a task seems to need some, the scope
was misjudged — stop and take it back through the gate.

---

## Do not

- **Do not name the scope in the application.** The whole design is that it does
  not have to (R1). Requesting a scope the realm has not granted fails the
  entire sign-in — no token — which took every kiosk down during spec 049.
- **Do not grant the privilege to operators.** FR-006. The widening reaches
  wall-display accounts and nobody else, and T005 exists to show it.
- **Do not raise session lifetimes for the realm.** FR-012 — it would loosen
  expiry for the management app too, trading a bounded loosening for an
  unbounded one.
- **Do not build enrolment, rotation or revocation as an operator workflow.**
  Out of scope, and the third feature running that would grow past what was
  asked if allowed to.
- **Do not touch `management-web`, the kiosk application, or any C#.**
- **Do not write bare `#NNNN` issue numbers** in committed docs.

---

## Phase 1 — The account and the scope

- [x] T001 Declare a wall-display account per fab in `src/AppHost/Realms/smart-sentinel-eye-realm.json`. Each carries the base role, membership of **its own fab's group**, and the offline privilege — nothing else. One per fab is forced, not preferred: fab scoping comes from the account, so a shared one would let any screen see every fab (R2).
- [x] T002 Add `offline_access` as a **default** client scope on the kiosk client. **Default, not optional**, so the application never names it and an app build cannot be refused by a realm that has not caught up (R1).
- [x] T003 [P] Note in the realm file, next to the account, **what it is for and what ends it**. It is declared by hand, never expires, and will outlive whoever installed it — a credential nobody remembers is a credential nobody rotates.

**Checkpoint**: a screen can sign in as something that is not a person.

---

## Phase 2 — US2: the account may do nothing else *(P1)*

- [x] T004 [US2] Test that the wall-display account is **refused every write** it attempts and **refused reads outside its own fab**. Assert the refusals, not the successes — a test that only shows the wall working proves nothing about what else the account could do.
- [x] T005 [US2] **Test that an operator account holds exactly what it held before.** This is the task most likely to be skipped and the one FR-006 exists for: the feature was refused once precisely because it widened operator authority, and "we didn't touch it" is an argument rather than evidence.
- [x] T006 [US2] Guard the claim in `tests/Architecture.Tests/` as a **consistency check** — the realm's wall-display accounts carry the offline privilege and operator accounts do not, failing in either direction. Not a text pin.

**Checkpoint**: **US2 is the half that makes the rest safe.** If it cannot be shown, nothing below should ship.

---

## Phase 3 — US1: the wall stays up *(P1)*

- [x] T007 [US1] Test that a screen signing in as the wall-display account receives an **offline** grant — decode the refresh token and assert its type and the absence of an expiry. Asserting "a token exists" passes today and proves nothing.
- [ ] T008 [US1] Test survival past the session limits **with the ceiling shortened on a test realm**, because nothing in CI runs for ten hours. The task, the test's comment and the verification note must each say this **demonstrates the mechanism and not the production configuration** — one place saying it is not enough.
- [ ] T009 [US1] Test recovery from an outage **longer than the idle cut-off** — the case spec 049 explicitly could not do. Induced by ageing the stored grant past that window before restarting, not by restarting quickly.

**Checkpoint**: the wall stays up, and comes back from an outage that outlasts a session.

---

## Phase 4 — US3: withdrawal *(P2, and genuinely uncertain)*

- [ ] T010 [US3] **Test whether one screen's session can be ended while a sibling keeps running.** R4 reasons it can; reasoning is what this feature exists to distrust. Both directions: the withdrawn screen stops, the other does not.
- [ ] T011 [US3] **If it cannot, re-scope US3 in the open** — amend the spec, file the gap, and say so in the record. Do not reinterpret FR-009 into something the mechanism happens to satisfy. Spec 049's US3 was re-scoped exactly this way and that was the right move; quietly redefining it would not have been.

---

## Phase 5 — The record and verification

- [ ] T012 Write the ADR and update constitution §Availability to **what was actually demonstrated** — including that ten hours was shown on a shortened ceiling and that twenty screens were not exercised. Then `verification.md`. §Availability has been half-met twice and described as met once; the entry should not claim the target is discharged unless it is.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003            Phase 1
          │
   ┌──────┴───────┬─────────────┐
   ▼              ▼             ▼
T004 ─▶ T005    T007 ─▶ T008   T010 ─▶ T011
   └─▶ T006        └─▶ T009      US3
   US2  (gate)     US1
          └────────┬─────────────┘
                   ▼
                 T012
```

**US2 gates the rest.** US1 and US3 are independent of each other.

---

## Parallel opportunities

- **T003** is a comment in a file the others also touch — sequential in practice.
- **US1 and US3 are genuinely parallel** once Phase 1 lands.
- **T004–T006 are not parallel**: one claim, asserted three ways.

---

## Implementation strategy

**US2 first, and it is a gate rather than a phase.** The whole feature is
letting a credential live longer, so demonstrating that it may do little is what
makes the longer life acceptable. Shipping US1 without US2 would be shipping the
cost without the mitigation.

**No application code, and no coverage gate to cite.** ADR-0065's thresholds
cover Domain, Application and Shared assemblies; none are touched. That is not a
reason to test less, only a reason not to claim the gate as evidence.

**The feature issue is on Project #13** — Phase 3's gate is satisfied.

---

## Three things most likely to go wrong

1. **US2 gets asserted rather than demonstrated.** "We only touched the
   wall-display account" is an argument. T005 checks an operator account
   directly, because this feature was refused once for widening exactly that.

2. **The withdrawal claim gets reasoned into being met.** R4 is a chain of
   plausible inference and nothing more. T010 tests it and T011 re-scopes in the
   open if it fails — the failure mode is not "it doesn't work", it is "it gets
   written up as working".

3. **The record claims the target is discharged.** Ten hours will be shown on a
   shortened ceiling and twenty screens will not be exercised. §Availability has
   already been described as met once when it was not.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The grant outlives a session | T007, decoding the token | asserting a token exists |
| A screen survives the ceiling | T008, ceiling shortened | anything shorter, which passes with the defect |
| Recovery past the idle cut-off | T009, grant aged first | a quick restart, which spec 049 already did |
| The account may do nothing else | T004 | the wall working |
| **Operators gained nothing** | **T005, checked directly** | not having touched them |
| One screen can be withdrawn alone | T010 | R4's reasoning |
| **Ten hours in production** | **nothing** | a shortened ceiling shows the mechanism only |
| **Twenty screens together** | **nothing** | one screen is a weaker claim |
| **That the grant is ever cleaned up** | **nothing — nothing does it** | — |

The last three rows are the honest ones, and the last is new: this feature
creates a credential that never expires and nothing removes it.
