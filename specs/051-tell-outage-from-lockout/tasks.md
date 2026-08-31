# Tasks — 051 tell an outage from a lockout

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/kiosk-failure-states.md](./contracts/kiosk-failure-states.md)

Sixteen tasks. **Frontend only.** If a task seems to need C#, a realm change or a
new dependency, the scope was misjudged — stop and take it back through the gate.

---

## Do not

- **Do not touch the session ceiling.** Issues 1989 and 1992; 1989 is blocked on
  1992. A screen still drops out roughly twice a day for reasons this feature
  does not address, and pretending otherwise would claim a target it has not met.
- **Do not fold "session ended" into "shut out".** They are different verdicts
  with different screens. Conflating them makes a twice-daily ceiling drop-out
  announce itself as *revoked*, sending someone to re-commission hardware that
  needed a sign-in.
- **Do not touch** enrolment, rotation, per-device identity (issues 1987, 1988),
  crash recovery, or stream and wall-content outages. All adjacent, all separate,
  all filed.
- **Do not touch `management-web`.** A person is standing in front of it.
- **Do not move any of this to `apps/shared`.** There is one consumer. Moving it
  on the chance of a second is the speculative generality the guidelines forbid.
- **Do not widen the kiosk's scopes.** Unattended recovery must not be bought
  with authority.
- **Do not add a dependency.** `oidc-client-ts` already exports `ErrorResponse`
  and `ErrorTimeout`; research §R1 read the code that throws them.
- **Do not write C#.**
- **Do not write bare `#NNNN` issue numbers** in committed docs.

---

## Phase 1 — The classification rule *(the gate)*

- [ ] T001 Write the verdict type and the ordered rule in `apps/kiosk-web/src/app/identityFailure.ts`, following data-model.md §1 exactly. **The code check comes before any class check.** Three verdicts — `recoverable`, `refused`, `interactive` — and nothing else.
- [ ] T002 [P] Test that **`server_error` and `temporarily_unavailable` are `recoverable`**, in `apps/kiosk-web/src/app/identityFailure.test.ts`. **Its own task, not a case inside another one.** These arrive on an `ErrorResponse` from a provider that answered, so a rule branching on class before code marks them terminal and darkens a whole wall. Stopping a provider never reaches this branch, so a suite built only on that would pass with the ordering inverted.
- [ ] T003 [P] In `apps/kiosk-web/src/app/identityFailure.test.ts`, test the `refused` codes (`invalid_grant`, `invalid_client`, `unauthorized_client`, `access_denied`, `invalid_scope`) and that an **unrecognised code is `recoverable`** (FR-005). Assert the default explicitly, so removing it fails rather than silently reversing the asymmetry.
- [ ] T004 [P] In `apps/kiosk-web/src/app/identityFailure.test.ts`, test that a network throw and an `ErrorTimeout` are both `recoverable`, and that an error carrying **no code at all** does not reach `refused`.

**Checkpoint — this phase is a gate.** Both P1 stories rest on this rule, and a
wrong rule breaks them in *opposite* directions: an overloaded provider read as
terminal darkens the wall; a revoked screen read as recoverable retries forever
and never says it is revoked. **Nothing below is worth building until T001–T004
are green and mutation-checked.**

---

## Phase 2 — The retry schedule

- [ ] T005 Write the schedule in `apps/kiosk-web/src/app/retrySchedule.ts` — 2 s, doubling, **30 s ceiling**, ±30% jitter, unbounded — per data-model.md §2.
- [ ] T006 [P] **Test the ceiling against SC-001's two-minute budget** in `apps/kiosk-web/src/app/retrySchedule.test.ts`: assert that the worst-case interval (ceiling + maximum jitter) leaves room for a renewal round-trip inside two minutes. **The ceiling lives in one file and the criterion lives in a spec, and nothing else connects them** — without this, a later "let us be gentler, make it 90 seconds" breaks SC-001 with every test still green.
- [ ] T007 [P] In `apps/kiosk-web/src/app/retrySchedule.test.ts`, test that jitter actually spreads: repeated schedules from the same attempt number must not all return the same delay, and every delay must stay within the ±30% band.

---

## Phase 3 — US1: the wall comes back by itself *(P1)*

