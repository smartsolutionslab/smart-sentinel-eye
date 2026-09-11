# Spec 136 — The figure is from CI, and it was already in the artifact

**Issue:** #2148 — *The 17.58 ms MQTT figure is an off-CI reading, and two records call it a breach*
**Branch:** `docs/2148-a-figure-read-where-the-budget-does-not-apply`
**ADRs:** 0037 (phases and gates), 0144 (the autonomous lane), 0036 (smallest change),
0139 / constitution §Testing (the two colours of phase 4a), 0100 (the go-auth broker
that NFR-002 is about), 0086 (no `Co-Authored-By`), 0028 (GitFlow)

## The issue arrived refuted, and the refutation was itself incomplete

The body says `NFR002_MqttConnectAuthTests` asserts p50 ≤ 15 ms while the observed
figure is 17.58 ms — *"an assertion that has already gone red and nothing reports it."*

Comment 1 (2026-09-07) refuted the premise: 17.58 ms came from a **dev box**, where
`BudgetsApplyHere` is false and the test reports without enforcing. It re-scoped the
issue to two record corrections and recommended the verdict **UNKNOWN**, because the CI
figure was not in hand — only four green runs bounding it below 15 ms.

Comment 2 (2026-09-10) added three fresh off-CI readings from spec 123: p50 33.2 / 27.4
/ 32.3 ms and p99 102.0 / 102.8 / 83.9 ms. Twice what spec 021 recorded, and the p99
breached too — which the body never mentions.

**Phase 1 of this spec settled it.** The CI figure was not missing. It has been sitting
in every integration run's uploaded artifact since the CI pipeline first landed
(`f085cd3e`), because `ci.yml:179` already passes `--logger
"trx;LogFileName=integration.trx"` and the next step uploads it with `if: always()`.
Comment 1 proposed adding a flag that was already there.

## The measurement

Four green `ci.yml` runs on `develop`, artifact `integration-test-results`, the test's
own unconditional `output.WriteLine` line read verbatim out of `integration.trx`:

| run | date | p50 | p99 | max | gate |
|---|---|---|---|---|---|
| 34631985515 | 2026-09-11 18:12 | **1.98 ms** | **8.88 ms** | 8.94 ms | budgets enforced |
| 34607694151 | 2026-09-11 14:01 | **2.21 ms** | **4.53 ms** | 6.26 ms | budgets enforced |
| 34589148935 | 2026-09-11 10:25 | **2.29 ms** | **6.72 ms** | 11.50 ms | budgets enforced |
| 34573183563 | 2026-09-11 07:10 | **2.11 ms** | **4.84 ms** | 10.40 ms | budgets enforced |

Every line ends `(budgets enforced)` — the CI branch of the ternary at
`NFR002_MqttConnectAuthTests.cs:128`, which is the test stating for itself that
`GITHUB_ACTIONS` was `true` and that the assertions at `:138` and `:141` ran.

- **p50 1.98–2.29 ms against 15 ms → margin 6.6×–7.6×**
- **p99 4.53–8.88 ms against 50 ms → margin 5.6×–11.0×**

Four runs, because a single measurement after machine churn reads like a regression and
one reading is not a figure.

**So the honest verdict is neither "has gone red" nor UNKNOWN. It is COMFORTABLE, with
a measured figure** — the same class the census gives `ResolvedTextReachesItsFabTests`
at 6.6×. Comment 1's recommended UNKNOWN was correct given what it had; it is
superseded by a number that costs nothing to obtain.

## What the two environments actually differ by

| | p50 | p99 |
|---|---|---|
| CI (Linux, native Docker) | 1.98–2.29 ms | 4.53–8.88 ms |
| dev box (Windows, Docker Desktop VM hop) | 27.4–33.2 ms | 83.9–102.8 ms |
| ratio | **≈ 14×** | **≈ 14×** |

#1905's gate is not merely defensible, it is **understated**. The reason it exists has
been asserted qualitatively ("a different and slower path"); this is the first time the
repository can say by how much. That ratio is the single most useful thing this issue
produces, and neither the body nor either comment contains it.

## The trap this issue is named after, and the next instance of it

The title is *a figure read where the budget does not apply*. There is a second live
instance of the same misreading, sitting one paragraph away:

ADR-0100 and spec 008 set **NFR-002 at ≤ 5 ms p99 per device-connect** — an
**auth-overhead** SLO on **production hardware**. The test's 50 ms is a wall-clock
gross-regression ceiling covering TCP + MQTT handshake through the container host
proxy, which the class remarks say explicitly. The measured CI wall-clock p99 is now
**4.53–8.88 ms** — numerically straddling 5 ms. A reader who has both numbers on screen
and not the distinction between them will conclude NFR-002 is marginal. It is not; the
two figures measure different things. Writing the CI figure down without writing that
down manufactures the next #2148.

## User stories

### US-1 (P1) — A reader of any of the five records gets the CI figure and the reason

**As** someone auditing a latency budget, **I want** every record of this test's margin
to carry the CI figure it is actually enforced against, **so that** I do not re-open
#2148 from a dev-box reading.

This is the whole slice. It is independently shippable and independently observable:
open each changed file and the claim is either there or it is not.

## The five records, not two

Comment 1 named two. `grep -rn '17\.58'` finds four, and a fifth record classes the
p99 as never-recorded:

