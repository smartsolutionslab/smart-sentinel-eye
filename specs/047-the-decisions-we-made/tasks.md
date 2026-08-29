# Tasks: The decisions we made, against the system we built

**Feature**: `047-the-decisions-we-made` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

**18 tasks across six phases.** There is no product code. The work is ~60–80
claims checked against the repository, a record of what was found, an ADR, and a
guard.

**The unit of audit is the claim, not the decision.** Decision 005 makes three
claims and they take three different verdicts. A task that produces one verdict
per row will be wrong and will look finished.

**The likeliest failure is an audit that stops when it gets boring.** Twenty-seven
decisions, most probably fine, and every "holds" feels like wasted effort next to
finding another StreamKeeper. An audit recording only its discoveries **cannot be
told apart from one that gave up**, so rows that hold are recorded as checked,
with evidence, exactly like rows that fail.

---

## Do not

- **Do not verdict per decision.** Split every row into its claims first. 005 is
  three claims (RTSP, ONVIF, vendor adapters) with three different answers.
- **Do not record a conclusion as evidence.** Write the command and its output.
  *"Vendor adapters are missing"* is the same kind of unsupported assertion as the
  claim being audited — the defect reproduced in a new document.
- **Do not write "not built" after one search.** One grep answers *is this string
  present*, which is a different question. Search for the **name**, then for the
  **job**, and record both.
- **Do not re-litigate architecture.** Recording that AEL exists is not endorsing
  it. Every *"is this the right choice"* becomes an issue, never a verdict.
- **Do not leave a divergence as prose.** Each one ends in an ADR or an issue.
  A note that does neither is how the current situation arose.
- **Do not audit the other ~100 ADRs.** Out of scope. Note in passing that
  ADR-0117's leg table and ADR-0026's abandoned stack suggest the problem is not
  confined to ADR-0000 — but that is a later feature.
- **Do not build anything a decision describes.** If `IRuleEngine` is absent, this
  feature records that and files an issue. It does not write the interface.
- **Do not edit ADR-0000 or the constitution before the ADR lands.** Governance
  requires the ADR first, and this feature is about respecting the record.
- **Do not write `#NNNN`-style bare issue numbers** in committed docs — the
  automation closes a merely-mentioned issue on merge.

---

## Phase 1: The record to write into

- [x] T001 Create `specs/047-the-decisions-we-made/audit.md` with the table shape from [data-model.md](./data-model.md): decision, claim, verdict, evidence, disposition. One row **per claim**. Include the four verdicts and the rule for each at the top, so an auditor does not have to hold them in their head.
- [x] T002 Seed it with the three worked examples from [research.md](./research.md) — 005, 012, 013 — as the reference for what a good entry looks like. **They are the calibration**: same component, three different verdicts, and each shows the two-search rule producing a different answer.

**Checkpoint**: there is somewhere to write, and an example of the standard.

---

## Phase 2: The audit (US1) — the bulk of the work

**Each of T003–T006 follows the same rules**, repeated in each because they are
the ones that get dropped when the work gets repetitive:

- split the row into claims, verdict each **claim**;
- record the **command and its output**, never a conclusion;
- **two searches** before any *not built* — the name, then the job — both recorded;
- **record the rows that hold**, with evidence;
- use **unverifiable here** for deployment, hardware or topology claims rather
  than guessing — 013 is the pattern.

- [x] T003 [US1] Audit decisions **001–009** into `audit.md`. Known ground: 005 diverges partly (RTSP holds, ONVIF absent, vendor adapters absent) and 008's kiosk auth claim is contradicted by the realm — the client is public and uses the authorization-code flow, not `client_credentials`. 009's Marten claim reads as an unrealised intention rather than a falsehood; say which and why.
- [x] T004 [US1] Audit decisions **010–018** into `audit.md`. Known ground: 012 is not built (no shard, no coordinator, no ownership map — the only `failover` in the AppHost is an unrelated comment); 013 is unverifiable here; 016 **holds** and its count is recorded. 011's GPU transcode is an unrealised intention.
- [x] T005 [US1] Audit decisions **019–027** into `audit.md`. Known ground: 019 diverges — the language is AEL (`AelLexer`, `AelParser`, `AelInterpreter`), not CEL; 020 and 023 name interfaces that are absent; 025's Helm claim is partial (`deploy/helm` holds one chart, for Mosquitto). **014 and 021 are already amended** by specs 045 and 046 — record them as such rather than re-auditing.
- [x] T006 [US1] Audit constitution **§IX**'s four rows into `audit.md`. **Check each against accepted ADRs as well as code** — its observability row still says *"Both Aspire + Grafana"*, which ADR-0118 abandoned. Nothing in the code changed; an ADR did, and a code-only check misses it entirely. The rule-engine row also carries the CEL error from 019.

**Checkpoint**: every claim has a verdict and evidence a reader can re-run.
**US1 is satisfied here** — the record is checkable even before it is corrected.

---

## Phase 3: Sort the divergences (US1)

- [ ] T007 For every non-holding claim, assign a disposition — **legitimise** (an ADR amends the decision to match a system that is right) or **correct** (an issue proposes changing a system that has not caught up). **Where it is not obvious, write the issue**: an unnecessary issue gets closed, while an ADR that legitimises a mistake makes it policy.
- [ ] T008 [P] Raise the **correct** issues. Expected: `IRuleEngine` and `IAuthorizationDecisionPoint`, which §IX mandates *"in v1"* and which do not exist — a live constitutional gap rather than stale prose. Add each to Project #13.
- [ ] T009 [P] Record the **legitimise** candidates for the ADR. Expected: AEL instead of CEL. **Recording it is not endorsing it** — if anyone thinks CEL was right, that is an issue, not a verdict.

