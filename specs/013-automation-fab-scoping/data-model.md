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
| unique index | `ux_rules_name_active` on `(name)`, filtered `state <> 'Archived'` | `ux_rules_fab_name_active` on `(fab, name)`, **same filter** |
| lookup index | `ix_rules_trigger_state` on `(trigger_source, trigger_kind, state)` | `ix_rules_fab_trigger_state` on `(fab, trigger_source, trigger_kind, state)` |

**The unique index is partial, and stays partial.** Archiving a rule has
always released its name for re-use; scoping the index to a fab must not
quietly remove that. An earlier draft of this document described a plain
unique index on `(fab, name)`, which would have done exactly that — corrected
here after reading `RuleConfiguration.cs`.

The lookup index gains `fab` as its leading column so the seeder's query and
any future fab-filtered read are covered by it.

### Migration

One migration, ordered so the table is never in a state the application
cannot serve:

```sql
ALTER TABLE rules ADD COLUMN fab character varying(32);   -- nullable first
UPDATE rules SET fab = 'munich' WHERE fab IS NULL;        -- backfill (spec Assumption)
ALTER TABLE rules ALTER COLUMN fab SET NOT NULL;

DROP INDEX ux_rules_name_active;
CREATE UNIQUE INDEX ux_rules_fab_name_active
    ON rules (fab, name) WHERE state <> 'Archived';       -- filter preserved

DROP INDEX ix_rules_trigger_state;
CREATE INDEX ix_rules_fab_trigger_state
    ON rules (fab, trigger_source, trigger_kind, state);
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
