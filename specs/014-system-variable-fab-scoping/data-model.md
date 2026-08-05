# Data Model: Fab-scope system variables

**Feature**: `014-system-variable-fab-scoping` | **Date**: 2026-08-05

## Variable (aggregate root)

| Field | Change |
|---|---|
| `Fab` | **new** — `FabIdentifier`, required by `Define`, private setter, never mutated afterwards |
| `Name`, `Type`, `Value`, `State`, `BooleanLabels`, `CreatedAt`, `CreatedBy` | unchanged |

No `MoveToFab`. A variable does not change fab: overlays reference it by name
within a fab, and moving one would silently repoint every overlay that resolves
it. Moving is out of scope by decision, not by omission.

### FabIdentifier

New value object in `SystemVariables.Domain.Variable`, mirroring
`Automation.Domain.Rule.FabIdentifier` exactly: 2–32 chars, lowercase letters,
digits or `-`, starting with a letter. `From(...)` with `Ensure.That(...)`.

The grammar must stay identical to the other three. The same string arrives
here from a caller's `/fabs/<id>` group and from a value-change message
stamped by Automation; a value one context accepts and another rejects would
strand variables that can never resolve.

## Persistence

```
fab   character varying(32)  NOT NULL

ux_system_variables_fab_name_active  UNIQUE (fab, name) WHERE state <> 'Archived'   [replaces ux_system_variables_name_active]
```

Keep the partial filter. Archiving has always released a name for reuse, and
scoping the index to a fab must not quietly take that away.

### Migration

Four steps, the shape spec 013 proved against a real pre-existing database:

1. add `fab` nullable — the column can be added to a populated table;
2. backfill to `'munich'`, **counting rows and raising a warning naming the
   count**;
3. alter to NOT NULL — the constraint can now hold;
4. drop `ux_system_variables_name_active`, create
   `ux_system_variables_fab_name_active`.

`Down` drops the column and restores the name-only index. It discards each
variable's fab, and rolling forward re-attributes everything to munich — say so
in the migration, because the index conflict is the louder failure and the
lesser one.

## Value-change request handling

`SystemVariableValueRequestedV1` is **unchanged**. It already carries
`Metadata.Fab`; only the consumer changes.

| Case | Today | After |
|---|---|---|
| Fab present, variable exists in it | sets it | sets it |
| Fab present, variable exists in *another* fab | **sets the other fab's variable** | changes nothing; logged with fab + name |
| Fab present, variable exists nowhere | logged, dropped | logged, dropped |
| Fab absent | sets whatever matches the name | changes nothing; logged |

## Dedup store

`TryReserveAsync(fab, variableName, causingEventIdentifier)` — fab added to the
reservation key and to whatever backs it.

Without it, two fabs' rules reacting to the same ingested event share a causing
event identifier and a variable name, so the second fab's legitimate change is
swallowed as a redelivery of the first. That is the normal case once both fabs
run rules on the same trigger, not an edge one.

## Reverse index

`IReverseIndex` keys on `(fab, variableName)`. The fab comes from the overlay
being indexed, recorded when the overlay revision is published, not supplied
per lookup — `VariableValueChangedDomainEventHandler` knows the variable that
changed, not a caller.

Keyed, not filtered: filtering after lookup would make cost grow with the
number of overlays in other fabs, on a path inside the 200 ms leg.

## Entities untouched

Overlay definitions, layouts, the variable value history, and every
`Shared.Contracts` message shape. This feature adds a field to one aggregate
and a dimension to two lookup keys.
