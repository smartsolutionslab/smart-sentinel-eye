# Plan 136 — The figure is from CI

**Spec:** `specs/136-the-figure-is-from-ci/spec.md`
**Issue:** #2148
**ADRs:** 0037, 0144, 0036, 0139, 0100, 0117

## Bounded context and layers

**None.** This feature touches no bounded context, no Domain, Application,
Infrastructure or Api project, and introduces no entity, value object, invariant,
aggregate, command, query or handler. The boundary rules of §Architecture
(NetArchTest, no cross-context references, `Shared.Contracts` only) are not engaged
because no project reference changes.

Stating that plainly is the plan's first job. A plan.md that invents a context for a
records correction is how a two-file change acquires a migration.

## Messaging

No domain event, no integration event, no `Shared.Contracts` message, no Wolverine
handler, no outbox row. Nothing is published.

## What is actually changed

Six files, in two groups.

### Group A — the test's own remarks (one file, comments only)

`tests/Integration.Tests/Identity/NFR002_MqttConnectAuthTests.cs`

The `<remarks>` block at `:79-90` already carries spec 123's three off-CI readings and
their ≈ 2× arithmetic. A **CI paragraph is added beside it**, not instead of it, holding
the sampled run figures, the two margin floors and the ≈ 14× (p50) / ≈ 15× (p99)
environment ratio — and one sentence distinguishing the 50 ms wall-clock ceiling from
NFR-002's 5 ms auth-overhead SLO (ADR-0100).

This is the `ResolvedTextReachesItsFabTests.cs:121-131` shape the issue body asked for:
threshold, observation and arithmetic together, at the point of enforcement.

**Constraint, load-bearing:** the diff in this file must be **entirely inside comment
tokens**. `P50BudgetMilliseconds = 15` (`:51`) and `P99CeilingMilliseconds = 50` (`:55`)
do not move, the `BudgetsApplyHere` expression (`:92-93`) does not move, the early
return (`:130-132`) does not move, and neither assertion (`:138`, `:141`) moves. #2141
prohibits the bulk-fix and the issue body prohibits widening; with a 4.22× sampled
margin floor there is no motive either.

**Watch the 300 LOC/file metric limit (ADR-0084).** The file is 9 495 bytes today;
count the lines before and after and keep the added paragraph tight. If it would breach,
the CI figures go in `verification.md` and the remarks carry a two-line summary plus the
cite — the limit is not negotiated for prose.

### Group B — the four records (five files, markdown only)

| file | edit |
|---|---|
| `specs/021-transactional-outbox/verification.md:212-221` | Rewrite the CONNECT→CONNACK sentence: an off-CI reading, budgets not enforced, so neither "breached" nor "passed" describes it. Cite #2148 and the CI figure. |
| `specs/087-which-assertions-cannot-fail/census.md:177` | Margin row → sampled CI observation, floor 4.22×, **COMFORTABLE**. Keep the off-CI figure in the row as the *environment that does not enforce*, so the row itself teaches the distinction. |
| `specs/087-which-assertions-cannot-fail/census.md` §2d item 7 | The p99 is no longer one of the nine UNKNOWNs. Strike it from the enumeration and say where the number now lives — do not silently renumber the other eight. |
| `specs/087-which-assertions-cannot-fail/spec.md:166,168,346` | The same margin row; remove the p99 from the "never recorded" row; mark **F14 void** with the reason. Coordinates updated 2026-09-12 — planned as `:164,165,330`, of which `:165` was never the right row (`:164`/`:166` were), and all three then shifted by this spec's own annotations. |
| `specs/087-which-assertions-cannot-fail/tasks.md:134` | F14's restatement, same correction (planned as `:132`; shifted by this spec's F13 annotation). |

**F14 is marked void, not deleted.** A finding that is removed leaves the next reader no
way to know it was examined, and #2148 exists precisely because a record was read without
its provenance. The census is a record of what was found; the correction belongs in it.

**Precedent for editing delivered specs in place:** `specs/087-*` has been amended seven
times since it landed (`4a514e81`, `c3d5cbfe`, `7c6fa7e1`, `2a84c552`, `8725b8ee`,
`e441d5a8`, `55db1d08`), and `specs/021-*/verification.md` once already —
`697db54e docs(021): correct three documents that asserted the opposite`. This is the
house way; no new convention is being introduced.

## Phase 4a — the colour, and what it can honestly assert

**Behaviour-preserving.** Every executable line in the diff is a comment. There is no
new behaviour, so ADR-0139's red does not apply; constitution §Testing's other obligation
does — the covering test is captured **green before** the change and must pass
**unmodified after**.

The covering test is `NFR002_MqttConnectAuthTests` itself. Characterisation here is
literal and cheap: **the forty CI trx figures already captured are the "before"**, taken
from runs of the unmodified file. The "after" is this branch's own CI run, which must
produce a comparable figure with the same `(budgets enforced)` suffix and the same two
thresholds.

**What phase 4a must not do.** It must not add a test asserting that a markdown file
contains a string. That proves prose was typed, not that a figure is true, and it breaks
on rewording — it is the anti-pattern this session has flagged repeatedly. The claims in
Group B are verified by reading, by the reviewer, at phase 6.

**What phase 4a genuinely gets**, therefore, is one assertion and it is a real one: the
thresholds and the gate are byte-identical before and after. `git diff` restricted to
non-comment lines in the `.cs` file must be **empty**. That is mechanically checkable,
it is the exact property #2141 cares about, and it does not go stale.

## Risks

| risk | mitigation |
|---|---|
| The CI figures go stale — artifact retention is 14 days | Run ids **and** figures are written into the tree, not linked |
| A reader conflates the 1.99–12.97 ms CI wall-clock p99 with NFR-002's 5 ms auth SLO | An explicit sentence in the remarks and in the census row; called out in spec.md as the next instance of this issue's own trap — which `docs/adr/0100-…:237-239` and two spec-008 records already spring |
| A range quoted from too few runs is falsified by the spec's own recipe | Sample **every** green run in a multi-day window (40), state the sample size, and phrase the claim as a floor rather than a constant |
| Renumbering §2d's nine UNKNOWNs breaks cites elsewhere | Strike item 7 in place; do not renumber. `grep -rn '2d' specs/087-*` before editing |
| The remarks push the file past 300 LOC (ADR-0084) | Count first; overflow goes to `verification.md` with a cite |

## Gate

Phase 2 gate: this plan introduces no architecture, no ADR-worthy decision, and no
deviation from the constitution. **No new ADR is required**, which matters under
ADR-0144 — an ADR would block the autonomous lane.
