# Implementation Plan: A fab identifier can be sorted, in every context that has one

**Branch**: `039-comparable-fab-identifier` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/039-comparable-fab-identifier/spec.md`

---

## Summary

Eight `FabIdentifier` records gain `IComparable<FabIdentifier>` with an ordinal
comparison on `Value`, plus the four comparison operators. A convention test
keeps the eight in step. One listing test that could not be written becomes
writable, and the comment warning authors away from it goes.

No production behaviour changes. No EF, migration, query, dependency or ADR.

### What Phase 0 changed about this plan

- **Larger than expected**: the work is eight value objects **and eight test
  files**, not eight and one. `Identity.Domain` sits at 91.7% against a ≥ 90%
  gate over ~250 lines; five uncovered members would take it to ~89.8% and fail
  the build (research §4). One of the eight test files does not exist yet.
- **The spec asked for an assertion that cannot be written.** Under the fab
  grammar I could not construct a pair whose ordinal and culture-sensitive
  orderings disagree on this platform, and globalization-invariant mode is off.
  Ordinality is asserted **structurally** instead — the convention test requires
  the source to name `StringComparison.Ordinal` (research §5).
- **The copies have already drifted**, and it is the same context twice:
  `AuditObservability`'s body omits `nameof(value)` from its guard, and it is the
  one context without a `FabIdentifierTests.cs`. Observed, raised, **not fixed**
  here (research §1).
- **The convention test reads source rather than reflecting**, and that decision
  and the ordinality problem resolve each other: `StringComparison.Ordinal` is not
  visible by reflection, and a ninth context added without a project reference is
  not visible to reflection either (research §3).

---

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: none added. xUnit + Shouldly for tests; the convention
test needs no NetArchTest because it reads source.

**Storage**: untouched. No migration, no EF change, no query change.

**Testing**: xUnit + Shouldly; a source-reading convention test in
`tests/Architecture.Tests/`.

**Target Platform**: unchanged.

**Project Type**: eight bounded contexts' Domain assemblies, one Application
test, one architecture test.

**Performance Goals**: not on the §IV latency path. The comparison runs inside a
sort the database already performs.

**Constraints**: **the ≥ 90% Domain coverage gate in every one of the eight**.
This is the binding constraint on the shape of the work (research §4).

**Scale/Scope**: 8 value objects, 8 Domain test files (7 extended, 1 created),
1 new Application test, 1 convention test, 1 comment deleted, 0 production
behaviour changes.

---

## Constitution Check

*GATE: must pass before Phase 0. Re-checked after Phase 1 — see below.*

| Principle | Verdict | Note |
|---|---|---|
| **II. DDD with value objects** | **Pass** | Strengthens it. A value object that can be equated but not ordered is a partially-modelled value; this completes it, and the ordering is defined by the type rather than by whoever sorts it. |
| **III. Bounded context isolation** | **Pass** | Eight independent edits with no shared code and no new reference. ADR-0044's duplication is preserved exactly; this is the ADR being *applied*, not worked around. |
| **IV. Latency budget** | **Not on the path** | No leg affected; no event, no service call. |
| **V. Spec-driven** | **Pass** | Spec → plan → tasks → implementation. |
| **VII. Observability** | **Not engaged** | No new signal. The failure this removes was an exception in a test run, never a production log line. |
| **VIII. Safe at trust boundaries** | **Pass** | No boundary touched. Validation is unchanged — `From` is not modified in any copy. |
| **IX. No speculative generality** | **Admitted and argued, not waved through** | See below. |

### §IX in full, because it is the one that bites

Seven of the eight contexts gain an ability no current caller uses. Searched:
nothing keys, hashes into a set, or sorts a `FabIdentifier` anywhere except
`ListCamerasQueryHandler`'s tie-break (research §6). On its face that is
generality for a need that does not exist.

**The counter-argument, which the spec makes and this plan accepts.** This is not
a new abstraction — no interface is introduced, no extension point, no
configuration. It is **closing a gap between eight copies that ADR-0044 makes
deliberately identical**. The grammar is already the same in all eight by design;
the ordering differing where the grammar does not is not restraint, it is drift
that nobody chose. `CameraName`, in the same folder, already has it.

**And the alternative is worse in a specific way.** Fixing only `CameraCatalog`'s
leaves seven copies differing invisibly, and the failure when the eighth context
sorts is the one this feature exists to remove: a message naming neither the field
nor the query, costing the next author the same half-hour. The convention test is
what turns "keep them in step" from a habit into something structural.

**What would change this verdict**: if the eight copies were not required to be
identical, this would be seven unnecessary edits. They are, so it is not.

**Post-Phase-1 re-check**: unchanged. The design adds one interface implementation
per existing type and one test. No new type, no new abstraction, no new
dependency.

---

## Project Structure

### Documentation (this feature)

```text
specs/039-comparable-fab-identifier/
├── spec.md
├── plan.md                        # this file
├── research.md                    # Phase 0 — eight findings
├── contracts/
│   └── fab-ordering.md                # the comparison, and what the guard requires
├── quickstart.md
└── checklists/requirements.md
```

**No `data-model.md`.** No entity, field or stored state changes. The one type
involved is described in the spec's Key Entities and its behaviour in
`contracts/fab-ordering.md`.

### Source code

```text
src/{AuditObservability/Domain/AuditEvent,
     Automation/Domain/Rule,
     CameraCatalog/Domain/Camera,
     EventIngestion/Domain/Event,
     Identity/Domain/RegisteredClient,
     LayoutComposition/Domain/Layout,
     StreamDistribution/Domain/Stream,
     SystemVariables/Domain/Variable}/FabIdentifier.cs      # 8 identical edits

