# Tasks — 049 a wall comes back on its own

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md)

Fourteen tasks. Small in code and large in consequence: the feature makes a
credential last longer, which is a security decision wearing a usability
feature's clothes.

---

## Do not

- **Do not touch enrolment, revocation-as-a-workflow, rotation, or
  operator→kiosk binding.** All adjacent, all separate, all filed. This feature
  is one screen coming back on its own.
- **Do not use the per-device confidential credentials.** They exist, they are
  the right shape, and **the whole finding of this feature is that they cannot
  live in a page.** Reaching for them is reaching for a published secret (issue
  1988).
- **Do not build a device runtime.** Nothing runs on a kiosk but a browser, and
  changing that is a subsystem, not a task (issue 1987).
- **Do not widen the kiosk's scopes.** Unattended recovery must not be bought
  with more authority. T009 exists to prove it was not.
- **Do not write C#.** Enrolment is unchanged and the services are unchanged. If
  a server change seems necessary, the scope was misjudged — stop and take it
  back through the gate.
- **Do not write bare `#NNNN` issue numbers** in committed docs — the automation
  closes merely-mentioned issues on merge. Write "issue NNNN".

---

## Phase 1 — The record *(a gate, not paperwork)*

- [ ] T001 Write the ADR amending the kiosk-auth decision. It says the kiosk does **not** use the interactive library it in fact uses, and puts the device credential in a "secure local store" a web page does not have. Amend it the way the last two features amended theirs: **keep the original legible**, record what was built, and say plainly why the original could not be.
- [ ] T002 **Record the cost in the same ADR, not only the mechanism.** Use `data-model.md`'s exposure table: a powered-off stolen kiosk yields **nothing today and a usable grant afterwards**. Everything else is unchanged — view-only, one fab, independently revocable. An ADR that states what was built and omits what it gives up hands the next reader a weakened posture with no note saying anyone chose it.
- [ ] T003 Refine FR-004 in writing. *No credential readable from the page* **cannot be met by any browser-only design, including this one** — the app already keeps tokens in the browser. The promise becomes: the delivered bundle carries no credential; what a device acquires is its own, independently revocable, and no broader than view-only. **Weaker than written, and said rather than assumed** — the same move as the label-matching withdrawal two features ago.
- [ ] T004 Correct constitution §Availability. It currently says the gap is "smaller than it looks" because the credentials exist and the app does not use them. That is too optimistic: they cannot live where the code that would use them runs.
- [ ] T005 [P] Guard the corrected claims in `tests/Architecture.Tests/`, as a **consistency check** — code versus record, failing in either direction — not a text pin. A guard that pins prose blocks legitimate rewording and gets deleted within a month, taking the useful part with it. Follow the existing latency-leg and founding-decision guards.

**Checkpoint — this is a gate.** **Nothing below is built until this lands.**
The last two features each found a locked decision contradicting the system, and
both stopped for the amendment; this one contradicts it the same way. Building
first would mean the record describes a system nobody has and the code
implements a decision nobody made.

---

## Phase 2 — US1: surviving a restart

- [ ] T006 [US1] Keep the kiosk's tokens in storage that outlives the browser process, in `apps/kiosk-web/src/app/auth.ts`. Today they sit in process-bound storage, so **a restart loses everything unconditionally** and no server setting can help.
- [ ] T007 [US1] Recover on boot: with tokens present, the kiosk returns to its wall **without a redirect and without a prompt**.
- [ ] T008 [US1] Tests in `apps/kiosk-web/src/app/`. **Every case starts from empty storage** — a check that begins signed in proves nothing about coming back, and this is the third feature running where the convenient fixture is the one that hides the defect: label text seeded at mount two features ago, a camera list resolved at first render in the last one, both shipping a defect because of it. Cover: nothing stored → the prompt appears; a stored grant → the wall returns with no prompt.
- [ ] T009 [US1] **Prove authority did not change.** Assert the kiosk's scopes are identical before and after. Unattended recovery must not be bought with a broader grant, and a reviewer should be able to confirm it in one diff rather than by reasoning about a flow.

**Checkpoint**: a rebooted screen comes back. It still drops out on the ceiling.

---

## Phase 3 — US2: surviving the ten-hour ceiling

- [ ] T010 [US2] Request the long-lived grant, and permit the kiosk client to receive it. The realm already defines the grant type; the kiosk client can currently ask for it **neither by default nor optionally** — verified by query, so this is a small configuration change alongside the app change.
- [ ] T011 [US2] Test it **with the ceiling shortened on a test realm**, because nothing in CI runs for ten hours. The task, the test's own comment and the verification note must all say this **demonstrates the mechanism and not the production configuration** — a green test here must never be read as a wall having been watched for ten hours.

