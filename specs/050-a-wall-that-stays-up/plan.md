# Implementation Plan — 050 a wall that stays up

**Branch**: `050-a-wall-that-stays-up` | **Spec**: [spec.md](./spec.md) · **Research**: [research.md](./research.md)

---

## Summary

A wall-display account per fab, holding the one privilege that makes a grant
outlive a session, and **nothing else**. Screens sign in as that account instead
of as an operator.

The application does not change at all. R1 established that a **default** client
scope produces the offline grant without the app naming it — which is what keeps
an app build and a realm change from being able to break each other.

---

## Technical Context

| | |
|---|---|
| **Changes** | Realm configuration only — accounts, one client scope, one role assignment |
| **Application code** | **None.** Spec 049 already persists the grant and spends it on startup |
| **Backend** | Unchanged. No C# — the admin client cannot create users and does not need to (R3) |
| **Tests** | e2e against the real stack; architecture guards for the record |
| **Unknowns** | One, named below and scheduled before the claim depends on it |

---

## Constitution Check

| Principle | Assessment |
|---|---|
| **§Availability — 20 kiosks unattended** | The target. This is the second attempt; the first stopped here deliberately |
| **§VIII least privilege** | **The centre of gravity.** A wall-display account may view one fab and do nothing else. Operators gain nothing — FR-006 makes that a thing to demonstrate |
| **§IV latency** | Not on the path. No claim |
| **Smallest possible change** | No application code, no new bounded-context capability, no device-management workflow |

**Gate: PASS**, with the trade stated rather than implied: a grant that never
expires is created, and §VIII is satisfied by *narrowness*, not by *lifetime*.
The ADR must say so.

---

## Design

### The account

One per fab, because fab scoping comes from the account (R2). It carries the base
role every account has, membership of its fab's group, and the offline privilege
— and no more. A screen signs in as it once, at installation.

### The scope, as a default and not an option

The kiosk client gains `offline_access` as a **default** scope. The application
never names it.

**This is the decision that repays spec 049's outage.** Requesting a scope the
realm has not granted fails the whole sign-in — no token at all. Making it a
default means there is nothing in the bundle to be refused, so the app build and
the realm change stop being coupled and ordered. Verified in R1, not assumed.

### What does not change

- **No application code.** If a change seems necessary, the scope was misjudged.
- **No other client.** `management-web` keeps the session lifetimes it has —
  FR-012.
- **No operator authority.** The privilege reaches wall-display accounts only.

---

## "Done" — before any code

| Story | Done when |
|---|---|
| **US1 stays up** | A screen holds a grant whose refresh token is an *offline* one with no expiry, and survives past the session limits. Demonstrated by **shortening the ceiling on a test realm** — which shows the mechanism and **not** the production configuration, and the note must say so |
| **US2 narrow** | The account is refused every write and every read outside its fab, **and an operator account is shown to hold exactly what it held before**. The second half is the one that would be skipped |
| **US3 withdrawable** | Ending one screen's session stops that screen and leaves a sibling running. **Unverified today — see risks** |

---

## Phases

1. **The accounts and the scope** — realm configuration, one account per fab.
2. **Prove the narrowness (US2)** — the account is refused writes and other fabs;
   operators are unchanged.
3. **Prove it stays up (US1)** — offline grant present; survives a shortened
   ceiling.
4. **Settle withdrawal (US3)** — test that one session can be ended
   independently. **If it cannot, US3 is re-scoped in the open rather than
   quietly reinterpreted**, as spec 049's US3 was.
5. **The record** — an ADR carrying the trade, and constitution §Availability
   updated to whatever is actually true when the work lands.

---

## Risks

**1. The withdrawal claim is unverified.** R4 reasons that offline sessions are
individually revocable, and *reasoning is what this feature exists to distrust*.
Phase 4 tests it. If one session cannot be ended alone, FR-009 is not met and
the record says so — and disabling the account would stop an entire fab's wall,
which is a hazard worth stating either way.

**2. A grant that never expires, and nothing that ends it.** This makes a
credential outlive sessions entirely. Nothing in the system cleans one up, and
an offline grant with no expiry accumulates silently. The plan does not solve
this; it requires the ADR to say what ends one, so the next reader is not
surprised.

**3. Claiming the target is met.** §Availability has now been half-met twice and
described as met once. When this lands, the entry should say what was actually
demonstrated — including that ten hours was shown on a shortened ceiling and
twenty screens were not exercised.

---

## Deferred, to be filed during this phase

- **Per-device identity** — an audit trail naming *screen 7* rather than the
  wall-display account. Already tracked; this feature narrows the gap without
  closing it.
- **Rotation of the wall-display credential.** It is declared by hand and will
  outlive whoever installed it. Out of scope here and a real liability.
