# SystemVariables domain model

**Source:** `src/SystemVariables/Domain/` (and the Application layer only where it shows publishing/consuming of integration events).

## Purpose

SystemVariables owns the named, typed, fab-scoped variables whose values operators or automation set, and it publishes the change events plus the resolved overlay text that follows from them. It does not own overlay content (OverlayDesigner), the rules that decide when a value is requested (Automation), or how resolved text is shown (LayoutComposition).

## Aggregates

### `Variable` (root)

- **Identifier:** `VariableIdentifier` (Guid v7, `src/SystemVariables/Domain/Variable/VariableIdentifier.cs:13`). Scoped to a fab by `FabIdentifier` and named by `VariableName` (`src/SystemVariables/Domain/Variable/Variable.cs:31`, `src/SystemVariables/Domain/Variable/Variable.cs:33`).
- **State:** `VariableState` is `Defined` or `Archived` (`src/SystemVariables/Domain/Variable/VariableState.cs:12`, `src/SystemVariables/Domain/Variable/VariableState.cs:14`). Two states, so no diagram.

| Transition | Entry point | Rule | Citation |
|---|---|---|---|
| (none) -> Defined | `Variable.Define` | Starts `Defined`; initial value defaults to `Unset`; raises the defined domain event | `src/SystemVariables/Domain/Variable/Variable.cs:80`, `src/SystemVariables/Domain/Variable/Variable.cs:91`, `src/SystemVariables/Domain/Variable/Variable.cs:95` |
| Defined -> Defined (value set) | `Variable.SetValue` | Only a `Defined` variable accepts a value; raises the value-changed domain event | `src/SystemVariables/Domain/Variable/Variable.cs:118`, `src/SystemVariables/Domain/Variable/Variable.cs:125` |
| Defined -> Archived | `Variable.Archive` | Sets state `Archived`, resets the value to `Unset`, raises the archived domain event | `src/SystemVariables/Domain/Variable/Variable.cs:140`, `src/SystemVariables/Domain/Variable/Variable.cs:141`, `src/SystemVariables/Domain/Variable/Variable.cs:142` |
| Archived -> Archived | `Variable.Archive` | Idempotent: returns without an event | `src/SystemVariables/Domain/Variable/Variable.cs:135` |

No code path leaves `Archived`.

#### Invariants

- A `Boolean` variable must be defined with `BooleanLabels` (`InvalidOperationException` otherwise): `src/SystemVariables/Domain/Variable/Variable.cs:69`.
- Only a `Boolean` variable may carry `BooleanLabels`: `src/SystemVariables/Domain/Variable/Variable.cs:74`.
- The value case must match the variable type, or `ArgumentException`; `Unset` is accepted for every type. Checked on define and on every set: `src/SystemVariables/Domain/Variable/Variable.cs:81`, `src/SystemVariables/Domain/Variable/Variable.cs:123`, rule at `src/SystemVariables/Domain/Variable/Variable.cs:159`.
- Only a `Defined` variable accepts a new value: `src/SystemVariables/Domain/Variable/Variable.cs:118`.
- Archiving clears the value: `src/SystemVariables/Domain/Variable/Variable.cs:141`.
- The variable's type is fixed at definition: `Type` has a private setter and no method assigns it after `Define` (`src/SystemVariables/Domain/Variable/Variable.cs:35`).

Domain events raised: `VariableDefinedDomainEvent`, `VariableValueChangedDomainEvent`, `VariableArchivedDomainEvent` (see below).

## Value objects

| Name | Wraps | Rule enforced | Citation |
|---|---|---|---|
| `VariableIdentifier` | `Guid` | Non-empty; `New()` mints Guid v7 | `src/SystemVariables/Domain/Variable/VariableIdentifier.cs:17` |
| `FabIdentifier` | `string` | 2 to 32 characters; lowercase letters, digits or `-`; starts with a lowercase letter | `src/SystemVariables/Domain/Variable/FabIdentifier.cs:73` |
| `VariableName` | `string` | Not blank, at most 64 characters; starts with a letter, then letters, digits or `_` | `src/SystemVariables/Domain/Variable/VariableName.cs:20` |
| `VariableType` | `string` | One of `String`, `Number`, `Boolean`; `From` throws on anything else | `src/SystemVariables/Domain/Variable/VariableType.cs:31` |
| `VariableState` | `string` | One of `Defined`, `Archived`; `From` throws on anything else | `src/SystemVariables/Domain/Variable/VariableState.cs:21` |
| `VariableValue` | abstract union | Closed hierarchy (private constructor) of `Unset`, `StringValue`, `NumberValue`, `BooleanValue`. `From(type, raw)` parses a wire string: a Number must parse as an invariant-culture float, a Boolean must be exactly `true` or `false`. `ToWireString` and `Render(BooleanLabels)` convert out; rendering `Unset` throws. | `src/SystemVariables/Domain/Variable/VariableValue.cs:29`, `src/SystemVariables/Domain/Variable/VariableValue.cs:40`, `src/SystemVariables/Domain/Variable/VariableValue.cs:52`, `src/SystemVariables/Domain/Variable/VariableValue.cs:96` |
| `Unset` | none | Singleton (`Instance`); the absence of a value | `src/SystemVariables/Domain/Variable/VariableValue.cs:102` |
| `StringValue` | `string` | No further validation | `src/SystemVariables/Domain/Variable/VariableValue.cs:107` |
| `NumberValue` | `double` | No further validation | `src/SystemVariables/Domain/Variable/VariableValue.cs:109` |
| `BooleanValue` | `bool` | No further validation | `src/SystemVariables/Domain/Variable/VariableValue.cs:111` |
| `BooleanLabels` | two `string` (truthy, falsy) | Each not blank and at most 64 characters; `Default` is "Yes"/"No" | `src/SystemVariables/Domain/Variable/BooleanLabels.cs:20`, `src/SystemVariables/Domain/Variable/BooleanLabels.cs:23` |
| `Creation` | `CreatedAt` + `OperatorIdentifier` | `CreatedAt` not null | `src/SystemVariables/Domain/Variable/Creation.cs:28` |
| `CreatedAt` | `DateTimeOffset` | Normalised to UTC; comparable | `src/SystemVariables/Domain/Variable/CreatedAt.cs:36` |

