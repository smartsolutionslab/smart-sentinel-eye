# Verification: 058 — properties that travel together

**Feature**: 058 | **Date**: 2026-09-02

## User Story 3 is DECLINED, not deferred

**`AuditEvent.Actor` + `ActorUsername` cannot become one composite without
dropping an index, and FR-004 forbids that.** This is a hard EF limitation,
found by building it rather than by reasoning about it, and it is recorded here
because a story ticked as "done later" would imply it is merely unfinished.

### What blocks it

`ix_audit_actor_occurred` spans **two entity types once the composite exists**:

```csharp
builder.HasIndex(auditEvent => new { auditEvent.Actor, auditEvent.OccurredAt })
    .HasDatabaseName("ix_audit_actor_occurred");
```

`Actor` becomes the composite and `OccurredAt` stays on the row. EF has no way
to express an index across that boundary, by either mechanism:

**Owned reference** — the index lambda tries to add a scalar `Actor` where a
navigation already exists:

```text
The property or navigation 'Actor' cannot be added to the 'AuditEvent' type
because a property or navigation with the same name already exists on the
'AuditEvent' type.
```

**Complex type** — research R1 rejected complex types generally; they were
tried here anyway, because this is exactly the case where an owned reference
fails and a complex type's members belong to the owning entity. Same error.
Naming the member directly fails differently and just as finally:

```text
The property 'Actor.Identifier' cannot be added to the type 'AuditEvent'
because no property type was specified and there is no corresponding CLR
property or field.
```

**Removing the index makes the model build.** That is how the cause was
confirmed — and it produces a pending migration that drops
`ix_audit_actor_occurred`, which is precisely the schema change FR-004 exists
to prevent. The index backs the audit search's actor filter, on a hypertable.

### What was not done about it

No workaround was adopted, and that is deliberate. The available ones are all
worse than the problem:

- **Drop the index** — breaches FR-004 and slows an indexed search path on the
  largest table in the system.
- **Keep it outside the EF model**, created by raw SQL like the TimescaleDB
  hypertable already is. Then EF's differ wants to DROP it on the next
  migration anyone generates — the same latent-divergence trap as issue #2022,
  deliberately re-created.
- **Split the composite** so only the username moves. That is not the story.

### Consequence for the spec

FR-006 and SC-001's count of twelve are **not achievable as written**. Eleven
pairs become eleven composites; the twelfth stays two properties. `spec.md` and
`data-model.md` describe the actor composite as buildable, and they were wrong
about the one thing that could not be checked in advance — an index is not a
column, and R1 only checked columns.

**If US3 is wanted, it needs a decision that is not this feature's to make**:
either the index moves out of the model with the divergence risk accepted, or
`occurred_at` and the actor stop sharing an index. Both are schema decisions.

## User Story 2 is delivered

`StoredPayload` groups the audit payload with its size and **derives** the
size, so the two can no longer disagree. No index spans it, so nothing above
applies.

- AuditObservability reports no pending model change.
- Domain 89, Application 43, Architecture 105 green; Release build clean.
- The derivation is enforced structurally, not by assertion: `StoredPayload`
  has no public constructor and its only factory takes content alone. A test
  asserts that shape, so re-introducing a size parameter fails the build's
  test run rather than being noticed in review.

## User Story 4 is DECLINED, for exactly the same reason

`Rule.TriggerSource` + `TriggerKind` cannot become one `Trigger` either.
`ix_rules_fab_trigger_state` spans the row and the composite:

```csharp
builder.HasIndex(rule => new { rule.Fab, rule.TriggerSource, rule.TriggerKind, rule.State })
    .HasDatabaseName("ix_rules_fab_trigger_state");
```

`Fab` and `State` stay on the row; the two trigger columns would move into the
composite. Same boundary, same refusal.

**Checked in isolation rather than by analogy**, because assuming was what made
the spec wrong about US3. A scratch model with nothing in it but an owner, a
composite and that index shape:

```text
owned reference    : InvalidOperationException: The property or navigation 'Trigger'
                     cannot be added to the 'Rule' type because a property or
                     navigation with the same name already exists on the 'Rule' type.
complex type       : InvalidOperationException: (identical)
```

### The general rule this feature discovered