| # | file:line | what it says now | why it is wrong |
|---|---|---|---|
| R1 | `specs/021-transactional-outbox/verification.md:212-215` | "the MQTT `CONNECT→CONNACK` p50 **breached** its 15 ms budget (17.58 ms) … and passed after" | Off-CI, where the assertion is inert. It could neither breach nor pass. |
| R2 | `specs/087-which-assertions-cannot-fail/census.md:177` | margin table: `17.58 ms`, `0.85×`, `TIGHT — has gone red` | Compares a CI threshold to a dev-box observation |
| R3 | `specs/087-which-assertions-cannot-fail/census.md` §2d item 7 | p99 ceiling listed among the nine UNKNOWNs: "the remarks assert a *relation* to it without a number" | The relation now has two numbers — off-CI (spec 123) and CI (this spec) |
| R4 | `specs/087-which-assertions-cannot-fail/spec.md:164` and `:165` | the same margin row, plus p99 in the "never recorded → UNKNOWN" row | Same two defects as R2 and R3 |
| R5 | `specs/087-which-assertions-cannot-fail/spec.md:330` + `tasks.md:132` | finding **F14**: "the inverse defect — threshold *below* observation, has gone red" | The finding itself is void; no inversion exists |

R1's *"and passed after"* is a detail neither comment caught, and it sharpens the case:
off-CI the assertion cannot fail **or** pass, so both halves of that sentence describe a
reading, not a gate. It is the clearest single example of the misreading.

## What is already done, and must not be redone

Spec 123 (#2149) **already** discharged the issue body's step 3 — *"write the observed
figure down next to the threshold, in the shape `ResolvedTextReachesItsFabTests.cs`
uses."* The three off-CI readings and their ≈ 2× arithmetic are in the test's own
`<remarks>` at `NFR002_MqttConnectAuthTests.cs:79-90`, citing #2149. That work is not
repeated here; the CI column is **added beside it**.

Spec 123 also established, by grepping its own run's trx, that these lines reach the
uploaded artifact. This spec's four runs are the independent confirmation of that
finding on `develop`, and the first use of it to answer a question.

## Acceptance scenarios

Gherkin, on the records — there is no request/response surface here.

```gherkin
Scenario: the CI figure is beside the threshold it is enforced against
  Given a reader opens tests/Integration.Tests/Identity/NFR002_MqttConnectAuthTests.cs
  When they read the remarks above BudgetsApplyHere
  Then they find the CI p50/p99 range, the off-CI range, the ratio between them,
   and the run ids the CI figures came from
```

```gherkin
Scenario: the conflicting record no longer reads as a breach
  Given a reader opens specs/021-transactional-outbox/verification.md at section 6
  When they read the CONNECT→CONNACK sentence
  Then it says the figure is an off-CI reading with budgets not enforced
   And it does not use the words "breached" or "passed" of an inert assertion
```

```gherkin
Scenario: the census verdict matches the census's own rule
  Given the census §2c rule "where none is written down the answer is UNKNOWN"
  When a reader reads the NFR002 p50 row
  Then it shows the CI observation, a margin of 6.6x-7.6x and the class COMFORTABLE
   And F14 is marked void with the reason, rather than deleted
```

```gherkin
Scenario: the auth SLO is not confused with the transport ceiling
  Given the CI wall-clock p99 of 4.53-8.88 ms is now recorded
  When a reader compares it to NFR-002's 5 ms p99
  Then the record states that the two measure different things
   And cites ADR-0100 and spec 008 for the auth-overhead SLO
```

```gherkin
Scenario: no assertion moved
  Given the diff of this spec
  When the changed lines in any .cs file are inspected
  Then every one is inside a comment, and P50BudgetMilliseconds is still 15
   And P99CeilingMilliseconds is still 50
```

There is no auth scenario and no bad-request scenario: nothing here has a caller, a
scope or a request body. Saying so is more honest than manufacturing one.

## Independent end-to-end test procedure

Reproducible by anyone with `gh`, no stack and no build:

```sh
gh run list --workflow ci.yml --branch develop --status success --limit 4 \
  --json databaseId -q '.[].databaseId' | while read -r R; do
  gh run download "$R" -n integration-test-results -D "/tmp/ci-$R"
  grep -rohE 'CONNECT.CONNACK over [^<]{0,160}' "/tmp/ci-$R"
done
```

Each line must read `(budgets enforced)` and carry a p50 well under 15 ms. Artifact
retention is **14 days** (`ci.yml`), so a reader after 2026-09-25 will need four fresh
runs rather than these four — which is exactly why the run ids and figures are written
into the tree rather than left as a link.

## Locked tech choices

None are exercised. No new dependency, no framework, no runtime resource, no value
object, no contract. `ci.yml` is **not** changed: the flag comment 1 proposed is already
present.

## Latency budget impact

**N/A to constitution §IV.** MQTT device CONNECT is not one of the six legs of `event
arrival → overlay rendered`; it is NFR-002, whose authority is `specs/008-*/spec.md:425`
and ADR-0100. No leg's budget, state or measurement changes, so §VII's dashboard rule
(ADR-0117) is not engaged.

## Out of scope, argued

**Surfacing the figure in the CI job's own console log.** Spec 123 identified this and
declared it out of scope: *"closing that would mean changing what CI runs."* It stays
out. The figure is obtainable today from the artifact, which is what this spec
demonstrates; adding `--logger "console;verbosity=detailed"` would change every
integration run's output to solve a problem a `gh run download` already solves. If
someone wants it in the log, that is a separate issue against `ci.yml` with its own cost
argument.

**Re-examining the other twelve census rows.** #2141's census is amended here only where
it names this test.

**Anything that moves a threshold.** The issue body forbids it, #2141 forbids the
bulk-fix, and with a 6.6× CI margin there is nothing to move.

**A guard test asserting these markdown files contain these strings.** Such a test
proves a string was typed, not that a figure is true, and it goes stale the moment the
prose is reworded. See tasks.md for what phase 4a does instead.
