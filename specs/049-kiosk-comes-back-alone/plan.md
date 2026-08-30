# Implementation Plan — 049 a wall comes back on its own

**Branch**: `049-kiosk-comes-back-alone` | **Spec**: [spec.md](./spec.md)
**Research**: [research.md](./research.md)

---

## Summary

Build the **middle shape**: the device acquires a long-lived grant once and
keeps it across restarts. That addresses **both** causes research found — a
restart losing every token, and the ten-hour ceiling ending the session while
the screen is running.

**Defer** the shape the current decision names — a credential held outside the
page — because nothing runs on a kiosk device except a browser, so it means
inventing a device runtime rather than relocating a secret (R2).

**Amend the record first.** Two things in it cannot be met as written, and the
plan's first phase is saying so rather than quietly satisfying them.

---

## Technical Context

| | |
|---|---|
| **Language** | TypeScript, React 19 |
| **Packages** | `apps/kiosk-web` (auth and recovery), realm configuration |
| **Backend** | **Unchanged.** Enrolment already mints per-device credentials; this feature does not use them, and says why |
| **Tests** | Vitest; Playwright against the real stack |
| **Unknowns** | None blocking. Every Phase 0 question was closed by reading source or querying the running realm |

---

## Constitution Check

| Principle | Assessment |
|---|---|
| **§Availability — 20 kiosks reboot unattended** | **The principle this feature serves.** It also corrects the entry, which says the gap is "smaller than it looks": the credentials exist but cannot live where the code that would use them runs (R3) |
| **§IV latency budget** | Not on the path. Coming back is bounded by hardware boot time this feature does not touch. No latency claimed |
| **§VIII security — untrusted input, least privilege** | **The tension in this feature.** Authority is unchanged (view-only, one fab); what changes is how long a device-held grant lasts. Treated as an explicit trade in the ADR, not a footnote |
| **Smallest possible change** | A device runtime is deferred, not built. Enrolment, revocation and operator binding stay out |
| **No speculative generality** | No device-management framework. One mechanism, for one story |

**Gate: PASS, with one thing to state plainly.** This feature **widens a
security exposure** — a grant that survives a restart lives longer than one that
does not. That is the cost of the target the constitution sets, and the ADR
records it as a decision rather than letting it arrive as a side effect.

---

## Design

### Phase 1 is the record, and it is the gate

Three corrections, and none of them is bookkeeping:

1. **The kiosk-auth decision** says the kiosk does *not* use the interactive
   library it in fact uses, and sketches a device credential in a "secure local
   store" a web page does not have. Amend it the way the last two features
   amended theirs: keep the original legible, record what was built and why the
   original could not be.
2. **FR-004 cannot be met by any browser-only design** (R3). It is refined, in
   writing, to: *the delivered bundle carries no credential; what a device
   acquires is its own, independently revocable, and no broader than view-only.*
   Weaker than written, and stated rather than assumed.
3. **§Availability's "smaller than it looks"** is too optimistic and is
   corrected — the credentials exist and cannot be used from a page.

**Nothing is built before this lands.** The last two features both discovered a
locked decision contradicting the system, and both treated the amendment as a
gate. This one contradicts the system in the same way.

### Phase 2 — surviving a restart (cause A)

Tokens live in storage that does not outlive the browser process. A restart
therefore loses everything regardless of any server setting. Moving them to
storage that persists is what makes a reboot recoverable at all.

**This is the change that widens exposure**, and it is the one the ADR must
cover: a stolen powered-off device yields nothing today and yields a grant
afterwards.

### Phase 3 — surviving the ceiling (cause B)

A restart-proof token still dies on the ten-hour clock. Escaping it means asking
for the long-lived grant type the realm already defines and the kiosk client
currently **cannot request** — it is neither a default nor an optional scope on
that client, verified by query. A small realm change, and it is the only part
that touches configuration outside the app.

### Phase 4 — what a screen shows when it cannot come back (US3)

Three states that must be distinguishable: *never enrolled*, *no longer
trusted*, *cannot reach the identity service*. The third retries by itself
because it resolves by itself; the first two do not, and a person standing in
front of the screen needs to know which they are looking at.

---

## "Done" — stated before any code

| Story | Done when |
|---|---|
| **US1 restart** | A kiosk with **no tokens at all** in storage, and a browser profile carrying no sign-in cookie, resumes its wall without input. Starting from a signed-in state proves nothing — the same trap as a resolved-at-first-render fixture in the last feature |
| **US2 continuous** | A kiosk survives its session ceiling without a prompt. Demonstrated by **shortening the ceiling on a test realm**, because nothing in CI runs for ten hours — and the note must say that is what was done |
| **US3 failure states** | Each of the three renders differently, and a revoked device stops showing cameras without waiting for a restart |
| **Twenty at once** | Twenty kiosks recover simultaneously. **Not provable in CI**; carried as an explicit gap unless a run against the real stack is possible |

---

## Risks

**1. The exposure trade gets made silently.** The whole feature is "make a
credential last longer". If the ADR records the mechanism and not the cost, the
next reader inherits a weakened posture with no note saying it was chosen. The
ADR must state what a stolen device now yields.

**2. A test that starts signed in.** Every check must begin from empty storage.
This is the third feature running where the natural fixture is the one that
hides the defect, and the previous two both shipped something because of it.

**3. Time and number are untestable here.** Ten hours and twenty screens are
both beyond CI. The temptation is to narrow the claim to what a fixture covers
and call the target met. The verification note states what was not done.

---

## Deferred, to be filed as issues during this phase

- **A device runtime holding a real device credential.** The only shape matching
  the decision as written, and a subsystem rather than a feature (R2). Filing it
  keeps the option visible rather than lost behind an amendment.
- **True device identity in the audit trail.** The grant belongs to whoever
  authorised the screen, not the screen. The system already mints per-device
  identities that would have solved this and cannot be used from a page.
- **Whether device credentials should keep being minted while nothing consumes
  them** — a standing credential-management liability, and a question about that
  endpoint rather than about recovery.
