# Implementation Plan: A plant-floor event reaches the things it is supposed to drive

**Branch**: `022-event-reaches-its-effects` | **Date**: 2026-08-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-event-reaches-its-effects/spec.md`

## Summary

Add the test that would have caught the break spec 021 shipped: publish a
plant-floor event over MQTT, and assert the **effects** — a variable's value
changed, a highlight arrived on the hub — with nothing asserted in between.

No production code changes. Phase 0 confirmed both things that could have
stopped this: a rule published mid-test really does become active (the publish
handler upserts the evaluator's cache), and the highlight really is observable
from outside (SignalR, with existing precedent in the integration suite). So
FR-010 holds and there is no finding to raise.

The work is one integration test class with three cases — the value effect, the
highlight effect, and a negative — plus the deliberate-break verification that
SC-002 demands, which is the part that distinguishes this from a test that
merely goes green.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: xUnit + Shouldly, the Aspire fixture (ADR-0103),
MQTTnet for ingress, `HubConnection` for the SignalR effect

**Storage**: PostgreSQL, read back through the SystemVariables API rather than
queried directly — the effect an operator sees, not the row underneath it

**Testing**: this feature *is* testing; no production behaviour changes

**Target Platform**: Linux containers via the Aspire fixture; must run on the CI
runner (FR-007)

**Project Type**: integration test spanning EventIngestion → Automation →
SystemVariables / LayoutComposition

**Performance Goals**: none introduced. SC-005 asks the path be *measured*
against the `event → overlay state ≤ 200 ms` leg and the measurement's limits
stated

**Constraints**: FR-004 — assert effects only; FR-010 — no production change;
FR-007 — routine build, not an exclusion

**Scale/Scope**: one test class, four contexts exercised, zero source files
touched

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1.*

| Principle | Assessment |
|---|---|
| **§I DDD, value objects** | No domain change. The test drives public interfaces only. |
| **§II Bounded contexts** | Exercises the boundaries rather than crossing them: everything the test does goes through an API, the broker, or a hub, which is precisely what makes it a test of the joins. |
| **§III Contracts versioned** | Nothing in `Shared.Contracts` changes. |
| **§IV Latency budget** | The path this covers *is* the `event → overlay state ≤ 200 ms` leg. Measured and reported with its caveats (SC-005), not asserted as a gate — a fixture running nine services on one host does not produce the number a fab would. |
| **§V Observability** | Unchanged. |
| **§IX No speculative generality** | One test class, no helpers built for hypothetical future cases. |
| **Governance** | No ADR affected, no amendment needed. |

**No exceptions required.** Notably this is the first feature in the recent run
that needs none — because it adds proof rather than behaviour.

## Project Structure

### Documentation (this feature)

```text
specs/022-event-reaches-its-effects/
├── plan.md              # this file
├── research.md          # Phase 0 — R1/R2 decide feasibility; both yes
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source code

```text
tests/Integration.Tests/Automation/
  EventReachesItsEffectsTests.cs      # new — the journey, end to end

(no files under src/ change)
```

## Approach

### The shape of the test

1. **Seed a rule and publish it.** `POST /rules` mints a Draft;
   `POST /rules/{name}/publish` makes it Active and upserts the evaluator's
   cache. A test that stops at create has seeded something that cannot fire —
   and would pass for the wrong reason for ever after.
2. **Assert the rule is Active** before relying on it, so a failure downstream
   cannot be explained away by a mis-seeded precondition.
3. **Publish a matching event over MQTT**, as a machine does. Not a direct HTTP
   post into the middle of the chain: FR-002 wants the whole ingress exercised,
   and the ingress is where spec 020 lived.
4. **Poll for the effect** to a deadline, then assert on the effect itself.
5. **A negative case**: an event matching no rule changes nothing. Without it, a
   test asserting a value equals what it already was would pass on a dead
   system.

### What is asserted, and what must not be

FR-004 rules out the assertion a reasonable person reaches for first. Each of
these **would have passed against the failure this feature exists to catch**:

- `FabEventIngestedV1` was published — it was
- the rule was evaluated — it was, correctly
- `SystemVariableValueRequestedV1` was published — **this is exactly what broke**
- the event is stored and readable — it was, which is what hid the break

Only the changed value and the arrived highlight sit downstream of every join.

### The part that makes it a test rather than a ritual

**SC-002 requires breaking the journey deliberately and watching this fail.**
Every other criterion is satisfiable by a test that runs green, and 228 of those
already existed while the path was broken. The verification step is therefore
not optional polish; it is the evidence the feature was delivered.

The cheapest honest break is the one that actually happened: route the ambient
publish in `OutboxEventBus` back through the DbContext outbox, run the test,
watch it fail, restore. Recorded in the verification note.

### Waiting properly (FR-009)

Poll to a deadline; on expiry report whether *anything* changed, so "late" and
"never" are distinguishable. No fixed sleeps — slower and less informative — and
no asserting on an intermediate step to dodge the wait, which would reintroduce
the blindness being fixed.

## Verification strategy

| Requirement | How it is shown |
|---|---|
| FR-001 / SC-001 | active rule + MQTT event → variable value changed, repeatedly |
| FR-001 (highlight) | active rule + MQTT event → highlight frame on a hub connection |
| FR-003 / SC-003 | non-matching event → no change, no frame |
| FR-005 / SC-002 | the journey broken on purpose → this test fails |
| FR-006 | the rule is asserted Active before the event is published |
| FR-007 / SC-004 | runs in the routine build, three consecutive green runs |
| SC-005 | arrival-to-effect measured, reported, and its limits stated |

## Risks

| Risk | Handling |
|---|---|
| **Flakiness across four services** | Generous polling deadline with late-vs-absent distinguished. If it still flakes, that is a reportable outcome (FR-008), not a quiet `Category=Disruptive`. |
| **A third CI exclusion** | Would return this path to zero coverage — the state that let the break through. Treated as failure of the feature, and the plan says so rather than leaving it as an easy out. |
| **The test passes for the wrong reason** | Two guards: the rule is asserted Active, and the negative case proves the assertion can fail. |
| **Latency measured on a laptop quoted as a guarantee** | SC-005 asks for the number *and* its limits. Specs 020 and 021 both did this; the same honesty applies. |
| **Redelivery double-applies the effect** | SystemVariables dedups by event. Redelivery became routine in spec 020, so it is asserted rather than assumed. |

## Phase gates

- **Phase 0 complete** — research.md answers R1–R4. Both feasibility questions
  are yes, so FR-010 holds and nothing needs raising.
- **Phase 1 complete** — data-model.md, quickstart.md. No contracts directory:
  this feature defines no interface.
- **Gate before Phase 2**: the plan aligns with the constitution and needs no
  ADR change.