- [ ] T008 [US1] Classify the rejection in `apps/kiosk-web/src/app/useSessionExpiry.ts`, replacing the `.catch(() => false)` that currently destroys the cause. **The renewer must still resolve to the same boolean** — the gateway depends on it — and T011 asserts that separately.
- [ ] T009 [US1] Render the `recoverable` verdict as a reconnecting screen in `apps/kiosk-web/src/features/auth/ReconnectingScreen.tsx` and wire it into `App.tsx` ahead of the raw `auth.error` branch. It says recovery is automatic and no action is needed (FR-008), keeps a manual attempt available (FR-013), and **shows no library text as its headline** (FR-010) — "Failed to fetch" is what it says today.
- [ ] T010 [US1] End-to-end test in `e2e/kiosk-identity-recovery.spec.ts` that the wall returns **with nothing touched**: intercept the token endpoint to fail, confirm the reconnecting screen, release the interception, and assert the wall returns within the budget. **Must not click the manual retry button, and must assert it was not clicked** — that button already works today, so a test that presses it proves nothing about a wall nobody is standing at. **Must assert the interception actually fired**: `signinSilent` may run in a hidden iframe, and a pattern that matches nothing looks exactly like a test that passes.
- [ ] T011 [US1] In `apps/kiosk-web/src/app/useSessionExpiry.test.ts`, test that `setSessionRenewer`'s contract is unchanged — classifying a rejection must not change what the renewer resolves to. The gateway's 401 path depends on that boolean, and it is invisible from every screen this feature adds.

---

## Phase 4 — US2: a shut-out screen says so *(P1)*

- [ ] T012 [US2] Render the `refused` verdict in `apps/kiosk-web/src/features/auth/NotAuthorizedScreen.tsx` and **skip `signinRedirect` entirely** for that verdict in `useSessionExpiry.ts`. Skipping the redirect is the whole mechanism: it is what keeps the provider's login form off the wall (FR-007).
- [ ] T013 [US2] End-to-end test in `e2e/kiosk-identity-refused.spec.ts` **asserting absence**: with the token endpoint answering `invalid_grant`, the page must contain **no password input and no username input anywhere**, must state the screen is not authorized, and must not retry. **Absence is the assertion** — "the app stopped erroring" and "a nicer message appeared" both pass while the provider's login form is still on the wall, which is today's actual behaviour. Assert the interception fired.
- [ ] T014 [US2] In `apps/kiosk-web/src/App.test.tsx`, **regression-test that the `interactive` path is unchanged** — the existing "Session expired" screen still appears for a completed redirect that lands unauthenticated. `useSessionExpiry` is being edited, this path is the twice-daily ceiling drop-out, and it is the most likely thing to break by accident while nobody is looking at it. **Re-render through the same `AuthProvider`** — spec 049 lost a day to a test that re-rendered without it, remounting the component and resetting the very ref it was asserting on, which read as the guard being broken.

---

## Phase 5 — US3: screens do not arrive together *(P2, riding along)*

- [ ] T015 [US3] End-to-end test in `e2e/kiosk-identity-herd.spec.ts` that several screens recovering from one outage make their attempts at **measurably different times**. Assert the spread, not the presence of jitter in the source. Assert the interception fired in every context.

---

## Phase 6 — The record and verification

- [ ] T016 Write the ADR recording the classification rule, its ordering, the asymmetric default and the retry bound; then `verification.md`. **State what could not be done**: twenty screens, a wall over days, and a real network partition as distinct from an aborted request. Do **not** let the note imply this closes §Availability — the ceiling is the more frequent failure and is untouched.

---

## Mutations that must each kill a test

Run these before trusting any guard. **If a mutation survives, the test is
decoration.**

| # | Mutation | Must be killed by |
|---|---|---|
| 1 | Check the error's **class before its code** | T002 |
| 2 | Default an unrecognised code to `refused` | T003 |
| 3 | Remove the jitter | T007, T015 |
| 4 | Raise the ceiling past the SC-001 budget | T006 |
| 5 | Let a `refused` verdict fall through to `signinRedirect` | T013 |
| 6 | Drop the absence assertion on credential fields | T013 (the test must fail when the login form is present) |
| 7 | Make the renewer resolve to something else on rejection | T011 |
| 8 | Route the `interactive` case to the refused screen | T014 |

