# Tasks — 054 divide the span the decision is waiting on

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/the-comparison.md](./contracts/the-comparison.md) · **Runbook**: [quickstart.md](./quickstart.md)

Thirteen tasks. **The apparatus already exists** — this feature supplies an
environment, a driver, and an extraction so both runs execute one implementation.

**US2 has no phase of its own.** "A run shape nobody can quietly break" is
delivered by Phase 1's shape constant and Phase 2's conditions block. It is a
property of how the other phases are built, not a separate slice.

---

## Do not

- **Do not boot a stack from the driver, ever.** A figure labelled "run mode"
  that came from a fixture is worse than no figure, and the failure is silent.
- **Do not reimplement the division.** `IngestAttribution`, `ClockOffsetProbe`
  and `AttributionVerdict` are reused unchanged. A second copy is a second thing
  to get wrong.
- **Do not build endpoint discovery on the Vite dev server**, the way
  `scripts/wait-for-e2e-stack.sh` resolves the gateway. Fine for a smoke check,
  wrong for a measurement.
- **Do not change NFR-001's budget.**
- **Do not propose a lever**, however obvious the comparison makes one.
- **Do not touch the audit pipeline.** Nothing here is supposed to get faster.
- **Do not change what Development logs at.** That trade-off belongs to whoever
  works in it daily.
- **Do not un-exclude the measurement from CI.** It needs a stack CI does not run.
- **Do not write bare `#NNNN` issue numbers** in committed docs.

---

## Phase 1 — The extraction *(the gate)*

- [x] T001 Add `IngestRunShape` in `tests/Integration.Tests/AuditObservability/IngestRunShape.cs` per data-model §1: generator, warm-up 100, measured 1000, writers 50, target 100 ev/s, tolerance ±15%. **One definition, so drift between the two runs is not expressible.**
- [x] T002 Add `IngestRunConditions` in `tests/Integration.Tests/AuditObservability/IngestRunConditions.cs` per data-model §2: environment, endpoint actually connected to, intended and achieved rate, logging level, switch state, rows measured and missing.
- [x] T003 Extract the run body and the attribution SQL from `NFR001_AuditIngestLatencyTests.cs` into `tests/Integration.Tests/AuditObservability/IngestSpanMeasurement.cs`, taking an authenticated client, a DbContext factory, a connection string and an `IngestRunShape`. **Behaviour-preserving: it returns the bands, the offset, the verdict and the conditions, and asserts nothing.**
- [x] T004 Rewire `NFR001_AuditIngestLatencyTests.Where_the_ingest_span_goes` to call the extraction and keep its own assertions, in `tests/Integration.Tests/AuditObservability/NFR001_AuditIngestLatencyTests.cs`.

**Checkpoint — this is the gate, and it is a REGRESSION gate.** Re-run the
fixture measurement and check its output against the figures recorded in
ADR-0135: the same shape of breakdown, per-row residual **0.000 ms**, every row
stamped, achieved rate within tolerance. **Not "it compiles" and not "the tests
pass"** — an extraction that changed behaviour makes every number after it
incomparable with the very figures it exists to be compared against.

---

## Phase 2 — The run-mode driver

- [x] T005 Add `RunModeIngestAttributionTests` in `tests/Integration.Tests/AuditObservability/RunModeIngestAttributionTests.cs` **with no `[Collection]` attribute**, reading the system-variables address, the Keycloak address and the audit-db connection string from the environment.
- [x] T006 Mint the access token against the **proxied** Keycloak address in `tests/Integration.Tests/AuditObservability/RunModeIngestAttributionTests.cs`, by the same password grant the fixture uses. **A token minted against the container's mapped port is rejected by every service** — the issuer will not match — and the 401s do not name the cause.
- [x] T007 [P] **Test that absent or unreachable configuration is a refusal**, in `tests/Integration.Tests/AuditObservability/`: the run fails naming what it could not reach, and **no stack is started**. Contract C4, and the most important guard here.
- [x] T008 [P] **Test that the run-mode class cannot acquire the fixture**, in `tests/Integration.Tests/AuditObservability/`: assert by reflection that it carries no `[Collection]` attribute. The mechanism is real but silent, so it is asserted rather than relied on.
- [x] T009 [P] **Test that both runs read one shape**, in `tests/Integration.Tests/AuditObservability/`: assert the fixture run and the run-mode run reference the same `IngestRunShape` values. Two constants that happen to match satisfy a reader and not this test.

---

## Phase 3 — US1: the measurement *(P1)*

- [x] T010 [US1] Emit the conditions block **before any assertion that can fail**, in `tests/Integration.Tests/AuditObservability/RunModeIngestAttributionTests.cs`, and report the **endpoint actually connected to**. A refused run must still say what it was refused for, and the endpoint is the only guard against attributing a figure to the wrong stack.
- [x] T011 [US1] Apply the same guards the fixture run applies, in `tests/Integration.Tests/AuditObservability/RunModeIngestAttributionTests.cs`: every row stamped, per-row residual zero, achieved rate within ±15%, logging not Debug or Trace, clock verdict established. **A run that cannot meet its conditions reports rather than publishes** (contract C3).
- [x] T012 [US1] Take the measurement per [quickstart.md](./quickstart.md): **at least three runs**, at `Warning`, switch on, spread recorded, achieved rate beside intended. Check the reported endpoint against the stack started, and the audit store's growth against the measured count — **no automated check can do this**.

