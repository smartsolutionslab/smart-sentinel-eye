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
(`f085cd3e`, where the flag was at `:142`), because `ci.yml:180` already passes
`--logger "trx;LogFileName=integration.trx"` and the next step uploads it with
`if: always()`. Comment 1 proposed adding a flag that was already there.

## The measurement

**Forty** green `ci.yml` runs on `develop` — every green run in the window
**2026-09-07 05:07Z → 2026-09-11 21:04Z** — artifact `integration-test-results`, the
test's own unconditional `output.WriteLine` line read verbatim out of each run's
`integration.trx`. All forty run ids with their figures are in
[`verification.md`](verification.md); the distribution is:

| | min | max | mean | margin floor | margin ceiling |
|---|---|---|---|---|---|
| p50 vs 15 ms | **1.03 ms** (34519576651) | **3.55 ms** (34475397402) | 2.18 ms | **4.22×** | 14.5× |
| p99 vs 50 ms | **1.99 ms** (34519576651) | **12.97 ms** (34475397402) | 6.50 ms | **3.85×** | 25.1× |

All forty lines end `(budgets enforced)` — the CI branch of the ternary at
`NFR002_MqttConnectAuthTests.cs:159`, which is the test stating for itself that
`GITHUB_ACTIONS` was `true` and that the assertions at `:169` and `:172` ran. Not one
of the forty breached either threshold.

**Read that as a sampled range with a floor, not as a constant of the environment.**
Forty runs over five days say the margin did not fall below 4.22× on the median or
3.85× on the p99 *in this window*; they do not promise the next run stays there. A
reader who re-runs the recipe and gets a figure outside the range has found news, not a
contradiction — which is the whole difference between quoting a measurement and quoting
it with the qualification that makes it true.

**An earlier draft of this spec said 1.98–2.29 ms on a four-run sample, and it was
wrong in exactly the way #2148 is about.** Four runs are enough to refute "has gone
red"; they are not enough to state a range. The recipe below, re-run days later, returns
runs outside that band — 34586548968 at p50 2.40 ms is *more recent* than one of the
four the draft cited. A claim about an environment needs a sample that can survive its
own reproduction instructions.

**So the honest verdict is neither "has gone red" nor UNKNOWN. It is COMFORTABLE, with
a measured figure** — a 4.22× sampled floor sits above the ~3.9× the census already
classes COMFORTABLE (`PostgresConnectionBudgetIntegrationTests` count) and well clear of
the 2.4× it classes TIGHT. Comment 1's recommended UNKNOWN was correct given what it
had; it is superseded by a number that costs nothing to obtain.

## What the two environments actually differ by

| | p50 | p99 |
|---|---|---|
| CI (Linux, native Docker), 40 runs | 1.03–3.55 ms, mean **2.18** | 1.99–12.97 ms, mean **6.50** |
| dev box (Windows, Docker Desktop VM hop), 3 runs | 27.4–33.2 ms, mean **30.97** | 83.9–102.8 ms, mean **96.23** |
| ratio of means | **≈ 14×** (14.2) | **≈ 15×** (14.8) |

The ratio is taken on the means, because the ranges overlap nothing and a ratio of
endpoints would be four different numbers depending which endpoints you pick.

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
**1.99–12.97 ms** — numerically bracketing 5 ms. A reader who has both numbers on screen
and not the distinction between them will conclude NFR-002 is marginal. It is not; the
two figures measure different things. Writing the CI figure down without writing that
down manufactures the next #2148.

**And three records spring that trap already.** `docs/adr/0100-mosquitto-go-auth.md:237-239`,
`specs/008-identity/plan.md:421-423` and `specs/008-identity/tasks.md:234` each say
`NFR002_MqttConnectAuthTests` *asserts p99 ≤ 5 ms* — against *Testcontainers*. Both
halves are wrong: the assertions are p50 ≤ 15 ms and p99 ≤ 50 ms, and the fixture is
Aspire (ADR-0103). A reader following this spec's own ADR-0100 cite lands on that
sentence with 1.99–12.97 ms in hand and concludes exactly what this spec says must not
be concluded. The two `specs/008-identity` records are corrected here; the ADR is not —
see *Out of scope, argued*.

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
| R1 | `specs/021-transactional-outbox/verification.md:212-221` | "the MQTT `CONNECT→CONNACK` p50 **breached** its 15 ms budget (17.58 ms) … and passed after" | Off-CI, where the assertion is inert. It could neither breach nor pass. |
| R2 | `specs/087-which-assertions-cannot-fail/census.md:177` | margin table: `17.58 ms`, `0.85×`, `TIGHT — has gone red` | Compares a CI threshold to a dev-box observation |
| R3 | `specs/087-which-assertions-cannot-fail/census.md` §2d item 7 | p99 ceiling listed among the nine UNKNOWNs: "the remarks assert a *relation* to it without a number" | The relation now has two numbers — off-CI (spec 123) and CI (this spec) |
| R4 | `specs/087-which-assertions-cannot-fail/spec.md:166` and `:168` | the same margin row, plus p99 in the "never recorded → UNKNOWN" row | Same two defects as R2 and R3 |
| R5 | `specs/087-which-assertions-cannot-fail/spec.md:346` + `tasks.md:134` | finding **F14**: "the inverse defect — threshold *below* observation, has gone red" | The finding itself is void; no inversion exists |

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
  Then it shows the sampled CI observation, a margin floor of 4.22x and the class
   COMFORTABLE
   And F14 is marked void with the reason, rather than deleted
