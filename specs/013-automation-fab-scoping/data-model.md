# Data Model: Automation fab scoping

**Feature**: `013-automation-fab-scoping` | **Date**: 2026-08-03

## `FabIdentifier` (new value object)

`src/Automation/Domain/Rule/FabIdentifier.cs`

Automation's own copy, per R1. Mirrors
`Identity/Domain/RegisteredClient/FabIdentifier.cs` in grammar and shape so
the same fab string is accepted identically on both sides of the wire.

| Aspect | Value |
|---|---|
| Base | `StringValueObject` |
| Factory | `FabIdentifier.From(string)` |
| Validation | Not null or whitespace; matches the existing fab grammar |
| Guards | `Ensure.That(...)` per ADR-0105 — never `ArgumentNullException.ThrowIfNull` |

## `Rule` (changed)

`src/Automation/Domain/Rule/Rule.cs`

| Field | Change |
|---|---|
| `Fab` | **NEW** — `FabIdentifier`, private setter, set at creation |
| everything else | unchanged |

**Invariants**

- `Fab` is required at creation and never changes afterwards. There is no
  `MoveToFab`; relocating a rule means re-authoring it (spec Assumptions).
- `Name` is unique within `Fab`, not globally.
- `Create` takes the fab; `Publish`, `Archive` and `Rotate`-equivalents do
  not — they act on an already-placed rule.

**State transitions**: unchanged. `Draft → Active → Archived` is orthogonal
to fab.

## Persistence

`src/Automation/Infrastructure/Persistence/Configurations/RuleConfiguration.cs`

| Element | Before | After |
|---|---|---|
| column | — | `fab`, `text`, `NOT NULL` |
| unique index | `(name)` | `(fab, name)` |
| lookup index | `(trigger_source, trigger_kind, state)` | `(fab, trigger_source, trigger_kind, state)` |

The lookup index gains `fab` as its leading column so the seeder's query and
any future fab-filtered read are covered by it.

### Migration

One migration, ordered so the table is never in a state the application
cannot serve:

```sql
ALTER TABLE rules ADD COLUMN fab text;              -- nullable first
UPDATE rules SET fab = 'munich' WHERE fab IS NULL;  -- backfill (spec Assumption)
ALTER TABLE rules ALTER COLUMN fab SET NOT NULL;

DROP INDEX ux_rules_name;
CREATE UNIQUE INDEX ux_rules_fab_name ON rules (fab, name);

DROP INDEX ix_rules_trigger;
CREATE INDEX ix_rules_fab_trigger ON rules (fab, trigger_source, trigger_kind, state);
```

`munich` is a literal, not configuration (R4): a migration must produce the
same result everywhere, and a config-driven backfill would assign different
fabs per environment.

Actual index names come from the existing migration; the above is the shape,
not the verbatim script.

## Cache

`IRuleCache` / `InMemoryRuleCache`

| Aspect | Before | After |
|---|---|---|
| key | `(TriggerSource, TriggerKind)` | `(Fab, TriggerSource, TriggerKind)` |
| lookup | `LookupActive(source, kind)` | `LookupActive(fab, source, kind)` |
| `CompiledRule` | no fab | carries `Fab` so the seeder can key it |

Widening the key rather than filtering the bucket is what keeps selection
O(1) as other fabs' rules accumulate (R3, SC-007).

## Read model

`RuleDto` gains `Fab`.

It is additive and safe: the rules SPA already tolerates unknown fields, and
an operator seeing their own fab on a row is useful context rather than new
information.

## Repository

`IRuleRepository.GetByNameAsync(RuleName, CancellationToken)` becomes
`GetByNameAsync(FabIdentifier, RuleName, CancellationToken)`.

Without the fab, a name that is now only unique per fab could return another
fab's rule — and the `If-Match` comparison would then run against the wrong
aggregate (R6).

`GetByIdentifierAsync` is unchanged: a `RuleIdentifier` is already globally
unique.
