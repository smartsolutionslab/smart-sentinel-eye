# Research: Fab-scope system variables

**Feature**: `014-system-variable-fab-scoping` | **Date**: 2026-08-05

Everything below was checked against the code, not recalled.

## Decision: follow spec 013's shape for the model and migration

**Decision**: A `FabIdentifier` value object local to `SystemVariables.Domain`,
a `Fab` on `Variable` set at creation and never moved, and a unique index on
`(fab, name)` keeping the existing `state <> 'Archived'` partial filter.

**Rationale**: Spec 013 did exactly this for rules and the shape survived a
code review, a live walkthrough and a migration against a real pre-existing
database. Value objects are not shared across contexts (ADR-0044), so a fourth
`FabIdentifier` is correct rather than duplication — the grammar must match the
other three or a fab string one context accepts and another rejects will strand
variables that can never be resolved.

**Alternatives considered**:

- *Share one `FabIdentifier` from `Shared.Kernel`.* Rejected: ADR-0044 and the
  boundary tests forbid it, and the existing three already diverge in nothing
  but namespace.
- *Composite key `(fab, name)` as the primary key.* Rejected: the aggregate
  already has an identifier, and changing the key shape would ripple into every
  reference for no gain.

## Decision: the migration must announce its backfill

**Decision**: Add nullable → backfill to `munich` → set NOT NULL → swap the
index, with the backfill counting affected rows and raising a warning naming
the count.

**Rationale**: Spec 013's `FabScopeRules` migration does this and it was not
theatre — walking the quickstart applied it to a database that predated the
change, and the warning fired naming four rules. The assumption "everything
that exists belongs to munich" cannot be checked from inside the database,
because the old rows are exactly the ones carrying no fab. Announcing it at the
moment it is applied is the only place it can be caught.

**Alternatives considered**:

- *Silent backfill.* Rejected: on any database that is not munich's, every
  variable would be attributed to a fab nobody operates and simply stop
  resolving, with nothing to indicate why.
- *Refuse to migrate when rows exist.* Rejected: blocks every deployment whose
  variables really are munich's, which is all of them today.
- *Config-driven fab.* Rejected for the reason spec 013 rejected it — a
  migration must produce the same result everywhere, and a configurable
  backfill assigns different fabs in dev and prod.

## Decision: dedup key gains the fab

**Decision**: `IVariableValueRequestDedupStore.TryReserveAsync` takes the fab
alongside the variable name and causing event.

**Rationale**: It currently reserves on `(variableName, causingEventIdentifier)`.
Two fabs' rules reacting to the same ingested event would share a causing
event identifier and a variable name, so one fab's legitimate change would be
swallowed as a redelivery of the other's. This is not hypothetical: it is the
normal case once both fabs run rules on the same trigger.

## Decision: resolution keys on `(fab, name)`, and the fab comes from the overlay

**Decision**: `IReverseIndex` keys on `(fab, variableName)`. The fab is carried
by the overlay entry already in the index, not supplied per lookup by the
caller.

**Rationale**: `InMemoryReverseIndex` keys `_byName` on the variable name alone
(`ConcurrentDictionary<string, HashSet<Guid>>`), so a munich overlay and a
dresden overlay referencing `oeeLine1` land in one bucket. The index exists to
answer "which overlays does this variable affect", and the answer must not
cross fabs. Taking the fab from the indexed overlay rather than the lookup
keeps `VariableValueChangedDomainEventHandler` — which knows the variable, not
the caller — able to ask the question at all.

**Alternatives considered**:

- *Filter after lookup.* Rejected on the same grounds spec 013 rejected it for
  the rule cache: lookup cost would grow with the number of overlays in other
  fabs, on a path inside the 200 ms leg.
- *Separate index per fab.* Rejected as the same thing with more moving parts.

## Finding: SC-005 has no baseline to compare against

**This is the one that changes the plan.**

The spec says the event-to-overlay time must show "no measurable regression …
measured the same way as before the change". There is no such measurement.

