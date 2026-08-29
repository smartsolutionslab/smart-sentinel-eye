# ADR-0130: The founding decisions, audited against the system they describe

**Status:** **Accepted**
**Date:** 2026-08-29
**Amends:** `docs/adr/0000-initial-decisions.md` rows 008, 009, 017, 019, 023, 027; constitution §IX, §Stack, §AppHost and §Retention
**Relates to:** ADR-0117, ADR-0118, ADR-0128, spec 040, spec 045, spec 046, spec 047, issue 1969

## Context

`0000-initial-decisions.md` holds decisions 001–027, marked *Locked*, and is the
most-cited document in the repository. **Nobody had ever checked its claims
against the code.**

This came to light three times, by accident, each time when a feature tried to
build on a decision and could not:

| Spec | What it found |
|---|---|
| 040 | Two latency legs recorded as unbuilt while their code ran on every kiosk |
| 045 | ADR-014 assigns work to a component that does not exist |
| 046 | ADR-021 needs two things that were never built |

**Three coincidences is a pattern**, so spec 047 audited all of it.

### Why a wrong record is not merely untidy

ADR-0117 made the record **decide whether obligations exist**: §VII binds
*implemented* legs, so *"a leg not yet built is not yet subject"*. A row that
wrongly says unbuilt **silently exempts working code from a constitutional
rule**. The same mechanism applies to §IX, which mandates strategy interfaces
*"in v1"*.

### What the audit found

**89 claims across 27 decisions, plus §IX's 4 rows.** Full evidence, per
claim, in `specs/047-the-decisions-we-made/audit.md`.

| Verdict | Count |
|---|---|
| Holds | 52 |
| Not built | 14 |
| Unverifiable here | 12 |
| Diverges | 7 |
| Partly | 3 |
| n/a | 1 |

**Fifty-two hold.** This is a correction, not a demolition — and a result that
condemned everything would itself have been suspect.

### The method, because the verdicts depend on it

**The unit of audit is the claim, not the decision.** Decision 005 makes three
claims and they take three different verdicts. Decisions 005, 012 and 013 all
assign work to *StreamKeeper*, whose name appears nowhere in the code — and their
verdicts are **diverges**, **not built** and **unverifiable here** respectively.
One verdict per row would have recorded them identically and been wrong twice.

**Absence required two searches**, both recorded: one for the *name*, one for the
*job* under any other name. A single grep answers "is this string present",
which is a different question.

**Evidence is the command and its output, never a conclusion.** The claims being
audited were themselves conclusions recorded without evidence; recording more
conclusions would have reproduced the defect in a new document.

## Decision

**1. The audit's verdicts are adopted**, and `audit.md` is the evidence of
record. This ADR states the corrections; it does not restate the evidence, which
would lose what makes it checkable.

**2. Six divergences are legitimised** — the system is defensible and the
decision was stale:

- **019 — the expression language is AEL, not CEL.** A complete hand-written
  lexer, parser and interpreter exists in `src/Automation/Application/Ael/`.
  Nothing is missing; the decided *name* is wrong. **This is not an endorsement
  of AEL over CEL** — if that choice is wrong, it is an issue against a working
  system, not a verdict this audit may make.