---

## Phase 4 — US3: the record *(P2)*

- [x] T013 [US3] Write the ADR and `verification.md` in `docs/adr/` and `specs/054-divide-the-recorded-85ms/`: both breakdowns in one table, every difference between the runs other than the environment nil or named, the spread across ≥3 runs, and **the write leg and requirement-span floor marked NOT ESTABLISHED in the same table as the established figures** — run mode has the same host/container split. **State what was measured and stop.** No recommendation, no lever, no changed budget; a reviewer should push back if it does otherwise.

---

## Mutations that must each kill a test

| # | Mutation | Must be killed by |
|---|---|---|
| 1 | Give the run-mode class `[Collection(AspireCollection.Name)]` so it boots a fixture | T008 |
| 2 | Let absent configuration fall back to a default endpoint instead of refusing | T007 |
| 3 | Give the two runs separate shape constants | T009 |
| 4 | Move the conditions block after the assertions | T010 |
| 5 | Drop the reported endpoint from the conditions | T010 |
| 6 | Let a run below the rate tolerance report its breakdown anyway | T011 |

**Mutation 1 is the one to run first.** It is the only one whose survival
produces a *plausible* wrong answer — a complete, well-formed breakdown labelled
"run mode" and taken from a fixture — rather than an obvious failure.

---

## Dependencies

```
T001, T002 ─▶ T003 ─▶ T004        Phase 1 (GATE)
                        │
                        ▼
              T005 ─▶ T006
                 └──▶ T007, T008, T009    Phase 2
                        │
                        ▼
              T010 ─▶ T011 ─▶ T012        Phase 3 (US1)
                                │
                                ▼
                              T013        Phase 4 (US3)
```

**Phase 1 gates everything**: the comparison is the deliverable, and it is void if
the fixture side moved. T003 is strictly after T001 and T002 — it consumes both.

---

## Parallel opportunities

- **T001 and T002 are parallel** — two records, different files, neither depends
  on the other.
- **T007, T008 and T009 are parallel** — three separate guards on the driver,
  independent of each other once T005 exists.
- **T003 and T004 are strictly sequential**: an extraction, then its caller.
- **T010 → T011 → T012 are sequential**: report, then guard, then run.

---

## Implementation strategy

**Phase 1 first, and it is a gate rather than a phase.** It touches merged,
verified code whose figures are recorded. That is what makes the regression check
possible — re-run and compare, rather than assert and hope — and also what makes
skipping it expensive.

**The deliverable is a comparison.** Not a number: two numbers that legitimately
belong in one table. Everything about the run shape being shared, and the
conditions being reported, exists to make that legitimacy structural rather than
asserted.

**No coverage gate is live.** No Domain or Application code is touched, so
ADR-0065's thresholds do not apply. Stated because the last two specs got this
wrong in both directions — one claiming a gate that did not apply, one missing one
that did.

**The measurement stays excluded from CI.** It needs a stack CI does not run, and
its sibling is excluded for the same reason.

**The feature's issue must be added to Project #13 by hand.** `/speckit-tasks`
adds nothing to the board.

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```

---

## Three things most likely to go wrong

1. **The extraction changes the fixture's figures.** It is the natural place to
   tidy while moving code, and tidying is what changes behaviour. The recorded
   figures make this checkable rather than a matter of confidence — so check,
   before anything downstream is measured.

2. **The driver measures the wrong stack and nobody notices.** An endpoint is an
   endpoint. A leftover fixture stack, or yesterday's AppHost, answers just as
   readily. T008 stops the in-process fallback; the reported endpoint is what
   stops the rest, and only if a human reads it.

3. **A single pair of runs becomes an effect size.** At `Warning` the same
   configuration has given 169.8, 173.7 and 244.4 ev/s. This repository has
   already published an overstated figure for exactly this reason, and corrected
   it. Three runs, spread reported, and the asymmetry explained rather than
   averaged: **at Debug the logging is the bottleneck so the figure reproduces; at
   Warning the machine is, so it does not.**

---

## What the automated checks do and do not prove

| Claim | Proved by | Not proved by |
|---|---|---|
| The extraction preserved behaviour | T004's gate, against **recorded** figures | the suite going green |
| The driver cannot boot a stack | T008, asserting the absent attribute | the class looking right |
| Missing configuration refuses | T007, asserting the refusal | a default that happens to be unset |
| The two runs share a shape | T009 | two constants that match today |
| A refused run still explains itself | T010, ordering asserted | the block existing |
| The run met its conditions | T011 | the run completing |
| Where the run-mode span goes | T012, three runs with spread | one run |
| **That the stack measured was run mode** | **nothing — a human checks the reported endpoint** | the endpoint being configured |
| **The write leg** | **nothing — same host/container split as the fixture** | it being a small plausible number |
| **That the recorded 85 ms is reproduced** | **nothing — that figure came from an ad-hoc driver nobody kept** | the environment matching |
| **That any lever would help** | **nothing** | a dominant-looking part |
| **Anything about production** | **nothing** | there is no production deployment |

The last five rows are the honest ones. **The third from last is worth dwelling
on**: this feature measures run mode, which is where the 85 ms came from — but the
driver that produced it was never committed, so "the same conditions" is a claim
about the environment, not about the load.