| Latency test | Covers |
|---|---|
| `NFR002_AuditSearchLatencyTests` | audit search |
| `CommandLatencyTests` | camera commands |
| `NFR001_JwtValidationLatencyTests` | token validation |
| `NFR002_MqttConnectAuthTests` | MQTT connect auth |
| `WhepHandshakeLatencyTests` | WHEP handshake |

None measures event → overlay. `NFR001_RuleEvaluationLatencyTests` — the test
that would — is unimplemented task **#749**.

**Consequence**: taking a baseline *after* changing the key measures the new
code against itself and passes trivially. The baseline has to be established
before the key changes, or SC-005 is unverifiable and should be restated.

**Decision**: establish it first, in the same feature, as the first task of the
resolution slice. It is worth doing regardless — the 200 ms leg is the load-
bearing NFR of the product and currently has nothing watching it.

## Finding: the component being changed has no test of its own

`src/SystemVariables/Infrastructure/Resolution/InMemoryReverseIndex.cs` is
verified only by a hand-written double at
`tests/SystemVariables.Application.Tests/Fakes/InMemoryReverseIndex.cs`.
Nothing references the shipped class.

This is the same finding the #1299 review raised against `InMemoryRuleCache`,
where the fix was a new `Automation.Infrastructure.Tests` project. The same
remedy applies: a `SystemVariables.Infrastructure.Tests` project, and the
fab-keying change lands on a component that has tests before it is changed
rather than after.

Related open tasks, all unimplemented, all on this path: **#461** (T035,
`InMemoryReverseIndexTests`), **#494** (T068, `VariableResolutionIntegrationTests`),
**#514** (T088, `VariablePushIntegrationTests`).

**Decision**: add the shipped-class tests before changing the key. Not as a
courtesy — without them, "the fab-keying works" would be asserted against a
double that the change also has to be applied to, which is how the two drift.

## Finding: SystemVariables is entirely unguarded

`SystemVariableEndpoints` contains no reference to `IFabAuthorizationGuard` or
`fabId`. Any authenticated caller holding `sse.variables.*` can read and change
every fab's variables today. Guarding it closes the SystemVariables slice of
**#1155**.

**Decision**: reuse `FabResolution` and `FabClaims` from `ServiceDefaults`
unchanged — both already exist, both are already tested against all four rows
of the decision table, and both are already driven over real HTTP by
`RuleFabResolutionIntegrationTests`. This feature adds no new resolution
mechanism; it applies the existing one to a second context.

## Decision: ADR-0114 needs amending, not superseding

**Decision**: Amend ADR-0114 to record that fab inference now covers the
SystemVariables endpoints as well as Automation's rule endpoints.

**Rationale**: ADR-0114 says explicitly that inference "is scoped to those
endpoints; extending inference is a new decision rather than an application of
that one". The decision itself is unchanged — infer for a single-fab operator,
refuse a multi-fab one who names none. Only its scope widens, which is what an
amendment is for. A superseding ADR would imply the original reasoning was
wrong; it was not.

## Decision: cross-fab references fail at evaluation, and say so

**Decision**: `SystemVariableValueRequestedV1Handler` resolves `(fab, name)`
from `Metadata.Fab`. A miss logs at warning with the fab and the variable name
and drops the request.

**Rationale**: The spec's Assumptions settle *where* — checking at authoring
would need Automation to call SystemVariables synchronously, which principle
III forbids. What research adds is *how loudly*: the handler already logs and
drops malformed input, and #1252 hid for a release behind exactly that shape of
silence. Spec 013's fix for the same trap was a distinct log message naming the
offending value, which is the precedent to follow.

## Resolved unknowns

| Unknown | Resolution |
|---|---|
| Which fab do pre-existing variables belong to? | munich — the only fab in service. Announced by the migration, not assumed. |
| Where does the fab come from on a value-change? | `Metadata.Fab`, already populated by Automation, currently ignored. |
| Does anything else publish that contract? | No. Automation is the only publisher. |
| How does the reverse index learn a fab? | From the overlay it indexes, at publish time. |
| Is there a latency baseline? | **No.** Must be established first — see above. |
| Is the changed component tested? | **No.** Must be tested first — see above. |