tests/
  {Automation,CameraCatalog,EventIngestion,Identity,
   LayoutComposition,StreamDistribution,SystemVariables}.Domain.Tests/
     …/FabIdentifierTests.cs                                # 7 extended
  AuditObservability.Domain.Tests/AuditEvent/FabIdentifierTests.cs   # 1 CREATED
  Architecture.Tests/FabOrderingConventionTests.cs          # NEW
  CameraCatalog.Application.Tests/Queries/
     ListCamerasQueryHandlerTests.cs                        # tying test added, comment removed
```

---

## Phase 1 — Design

### The comparison

Given verbatim in [contracts/fab-ordering.md](./contracts/fab-ordering.md).
Mirrors `CameraName` in shape and differs in exactly one respect — it compares
`Value`, not a normalised form — for a reason that belongs in the code rather
than in this plan, because the difference otherwise reads as an oversight
(research §2).

### The convention test

`tests/Architecture.Tests/FabOrderingConventionTests.cs`, reading source via the
same repository-root walk `StaleCodeConventionTests` uses. It asserts, for every
file named `FabIdentifier.cs` under `src/`:

1. the record declares `IComparable<FabIdentifier>`, matched on the **declaration
   line** rather than the bare word; and
2. its comparison names `StringComparison.Ordinal`.

The second is what makes reading source the right mechanism rather than a
stylistic preference: there is no assembly-level artefact for a comparison's
`StringComparison` (research §3, §5).

**Its failure message must name the offending file and say what breaks** —
FR-008. The runtime failure it prevents names neither the sort field nor the
query, which is the whole reason it took half an hour to diagnose the first time.

### The tying test

In `ListCamerasQueryHandlerTests`: two cameras that tie on the primary sort key
and differ by fab, asserted to come back **in fab order**, on **both**
tie-breaking sort paths. The order, not the absence of an exception — a
comparison returning 0 for everything also stops throwing, and leaves the paging
defect the tie-break exists to prevent.

### Testing strategy

**Per context**: `CompareTo` orders two fabs, returns 0 for equal ones, and
returns a positive number against `null`. Small, and it is what keeps eight
Domain coverage gates where they are (research §4).

**Once, in `Architecture.Tests`**: the convention, including ordinality.

**Once, in `CameraCatalog.Application.Tests`**: the behaviour that motivated all
of it.

---

## Risks

**1. A Domain coverage gate fails.** The most likely way this PR goes red, and it
is not a hypothetical — Identity has ~2% of headroom and this spends more than
that. Mitigated by covering the comparison in all eight rather than only where a
caller exists.

**2. `AuditObservability` is forgotten.** It is the one context with no existing
`FabIdentifierTests.cs`, so seven contexts are "edit a file" and one is "create
one". It is also the copy that has already drifted. Mitigated by the convention
test — but only for the interface; the missing coverage would show up as a gate
failure instead.

**3. The comparison is written against a normalised form**, copied from
`CameraName` without noticing why that type normalises. It would still pass every
test, because the fab grammar admits one spelling — a normalisation step with no
case that exercises it, which is the definition of code nobody can justify later.
Mitigated by the contract stating the difference and by the code carrying the
reason.

**4. The `nameof` drift gets fixed in passing.** It is one word, in a file already
being edited, and fixing it would be defensible in isolation. It would also make
eight identical edits into seven identical edits and one that is slightly
different — in a diff whose reviewability rests on their being identical. Raised
in the PR instead.