`OperatorIdentifier`, `IClock` and `Option<T>` come from `Shared.Kernel`.

## Domain events

In-process only (ADR-0040).

| Event | Raised by | When | Citation |
|---|---|---|---|
| `VariableDefinedDomainEvent` | `Variable.Define` | A variable is defined | `src/SystemVariables/Domain/Variable/Variable.cs:95` |
| `VariableValueChangedDomainEvent` | `Variable.SetValue` | A value is set on a `Defined` variable; carries the new value, optional boolean labels and an `Option<DateTimeOffset>` root-ingested time | `src/SystemVariables/Domain/Variable/Variable.cs:125` |
| `VariableArchivedDomainEvent` | `Variable.Archive` | The first archive of a variable | `src/SystemVariables/Domain/Variable/Variable.cs:142` |

Declarations: `src/SystemVariables/Domain/Variable/Events/VariableDefinedDomainEvent.cs:10`, `src/SystemVariables/Domain/Variable/Events/VariableValueChangedDomainEvent.cs:18`, `src/SystemVariables/Domain/Variable/Events/VariableArchivedDomainEvent.cs:11`.

## Integration events

### Published

| Event | Contract | Publishing code |
|---|---|---|
| `SystemVariableDefinedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableDefinedV1.cs` | `src/SystemVariables/Application/EventHandlers/VariableDefinedDomainEventHandler.cs:36` |
| `SystemVariableValueChangedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableValueChangedV1.cs` | `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:40` |
| `SystemVariableArchivedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableArchivedV1.cs` | `src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs:37` |
| `ResolvedOverlayTextChangedV3` | `src/Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV3.cs` | One per affected overlay, after a value change: `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:82`; after an archive: `src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs:68` |

`SystemVariableValueRequestedV1` has its contract under `src/Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs` but is not published by this context; see below.

### Consumed

| Event | Contract | Handler |
|---|---|---|
| `OverlayRevisionPublishedV3` | `src/Shared.Contracts/OverlayDesigner/OverlayRevisionPublishedV3.cs` | `src/SystemVariables/Application/EventHandlers/OverlayRevisionPublishedV3Handler.cs:25` (upserts the overlay's element texts into the reverse index) |
| `OverlayRevisionArchivedV1` | `src/Shared.Contracts/OverlayDesigner/OverlayRevisionArchivedV1.cs` | `src/SystemVariables/Application/EventHandlers/OverlayRevisionArchivedV1Handler.cs:18` (removes the overlay from the reverse index) |
| `SystemVariableValueRequestedV1` | `src/Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs` | `src/SystemVariables/Application/EventHandlers/SystemVariableValueRequestedV1Handler.cs:40` (de-duplicates, then sets the variable value through `SetVariableValueCommandHandler`) |

Publisher of `SystemVariableValueRequestedV1`: Automation, in `PublishVariableAsync` (`src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:163`). The contract file sits in the SystemVariables folder, which is the consuming side.

Downstream consumers of this context's events, read from `Handle(...)` methods: `ResolvedOverlayTextChangedV3` by `src/LayoutComposition/Application/EventHandlers/ResolvedOverlayTextChangedV3Handler.cs:24`; all four published events by AuditObservability (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:52`, `:53`, `:54`, `:56`). AuditObservability also handles `SystemVariableValueRequestedV1` (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:55`).

## Repositories and services

- `IVariableRepository` (`src/SystemVariables/Domain/Variable/IVariableRepository.cs:22`): `GetByIdentifierAsync` (line 24) and `GetByNameAsync(fab, name)` (line 32) return `Option<Variable>`; `ExistsIncludingArchivedAsync(fab, name)` (line 44) answers whether a name is taken including archived variables; `Add` (line 46) and `SaveAsync` (line 48).
- No domain services.

## Omitted

None. Every Domain type of this context is named on this page.

## Questions

- Whether a variable name is unique per fab, and whether an archived variable's name can be reused, is suggested by `IVariableRepository.ExistsIncludingArchivedAsync` (`src/SystemVariables/Domain/Variable/IVariableRepository.cs:44`) but is not enforced inside `Variable`. Where it is enforced (command handler or database index) was not read.
- `FabIdentifier` is declared in SystemVariables' own Domain folder (`src/SystemVariables/Domain/Variable/FabIdentifier.cs:20`) rather than shared; whether other contexts declare an identical type was not checked on this page.
- `Variable.Define` takes `VariableValue?` and `BooleanLabels?` as nullable parameters (`src/SystemVariables/Domain/Variable/Variable.cs:59`, `src/SystemVariables/Domain/Variable/Variable.cs:60`) while `SetValue` takes `Option<DateTimeOffset>` (line 114). CLAUDE.md calls `Option<T>` in Domain an advisory preference; noted only as an observation.
