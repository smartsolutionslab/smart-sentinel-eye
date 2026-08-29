# Implementation Plan: The decisions we made, against the system we built

**Branch**: `047-the-decisions-we-made` | **Date**: 2026-08-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/047-the-decisions-we-made/spec.md`

## Summary

Check all 27 founding decisions and constitution §IX against the code, record a
verdict per claim with its evidence, correct the record by ADR, and guard the
corrections so they cannot drift back.

**No product code.** The output is documents and tests.

**Phase 0 settled the method, not the answer** ([research.md](./research.md)).
The important finding: **the unit of audit is the claim, not the decision** —
rows 005, 012 and 013 all name StreamKeeper and all three take different
verdicts. A method of one-verdict-per-row would have recorded them identically
and been wrong twice.

## Technical Context

**Language/Version**: Markdown for the record; C# (xUnit) for the guard

**Primary Dependencies**: none new

**Storage**: none

**Testing**: xUnit architecture tests, following `LatencyLegRecordTests`

**Target Platform**: n/a — repository documents

**Performance Goals**: n/a

**Constraints**: ADR-0000 and the constitution cannot change without an ADR
(governance); original decision text must stay legible (FR-007)

**Scale/Scope**: 27 decisions + 4 §IX rows; roughly 60–80 individual claims

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| Governance — amendments need an ADR | ✅ | FR-005; the ADR is Phase 3. |
| §IX forward-compat interfaces | ⚠️ **Two are absent** | `IRuleEngine`, `IAuthorizationDecisionPoint`. This feature **records** that; building them is out of scope (FR-008) and becomes an issue (FR-009). |
| §IV / §VII record integrity | ✅ **This is the point** | ADR-0117 made the record decide whether obligations apply. |
| Karpathy: smallest change | ✅ | Documents and a guard. No product code. |
| Karpathy: no speculative generality | ✅ | No audit framework, no tooling. A method and a table. |
| ADR-0030 commit conventions | ✅ | One commit per audited group, so a wrong verdict is revertible alone. |

**Gate result: PASS.** The §IX warning is the feature's subject, not a violation
it introduces.

## Project Structure

```text
specs/047-the-decisions-we-made/
├── spec.md, plan.md, research.md
├── data-model.md          the verdict vocabulary and evidence shape
├── quickstart.md          how to audit one claim, and how to check one
└── audit.md               NEW — the audit itself, claim by claim (the deliverable)

docs/adr/
  0130-the-founding-decisions-audited.md   NEW — corrections, by ADR

docs/adr/0000-initial-decisions.md         rows annotated with status
.specify/memory/constitution.md            §IX corrected; version bump

tests/Architecture.Tests/
  FoundingDecisionRecordTests.cs           NEW — guards the corrections
```

**Structure decision**: the audit lives in the spec folder as `audit.md`, not in
the ADR. The ADR records *decisions*; the audit is *evidence*, and it is long.
Keeping them apart means the ADR stays readable and the evidence stays complete —
and the ADR cites the audit rather than summarising it away.

## Phases

### Phase 1 — Audit the claims

For each of the 27 decisions and 4 §IX rows: split into claims, apply the
two-search rule, record verdict and evidence in `audit.md`.

- **Rows that hold are recorded as checked** (FR-004). An audit listing only
  failures is indistinguishable from one that stopped early, and this phase is
  where that temptation lives.
- **Evidence is the command and its result**, not a conclusion (research D2).
- Grouped into commits by decision range, so one wrong verdict can be reverted
  without unpicking the rest.

**Exit**: every claim carries a verdict and evidence a reader can re-run.

### Phase 2 — Sort the divergences

Every non-holding claim goes to exactly one of two places (FR-009):

- **An ADR** that makes it legitimate — the system is right, the decision was
  stale. *AEL instead of CEL is the candidate.*
- **An issue** proposing correction — the decision was right, the system has not
  caught up. *`IRuleEngine` and `IAuthorizationDecisionPoint` are the
  candidates: §IX mandates them "in v1".*

**Nothing lands as prose alone.** This phase is the one that turns an audit into
work rather than a document.

**This phase does not re-decide architecture** (FR-008). Choosing which of the
two places a divergence belongs in is a judgement, and where it is not obvious,
it becomes an issue — the cheaper mistake.

### Phase 3 — Correct the record

- `docs/adr/0130-the-founding-decisions-audited.md`: the corrections, citing
  `audit.md` for evidence, and stating the method so a later reader can extend
  it.
- Annotate ADR-0000's rows with status, **keeping the original text legible**
  (FR-007) — the pattern ADR-0118 and ADR-0128 both used.
- Correct §IX, including the observability row that ADR-0118 already settled.
- Constitution version bump and amendment-history entry.

### Phase 4 — Guard it

- `FoundingDecisionRecordTests.cs`, following `LatencyLegRecordTests` — which
  **caught spec 045 changing §IV**, and is the only reason that change was
  noticed.
- Guard the claims that were *corrected*, not every row. A test asserting all 27
  rows verbatim would fail on every legitimate edit and would be deleted within a
  month.
- Messages say what is wrong and why it matters (FR-011), not that a string is
  missing.

### Phase 5 — Verify

- Re-run a sample of recorded evidence and confirm the verdicts reproduce.
- Confirm the guard fails when a corrected claim is restored.
- Confirm no constitutional principle contradicts an accepted ADR (SC-003).

## Complexity Tracking

| Deviation | Why | Simpler alternative rejected because |
|---|---|---|
| A separate `audit.md` rather than putting it in the ADR | Evidence is long and ADRs are read for decisions | Summarising the evidence into the ADR loses exactly what makes the audit checkable |
| Guarding only corrected claims | A verbatim guard on 27 rows blocks legitimate edits | It would be deleted the first time it obstructed a real change, taking the useful part with it |

## Risks

1. **A partial audit that reads as complete.** Twenty-seven rows, and the
   failures are far more interesting than the passes. FR-004 and SC-001 are the
   controls; Phase 1's commit-per-group makes gaps visible.
2. **"Not built" recorded where "diverges" is true.** The StreamKeeper rows show
   how easily one grep produces a confident wrong answer. The two-search rule is
   the control, and the second search is recorded so a reader can judge it.
3. **The audit becomes an architecture review.** Every divergence invites a
   debate about whether the system or the decision is right. FR-008 forbids it;
   Phase 2 routes the debate to an issue instead.
4. **Guard rot.** A guard that obstructs legitimate updates gets deleted. FR-012
   and the narrow scope in Phase 4 are the mitigation, and it is a real risk
   rather than a theoretical one.