- **009 and the constitution — Prometheus is not the metrics stack.** ADR-0118
  already abandoned the Grafana/Prometheus stack and chose the Aspire dashboard
  as the single sink. Nothing needs building; **the record simply never followed
  an accepted ADR** — in four places (row 009, §Stack, §AppHost's resource list,
  and §Retention's 30-day/Thanos/Mimir policy).
- **008 — the kiosk *app* uses the authorization-code flow**, as a public client
  with a view-only scope. **The device-bound `client_credentials` design is
  built** — `EnrollKioskCommandHandler` mints a per-kiosk confidential client —
  **and the app does not use it** (issue 1976). *This entry was wrong in the
  first draft and is corrected here; see the audit's note on how.*
- **023 — authorization is by scopes and fab groups**, not the four named roles
  (`admin`, `operator`, `viewer`, `kiosk`). The realm defines two: `user` and
  `admin`. Authorization is enforced at every endpoint and works.
- **027 — two web apps**, `apps/kiosk-web` and `apps/management-web`, not one
  `apps/web/`. Split deliberately by ADR-0074, which the layout row predates.
- **017 — `integer` and `decimal` are one `Number` type.** **Only the merge is
  legitimised.** The absent `datetime` and `json` types are issue 1971.

**3. Twenty-six unbuilt claims are recorded as unbuilt**, and each has an owner
(issues 1970–1975, plus a comment on 1015). None is legitimised by silence.

**4. "Unverifiable here" is a verdict, not a gap.** Fourteen claims concern fab
deployment, network topology or v2 intent, and this repository cannot settle
them. Recording decision 013's VLAN split as *not built* would have been a false
statement about a fab nobody here can see. **The audit stopped deliberately
rather than ran out.**

**5. The original decision text stays legible.** Rows are annotated, never
overwritten — the pattern rows 026 and 014 already use. **The record of what was
decided must not be replaced by the record of what happened**; both are needed to
see how far apart they drifted.

**6. The corrections are guarded** by `FoundingDecisionRecordTests`, following
`LatencyLegRecordTests` — which caught spec 045 changing §IV and is the only
reason that change was noticed. **The guard covers corrected claims only.** A
guard asserting all 27 rows verbatim would fail on the first legitimate edit and
be deleted within a month, taking the useful part with it.

## Consequences

**Positive — the most-cited document can be trusted.** A reader can act on a row,
or see plainly that they cannot.

**Positive — §IX's failure is now visible.** Its purpose is that v2 lands without
breaking changes, and three of four rows defeated it. That is issue 1970's
subject rather than a discovery waiting for a fourth accidental finder.

**Positive — every divergence has an owner.** Six issues and this ADR. Nothing
was left as prose, which is how the audited situation arose.

**Negative — this is a snapshot.** The audit is true on 2026-08-29 and starts
decaying immediately. The guard slows that for corrected claims and does nothing
for the other 46; re-auditing is a judgement for a later feature.

**Negative — the scope was bounded, and the problem probably is not.** Only
ADR-0000 and §IX were audited. **ADR-0117's leg table and ADR-0026's abandoned
stack suggest the same drift exists among the other ~100 ADRs**, and this ADR
does not address them.

**Neutral — the audit does not re-decide architecture.** That AEL exists is
recorded, not endorsed. Where a divergence might be the wrong choice, it became
an issue; where it is plainly fine, it was legitimised. **Nobody re-litigated a
design inside a record-keeping exercise**, which would have turned a bounded
audit into an open-ended review.

## Alternatives Considered

**Fix the rows quietly, without an ADR — REJECTED.** Governance requires an ADR
to amend ADR-0000 or the constitution, and a feature about respecting the record
is the last one that should bypass it.

**Overwrite the stale rows with what is true — REJECTED.** It would erase the
evidence that the record drifted, which is the most useful thing the audit
produced. Rows 026 and 014 already show the better pattern.

**One verdict per decision — REJECTED**, and demonstrably wrong: decision 005's
three claims take three verdicts.

**Audit everything, all ~100 ADRs — REJECTED for now.** ADR-0000 and §IX are what
three features actually tripped over. Bounded work that finishes beats
comprehensive work that stalls, and the method here transfers.

**Re-decide the divergences while we are here — REJECTED.** Whether AEL was
better than CEL is a real question and a different one. Folding it in would have
made the audit unfinishable.

## Implementation Notes

- `specs/047-the-decisions-we-made/audit.md` — the evidence, claim by claim.
- Rows 008, 009, 017, 019, 023 and 027 annotated in ADR-0000.
- Constitution: §IX's four rows corrected; §Stack, §AppHost and §Retention's
  Prometheus claims corrected; version bumped.
- CLAUDE.md's stack table states Marten more strongly than decision 009 does —
  corrected, because a summary that overclaims relative to its source is the same
  defect at one remove.
- Issues 1970–1975 carry the unbuilt work; issue 1015 carries decision 025's
  ungenerated Helm charts.
