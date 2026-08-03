# Phase 0 Research: Automation fab scoping

**Feature**: `013-automation-fab-scoping` | **Date**: 2026-08-03

Seven questions had to be settled before the design could be written. Each is
recorded with what was chosen, why, and what was rejected.

---

## R1 — Where does `FabIdentifier` live for Automation?

**Decision**: Automation gets its own `FabIdentifier` value object at
`src/Automation/Domain/Rule/FabIdentifier.cs`.

**Rationale**: Three contexts already carry their own copy —
`Identity/Domain/RegisteredClient/FabIdentifier.cs`,
`EventIngestion/Domain/Event/FabIdentifier.cs`,
`AuditObservability/Domain/AuditEvent/FabIdentifier.cs`. Constitution §III
forbids cross-context project references and NetArchTest enforces it, so
sharing one type is not available. Placement inside the aggregate folder
follows ADR-0092 and matches Identity exactly.

**Alternatives rejected**:
- *Promote `FabIdentifier` to `Shared.Kernel`* — Shared.Kernel is
  documented as "value-object base types, no domain". A fab is domain
  vocabulary, and hoisting it would make every context share one definition
  of a concept they are each entitled to model differently.
- *Reference `Identity.Domain`* — a boundary violation the architecture
  tests fail on.

---

## R2 — How does an endpoint learn which fabs the caller is assigned to?

**Decision**: Promote the existing private helper
`AuditEndpoints.ExtractFabSet` (`AuditEndpoints.cs:148`) into
`ServiceDefaults.Authorization` as a supported way to enumerate a caller's
fabs, and have Automation use it. AuditObservability switches to the shared
one.

**Rationale**: The capability already exists twice — once as
`ExtractFabSet`, once as an ad-hoc predicate in
`EventsEndpoints.Writes.cs:170`. Automation would be the third copy. All
three want the same thing: split the `groups` claim defensively, keep the
`/fabs/` prefixed entries, strip the prefix.

`IFabAuthorizationGuard` deliberately answers only "may this caller touch
fab X". Enumeration is a different question and does not belong on that
interface; it goes alongside as a separate helper so the guard's contract
stays a single assertion.

**Alternatives rejected**:
- *Add `GetAssignedFabsAsync` to `IFabAuthorizationGuard`* — widens a
  focused interface, and every existing implementation and test double would
  have to grow a method most callers never use.
- *Leave `ExtractFabSet` private and copy it* — a third copy of claim
  parsing, in security-relevant code, where the copies would drift.

---

## R3 — How is the rule cache keyed?

**Decision**: `IRuleCache.LookupActive(fab, triggerSource, triggerKind)`,
with `InMemoryRuleCache._byTrigger` keyed on the triple.

**Rationale**: The cache is the actual selection mechanism —
`RuleEvaluator.cs:28` delegates to it and does no further filtering. Fixing
the evaluator without the cache would leave the defect intact behind a
correct-looking call site. Keying on the triple also preserves the current
performance shape: lookup stays a single dictionary hit and cannot degrade as
other fabs' rules are added, which is SC-007.

**Alternatives rejected**:
- *Keep the key and filter the returned bucket* — makes lookup cost grow
  with other fabs' rule counts, failing SC-007 on the exact axis the system
  is expected to scale along (250 cameras, multiple fabs).
- *One cache instance per fab* — moves the problem to cache lifetime
  management and the seeder, for no gain over a wider key.

---

## R4 — What shape does the migration take?

**Decision**: A single EF migration performing, in order: add `fab` as
nullable; backfill every existing row to `munich`; set `NOT NULL`; drop the
unique index on `name`; create a unique index on `(fab, name)`.

**Rationale**: The three-step column addition is the standard way to add a
required column to a populated table without a window where the constraint
is violated. The index swap must happen in the same migration — between
dropping one and creating the other there is no uniqueness guarantee at all,
and doing it across two migrations would leave a released version in that
state.

`munich` is the spec's stated assumption. It is written as a literal in the
migration rather than read from configuration, because a migration must
produce the same result on every environment and a config-dependent backfill
would silently assign different fabs in dev and prod.

**Alternatives rejected**:
- *Archive existing rules instead of backfilling* — stops live automation on
  a 24/7 system.
- *Leave `fab` nullable and treat null as "all fabs"* — preserves the
  cross-fab defect precisely for pre-existing rules, which are the ones most
  likely to be running.

---

## R5 — How is the FR-013 deviation recorded?

**Decision**: A new ADR (next number: **0114**), not an amendment.

**Rationale**: The rule being deviated from — *"There is no implicit 'current
fab' — the caller picks per request"* — exists only as an XML doc comment on
`IFabAuthorizationGuard`. A search of `docs/adr/` finds no ADR asserting it,
so there is nothing to amend. The deviation therefore needs its own record,
and that record should also correct the guard's doc comment so the two stop
contradicting each other.

**Alternatives rejected**:
- *Amend ADR-0008 (Keycloak) or ADR-0007* — neither states the rule; adding
  a contradiction to an unrelated ADR would hide it.
- *Just change the XML comment* — a design reversal with security-relevant
  consequences, recorded nowhere a reviewer would look.

---

## R6 — Interaction with spec 012's concurrency gate

**Decision**: No change to the `If-Match` mechanism. Fab scoping is applied
*before* the version check in each handler.

**Rationale**: Rule commands were made conditional in spec 012 and identify
their target by `RuleName`. Once names are unique per fab rather than
globally, `GetByNameAsync` needs the fab too, or it can return another fab's
rule and the version check would then compare against the wrong aggregate.
Ordering matters: refuse on fab first, so a caller cannot use the difference
between a 403 and a 409 to learn whether a named rule exists in another fab
(FR-007).

**Alternatives rejected**:
- *Check version first* — leaks existence across fabs through the status
  code, which FR-007 forbids.

---

## R7 — Blast radius in existing tests

**Decision**: Accept that every test authoring a rule must supply a fab, and
treat updating them as part of the slice rather than follow-up work.

**Findings**: The affected surfaces are the Automation application tests
(rule builders and handler tests), `RuleLifecycleIntegrationTests` and
`RuleReadIntegrationTests`, the rules e2e path, and the ScenarioSimulator if
it authors rules. `RuleBuilder` needs a `WithFab`, defaulting to `munich` so
existing call sites stay readable.

**Rationale**: A required field on an aggregate cannot be introduced without
touching every construction site; deferring it would leave the branch
red. Spec 012 established that "existing tests will need updating — this is
expected, not incidental", and the same applies here.

---

## Open items carried into design

None. All seven questions are resolved; no `NEEDS CLARIFICATION` markers
remain in the spec or this document.