**Checkpoint**: both failures addressed. US2 is the more frequent one — twice a day per screen, on a wall that never reboots.

---

## Phase 4 — US3: what a screen shows when it cannot come back

- [ ] T012 [US3] Distinguish the three states in `apps/kiosk-web`: **never enrolled**, **no longer trusted**, **cannot reach the identity service**. The third retries by itself because it clears by itself; the first two must not pretend to, because a screen retrying a withdrawn credential forever tells whoever watches it the problem is transient when it is not.
- [ ] T013 [US3] **Revocation, tested in both directions** — withdrawing one screen's grant stops that screen **and leaves the others running**. This is the load-bearing security test, not a nicety: per-device revocability is precisely what makes the widened exposure survivable, and an assertion in one direction alone would pass against a mechanism that stops every screen or none.

---

## Phase 5 — Verify

- [ ] T014 Run the frontend job the way CI runs it — format, lint, typecheck, test — and **read the exit codes**, because counting matching output lines reported a false pass in the last feature. Then write `verification.md` stating plainly **what could not be done**: nothing in CI reboots twenty screens, and nothing runs for ten real hours. If a run against the real stack is not possible, say so rather than narrowing the claim to what the fixtures covered.

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003 ─▶ T004 ─▶ T005        Phase 1 — GATE
                                  │
                 ┌────────────────┼────────────────┐
                 ▼                ▼                ▼
     T006 ─▶ T007 ─▶ T008    T010 ─▶ T011    T012 ─▶ T013
              │  US1              US2              US3
              └─▶ T009
                                  └──────┬─────────┘
                                         ▼
                                       T014
```

**US1, US2 and US3 are independent of one another** and all depend on the gate.

---

## Parallel opportunities

- **T005** is a different file from T001–T004 and can be written alongside them.
- **US1, US2 and US3 are genuinely parallel** once the gate clears — different
  failures, different code paths.
- **T006 and T007 are not parallel**: one mechanism through one file.

---

## Implementation strategy

**The gate is the work.** Phases 2–4 are modest; Phase 1 is where this feature
either records a security trade honestly or buries it. Do not treat the ADR as
the write-up of a decision already made in code — it *is* the decision.

**US1 and US2 are both P1 and neither substitutes for the other.** US1 fixes the
dramatic failure, US2 the frequent one. Shipping US1 alone would let the record
claim the target is met while a wall still drops out twice a day.

**Phase 3's gate is already satisfied** — issue 1976 is on Project #13.
`/speckit-tasks` adds nothing to the board on its own.

**No C# means no coverage gate.** That is not a reason to test less; it is a
reason not to cite the gate as evidence.

---

## Three things most likely to go wrong

1. **The exposure trade gets made silently.** The entire feature is "make a
   credential last longer". If T002 slips to "the ADR describes the mechanism",
   a future reader inherits a weaker posture and no record that anyone weighed
   it. This is the one to protect.

2. **A test that starts signed in.** Every assertion about coming back is
   vacuous if storage is pre-populated. Two consecutive features shipped a
   defect for exactly this reason, and in both the natural fixture was the
   broken-case-hiding one. Assume this one will be too.

3. **A green suite implying a wall was watched.** Ten hours and twenty screens
   are both beyond CI. The temptation is to let a shortened-ceiling test stand
   in for the real thing silently. T011 and T014 both say so out loud because
   one place saying it is not enough.

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| A restarted kiosk returns to its wall unattended | T008 | any test starting from stored tokens |
| The kiosk's authority is unchanged | T009 | reasoning about the flow |
| The session ceiling no longer ends the wall | T011, ceiling shortened | a short test, which passes with the defect present |
| The three failure states are distinguishable | T012 | — |
| One screen's grant can be withdrawn alone | T013 | a one-directional assertion |
| The record no longer claims what cannot be built | T005 | — |
| **That twenty screens recover together** | **nothing in CI** | every test above |
| **That a wall survives ten real hours** | **nothing in CI** | a shortened ceiling, which shows the mechanism only |
| **That the exposure trade was the right one** | **nothing — it is a judgement** | recorded in the ADR, not tested |

The last three rows are the honest ones. **Time, number and judgement** are the
blind spots, and they are named here in advance rather than discovered in review.