Mutation 1 is the one to run first. It is the defect that darkens a fab, and it
passes every test that only ever stops the provider.

---

## Dependencies

```
T001 ──▶ T002 ─┐
       ▶ T003 ─┤  Phase 1 (GATE)
       ▶ T004 ─┘
          │
          ▼
T005 ──▶ T006, T007            Phase 2
          │
   ┌──────┴──────────┬──────────────┐
   ▼                 ▼              ▼
T008 ─▶ T009 ─▶ T010   T012 ─▶ T013 ─▶ T014    T015
   └─▶ T011            US2  (P1)               US3
   US1  (P1)
          └──────────┬──────────────┘
                     ▼
                   T016
```

**Phase 1 gates everything.** US1 and US2 are independent of each other once the
rule and the schedule exist. US3 needs only the schedule.

---

## Parallel opportunities

- **T002, T003, T004 are genuinely parallel** — one rule, three disjoint groups
  of cases, separate assertions in the same new file.
- **T006 and T007 are parallel.**
- **US1 and US2 are parallel** once Phase 2 lands, and they touch different
  components; only `useSessionExpiry.ts` and `App.tsx` are shared, so T008 and
  T012 are sequential against each other.
- **T015 is parallel with everything in Phases 3 and 4.**

---

## Implementation strategy

**Phase 1 first, and it is a gate rather than a phase.** The rule is four small
functions and it decides whether the other eleven tasks are building the right
thing. Get it wrong and both P1 stories are wrong in opposite directions.

**The smallest change that works.** One line currently destroys the cause —
`.catch(() => false)`. Everything else follows from keeping it. This is a
behaviour fix, not a refactor of the auth stack; the four existing auth states in
`App.tsx` keep their order and their meaning, and one new branch is inserted
ahead of the raw `auth.error` case.

**The feature issue is on Project #13** — Phase 3's gate is satisfied. Issue 1990
was added by hand during Phase 1, and its premise was corrected there in the
open, since what was filed does not describe what the system does.

**No coverage gate applies and none should be cited.** ADR-0065's thresholds
cover Domain, Application and Shared assemblies; none is touched. That is not a
reason to test less, only a reason not to offer a coverage number as evidence.

---

## Three things most likely to go wrong

1. **The rule branches on class before code.** An `ErrorResponse` means the
   provider *answered*, not that it refused permanently — `server_error` is an
   overloaded identity service, the single most likely real outage on a fab.
   Getting this backwards turns that outage into a wall of screens announcing
   they have been revoked, and **it passes every test that induces failure by
   stopping the provider**. T002 exists only for this, and mutation 1 is the
   first to run.

2. **A route interception silently matches nothing.** `signinSilent` may run in a
   hidden iframe. A test that intercepts nothing behaves exactly like a test that
   passes, and it would report this feature working while none of it ran. Spec
   050 shipped this defect in a different disguise — posting to a hardcoded host
   while tokens came from the proxied endpoint — and it took a deliberate control
   to catch. Every interception here asserts it fired.

3. **US2 gets asserted on presence rather than absence.** "A nicer message
   appears" is true while the provider's login form is still on the wall, which
   is the actual defect. The claim is that **no credential field exists
   anywhere**, and only an absence assertion can carry it.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| A recoverable failure retries | T010 | the screen reading "Reconnecting" |
| The wall returns untouched | T010, nothing clicked | the manual button working, which it already does |
| A refused screen shows no prompt | T013, asserting **absence** | the app not erroring |
| An overloaded provider is recoverable | **T002, stubbing `server_error`** | stopping the provider, a different branch entirely |
| The ceiling respects the recovery budget | T006 | the ceiling looking small |
| Screens do not arrive together | T015, comparing times | jitter existing in the source |
| The ceiling drop-out still behaves | T014 | not having meant to change it |
| **A wall stays up over days** | **nothing** | seconds of one screen |
| **Twenty screens** | **nothing** | a handful |
| **A real network partition** | **nothing** | an aborted request, which is not DNS, TLS or a hang |
| **Anything in production** | **nothing** | there is no production deployment (ADR-0130) |

The last four rows are the honest ones. **Unattended operation is a property of
twenty screens over days, and every check here watches one screen for seconds.**