**Checkpoint**: nothing is left as prose alone.

---

## Phase 4: Correct the record (US1, US2)

- [ ] T010 Write `docs/adr/0130-the-founding-decisions-audited.md`: what was audited, the method, the verdicts that changed the record, and the dispositions. It **cites** `audit.md` rather than restating it — the ADR is for decisions, the audit is the evidence, and summarising the evidence into the ADR loses what makes it checkable.
- [ ] T011 [US2] Annotate ADR-0000's rows with their status, **keeping the original decision text legible** — the pattern rows 026 and 014 already use (`**Amended by ADR-NNNN.** … Originally: …`). The record of what was decided must not be overwritten by what happened.
- [ ] T012 [US2] Correct constitution **§IX**, including the observability row ADR-0118 already settled. Bump the version and add an amendment-history entry.
- [ ] T013 [P] [US2] Fix CLAUDE.md where it states a decision more strongly than ADR-0000 does — the stack table's Marten entry is the known case. A summary that overclaims relative to its source is the same defect at one remove.

**Checkpoint**: US2 satisfied — unbuilt reads as unbuilt, and no principle contradicts an accepted ADR.

---

## Phase 5: Guard it (US3)

- [ ] T014 [US3] Create `tests/Architecture.Tests/FoundingDecisionRecordTests.cs`, following `LatencyLegRecordTests` — the file that **caught spec 045 changing §IV**, and the only reason that change was noticed. Guard the claims that were **corrected**, not all 27 rows.
- [ ] T015 [P] [US3] Each assertion carries a message saying **what is wrong and why it matters**, not that a string is missing. `LatencyLegRecordTests` is the model: its messages explain that a wrong row exempts code from a constitutional rule.
- [ ] T016 [US3] Test that a **legitimate** update still passes — a row edited because the system genuinely changed must not fail. A guard that blocks real updates gets deleted within a month and takes the useful part with it. **This is the test that stops the guard being the next thing to rot.**

**Checkpoint**: corrections cannot drift back, and progress is not obstructed.

---

## Phase 6: Verify

- [ ] T017 Re-run the full backend test suite the way CI does, not a subset. *(Spec 045 shipped a green subset and CI caught an architecture test it had never run.)*
- [ ] T018 **A person re-checks a sample of the audit**, per [quickstart.md](./quickstart.md) §2. **Pick boring rows deliberately** — the ones marked *holds* — because the question is whether the passes were really done, not whether the discoveries were interesting. For each: do the recorded commands still return what the audit says; does every *not built* carry a second search; does every *diverges* name what the system does instead. **Record which rows were re-checked, and any that did not reproduce.**

---

## Dependencies

```
T001 ─▶ T002 ──▶ T003, T004, T005, T006     (the record, then the audit)
                        │
                        ▼
                      T007 ─▶ T008, T009      (verdicts, then dispositions)
                        │
                        ▼
                      T010 ─▶ T011, T012, T013   (ADR first — governance)
                        │
                        ▼
                      T014 ─▶ T015, T016
                        │
                        ▼
                      T017 ─▶ T018
```

**T002 before the audit.** Auditing without a worked example is how the vocabulary
drifts between decision 003 and decision 023.

**T007 before T010.** The ADR records dispositions; they have to exist first.

**T010 before T011/T012.** Governance: ADR-0000 and the constitution do not change
without an ADR, and this feature of all features should not break that rule.

## Parallel opportunities

- **T003, T004, T005, T006** — four disjoint ranges of `audit.md`. Genuinely
  parallel if the file is sectioned first, which T001 does.
- **T008 and T009** — issues and ADR notes, different outputs.
- **T013** — CLAUDE.md, touched by nothing else.
- **T015** — messages, alongside T014's assertions.

## Implementation strategy

**The audit is the deliverable; everything else is bookkeeping on it.** T003–T006
are perhaps three-quarters of the work and none of it is clever.

**Commit per range.** One wrong verdict is then revertible without unpicking the
rest, and a gap is visible in the history rather than hidden in one large commit.

**Stop after Phase 2 if you must.** A recorded, checkable audit with no
corrections yet is still worth more than what exists today. The corrections are
the payoff, but the evidence is the asset.

---

## Three things most likely to go wrong

1. **The audit stops being an audit.** By decision 020 the pattern is familiar,
   the interesting failures are behind you, and "holds" starts getting written
   without a command run. Nothing in the artefact distinguishes that from
   thorough work — which is precisely the defect being corrected, committed
   again. T018 is the only real control, and it exists because the automated
   checks cannot see it.

2. **"Not built" where "diverges" is true.** StreamKeeper shows how confidently
   one grep produces a wrong answer: the name is absent, and part of the job is
   done by MediaMTX and `StreamDistribution`. The two dispositions are opposite —
   an issue to build something that already exists is worse than no issue.

3. **The guard is written to be thorough and gets deleted.** Assert all 27 rows
   verbatim and the first legitimate edit fails the build; someone deletes the
   file rather than fight it, and the corrections lose their protection silently.
   T016 exists to prove the guard permits progress.

---

## What the checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| A corrected claim cannot silently revert | T014, T016 | — |
| The guard permits legitimate updates | T016 | — |
| No principle contradicts an accepted ADR | T006, T012 | any code-only check |
| Each divergence has an owner | T007, T008, T009 | — |
| The recorded evidence reproduces | T018 — a person | every test above |
| **The audit was actually performed on the boring rows** | **T018 — a person** | **everything above** |

The last row is the honest one. **Nothing automated can distinguish a thorough
audit from a plausible one**, because the artefact of both is prose asserting
that someone looked. Only re-running the evidence tells them apart, and only on
the rows nobody found interesting.