**A pair that participates in a composite index alongside a column outside the
pair cannot be grouped**, by either EF mechanism, without changing the index.

That is the rule research R1 should have found and did not. R1 asked "do the
columns survive?" and the answer was yes. It never asked "do the *indexes*
survive?", and two of the twelve pairs are indexed this way. The check is cheap
and should have been in Phase 0:

```sh
grep -n "HasIndex" src/<Context>/Infrastructure/Persistence/Configurations/*.cs
```

### Revised outcome for the feature

| Story | Pairs | Outcome |
|---|---|---|
| US1 — timestamp + actor | 9 | **Delivered** |
| US2 — payload + size | 1 | **Delivered**, with the size derived |
| US3 — actor + username | 1 | **Declined** — `ix_audit_actor_occurred` |
| US4 — trigger source + kind | 1 | **Declined** — `ix_rules_fab_trigger_state` |

**Ten of twelve pairs grouped, not twelve.** SC-001's count is wrong as
written, and both misses share one cause rather than being two separate
disappointments.

Neither decline is a deferral. Both need a schema decision that is outside this
feature: either the index leaves the EF model — re-creating issue #2022's
divergence trap on purpose — or the index stops spanning the pair, which is a
change to how these tables are queried.

## Phase 7 (Polish) evidence

Recorded here rather than only in a PR body, so it survives if the PR does not.

### T050 — schema, every context

All nine report **no pending model change**, against the corrected baseline in
[baseline-schema.md](./baseline-schema.md) where all nine were clean to begin
with. CameraCatalog included, unborrowed, since PR #2021 brought its
`EntityFrameworkCore.Design` reference to `develop`.

### T051 — nothing outward moved

```sh
git diff --name-only origin/develop...HEAD | grep -E "Shared.Contracts|/DTOs/"
```

No file under `src/Shared.Contracts/` and no DTO definition changed. Only the
mapper expressions that fill them moved (FR-008, SC-005).

### T052 — coverage, every touched context

Domain assemblies, merged from each context's Domain and Application runs
(ADR-0065 gate is 90%):

| Assembly | Measured |
|---|---|
| StreamDistribution.Domain | 95.2% |
| CameraCatalog.Domain | 93.9% |
| Identity.Domain | 92.8% |
| Automation.Domain | 93.7% |
| SystemVariables.Domain | 94.5% |
| LayoutComposition.Domain | 96.3% |
| OverlayDesigner.Domain | 92.4% |
| AuditObservability.Domain | 94.4% |

No composite member was written without a caller, so none needed a test to
prop up a number — the failure mode that broke spec 057's CI.

**Not measured by the gate itself.** `scripts/coverage-check.ps1` needs
PowerShell 7 and this machine has 5.1, so these come from reportgenerator over
the same cobertura files the script merges. CI runs the real gate.

### T053 — build and unit suites

Release build clean, no warnings. **28 of 29 test projects run, 1940 tests
passed, 0 failed.** `Integration.Tests` did not run — it needs Docker, which
this machine does not have, and it is the only evidence that FR-008's outward
shapes survive a real round trip. **CI is the gate for it.** No claim is made
here about e2e either.

### T054 — findings recorded and not fixed

1. **Publish and archive carry no actor** anywhere in the codebase (FR-010). A
   revision now shows one `Creation` beside a bare `PublishedAt` and a bare
   `ArchivedAt`. Left visible; adding one is a schema and behaviour change.
2. **Whether any stored audit row's size disagrees with its content is
   untested for.** `StoredPayload` prevents new disagreements and preserves old
   ones; repairing them is a migration.
3. **Six of nine timestamp/actor sites had no assertion on the actor half**
   before this work — `Stream`, `RegisteredClient`, `Rule`, `Variable`,
   `Layout`+`Revision`, `Overlay`+`Revision`. Covering tests were added first in
   each case. That the "who did it" half was systematically unasserted is some
   evidence for the premise that two loose fields drift.
4. **23 of 29 test projects set `<Nullable>enable</Nullable>`** while ADR-0048
   and `Directory.Build.props` say disabled. It is why the same guard test needs
   `null!` in some projects and plain `null` in others. Not this feature's to
   fix; worth an issue.