```

```gherkin
Scenario: the auth SLO is not confused with the transport ceiling
  Given the sampled CI wall-clock p99 of 1.99-12.97 ms is now recorded
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
gh run list --workflow ci.yml --branch develop --status success --limit 40 \
  --json databaseId -q '.[].databaseId' | while read -r R; do
  rm -rf /tmp/ci-run
  gh run download "$R" -n integration-test-results -D /tmp/ci-run || continue
  printf '%s ' "$R"
  grep -rohE 'CONNECT.CONNACK over [^<]{0,160}' /tmp/ci-run | head -1
done
```

Each line must read `(budgets enforced)` and carry a p50 under 15 ms and a p99 under
50 ms. **`--limit 40`, not `--limit 4`, and the difference is the point of this spec:**
four runs can refute "has gone red", but a *range* quoted from four runs is a claim the
same recipe falsifies a week later. Each artifact is ~35 MB, so the loop deletes as it
goes; the whole sweep takes a few minutes.

Artifact retention is **14 days** (`ci.yml:188`), so a reader after 2026-09-25 will need
a fresh sweep rather than these forty runs — which is exactly why the run ids and
figures are written into the tree (`verification.md`) rather than left as a link.

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
bulk-fix, and with a sampled margin floor of 4.22× (p50) and 3.85× (p99) there is
nothing to move.

**A guard test asserting these markdown files contain these strings.** Such a test
proves a string was typed, not that a figure is true, and it goes stale the moment the
prose is reworded. See tasks.md for what phase 4a does instead.

**Correcting `docs/adr/0100-mosquitto-go-auth.md:237-239`.** The ADR's *Performance
Validation* section says `NFR002_MqttConnectAuthTests` (spec 008 T088) *"asserts p99
≤ 5 ms over a warm 100-cycle connect test against a Testcontainers Keycloak +
Mosquitto."* Both halves are false of the test as it stands: it asserts **p50 ≤ 15 ms**
(`NFR002_MqttConnectAuthTests.cs:51`) and **p99 ≤ 50 ms** (`:55`), and it runs against
the **Aspire fixture**, Testcontainers having been rejected by ADR-0103. The 5 ms is
NFR-002's production-hardware auth-overhead SLO, which this test does not gate.

This is a live instance of the very misreading #2148 records, and this spec cites
ADR-0100 in four places as the authority for *"the CI p99 is not NFR-002's 5 ms"* — so
a reader following the cite lands on a sentence that contradicts it.

**It is recorded here and not fixed, because amending an ADR is one of the three
outcomes the autonomous lane may not produce (ADR-0144).** It needs its own issue and a
human. Mitigated in the meantime by the sentence added to the test's own `<remarks>`,
which is what a reader arriving from either direction meets first. The two ordinary
records carrying the same wrong claim — `specs/008-identity/plan.md:421-423` and
`specs/008-identity/tasks.md:234` — *are* corrected here, since those are records, not
decisions.

## A note on line cites in this spec's own artefacts

`spec.md`, `plan.md` and `tasks.md` were written against
`NFR002_MqttConnectAuthTests.cs` as it stood **before** this spec added 31 comment
lines to its `<remarks>`. Where those artefacts name a pre-change line, the current
coordinates are:

| what | cited as | now at |
|---|---|---|
| `P50BudgetMilliseconds = 15` | `:51` | `:51` (unmoved) |
| `P99CeilingMilliseconds = 50` | `:55` | `:55` (unmoved) |
| `BudgetsApplyHere` expression | `:92-93` | `:123-124` |
| the `(budgets enforced)` ternary | `:128` | `:159` |
| the early return | `:130-132` | `:161-164` |
| the p50 / p99 assertions | `:138`, `:141` | `:169`, `:172` |

Spec 087 has the precedent for this note — `2a84c552 docs(specs): 087 — census's
line-shift note corrects the wrong cites`. A cite that has silently shifted is the
cheapest way for a checker to conclude the evidence is not there.
