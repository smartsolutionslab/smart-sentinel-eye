# Plan 235 — The branches the lookup never reaches

**Spec**: [spec.md](spec.md) · **Issue**: #2359 · **Phase**: 2 (Plan)
**Base**: `396c4fa7`

## Summary

Delete two unreachable `variable.State == VariableState.Archived` checks on the push side of
SystemVariables, after pinning — green — the behaviour around them with three characterisation tests.
The third site the issue names (`SetVariableValueCommandHandler:43`) is left to #2200's human
decision (spec §2).

## Constitution / ADR check

| Gate | Result |
|---|---|
| §I bounded contexts — no cross-context reference | Pass. SystemVariables `Application` + its test project only. |
| §II primitives in the domain model | Not touched (no Domain file changes). |
| §IV latency | Event → overlay state leg; one in-memory comparison removed per sibling; no I/O change. Not claimed as a gain. |
| §Testing — behaviour-preserving | Characterisation: C1–C3 captured green before, unmodified and green after (spec §5, §6). |
| ADR-0144 — lane does not make decisions | Site 1 excluded because #2200 reserves it for a human. |
| ADR-0036 — smallest change | Two `if` blocks removed, two one-line *why* comments, three tests. No new type, method or abstraction. `ExistsIncludingArchivedAsync` not used, not duplicated. |
| ADR-0048/0141 — `Option<T>` | Unchanged: both sites keep `Option<Variable> other = … GetByNameAsync(…)` and `!other.HasValue`. |
| ADR-0053/0054 — test naming, builders | Sentence-style names; `VariableBuilder` + `Variable.Archive(op, clock)`; no new builder method needed. |
| New ADR | None. |

## Bounded context and layers

**SystemVariables / Application** — `EventHandlers/`. Nothing in Domain, Infrastructure, Api,
`Shared.Contracts`, AppHost or frontend. No messaging change: both handlers still publish exactly the
same `SystemVariableArchivedV1` / `SystemVariableValueChangedV1` and `ResolvedOverlayTextChangedV1`
integration events with identical payloads.

## Entities / invariants relied on (unchanged)

- `IVariableRepository.GetByNameAsync` returns `None` for an Archived row (FR-005). **Contract not
  edited.**
- `Variable.Archive` sets `Value = VariableValue.Unset.Instance` (`Variable.cs:141`) — why a tracked,
  in-memory-archived instance is already skipped by the unset check (spec §1.1).
- `VariableArchivedDomainEventHandler.cs:70` name-skip — the real guard for FR-014; pinned by C3.

## Changes

### Tests first (phase 4a, `test-writer`) — `tests/SystemVariables.Application.Tests/EventHandlers/`

**`VariableValueChangedDomainEventHandlerTests.cs`**

- **C1** `A_sibling_archived_and_defined_again_resolves_to_the_live_one`
  - Repo: `shift` (String, value `"A"`, fab munich) → `.Archive(op, clock)`; a second `shift`
    (String, value `"B"`, munich) added.
  - Index: overlay → `"{{shift}} / {{oee}}"`.
  - Handle `VariableValueChangedDomainEvent` for `oee` (Number, 82.5, munich).
  - Assert single `ResolvedOverlayTextChangedV1` with `ResolvedText == "B / 82.5"`.

**`VariableArchivedDomainEventHandlerTests.cs`**

- **C2** `A_sibling_archived_and_defined_again_resolves_to_the_live_one`
  - Same repo seeding as C1; index overlay → `"{{shift}} / {{oee}}"`.
  - Handle `VariableArchivedDomainEvent` for `oee` (munich).
  - Assert `ResolvedText == "B / {{oee}}"`.
- **C3** `The_variable_being_archived_stays_literal_even_while_its_row_is_still_readable`
  - Repo: `oee` (Number, value 82.5, munich) **left Defined** — models the database row at dispatch
    time, before commit (spec §1.1, untracked case). A short *why* comment in the test says so.
  - Index overlay → `"OEE: {{oee}}%"`.
  - Handle `VariableArchivedDomainEvent` for `oee` (munich).
  - Assert `ResolvedText == "OEE: {{oee}}%"`.

Run the SystemVariables Application test project against **unmodified** production code; all three
green. Then the two counterfactuals from spec §5 (C3 red with `:70` removed; C1+C2 red with the fake's
Archived filter removed), each reverted with an empty `git diff` confirmed. Return verbatim output of
all three runs.

### Production (phase 4b, `backend-engineer`) — `src/SystemVariables/Application/EventHandlers/`

**`VariableArchivedDomainEventHandler.cs`** — delete `:88-91`:

```csharp
                if (variable.State == VariableState.Archived)
                {
                    continue;
                }
```

and add one comment line above the `if (!other.HasValue)` at `:82`, e.g.
`// None also covers an Archived sibling: GetByNameAsync excludes them (FR-005).`
The `Variable variable = other.Value;` line stays (still read by the unset check and the snapshot).

**`VariableValueChangedDomainEventHandler.cs`** — delete `:138-141` (same four lines), same one-line
comment above `if (!other.HasValue)` at `:132`. The `BuildSnapshotAsync` `<summary>` ("Unset /
archived / missing variables are absent from the snapshot") stays — still true.

**Check after the edit**: the `using SmartSentinelEye.SystemVariables.Domain.Variable;` in both files
remains needed (`Variable`, `VariableName`, `VariableValue`). `VariableState` is no longer referenced in
either file — confirm with a grep; nothing to remove since it lives in the same namespace.

**Not touched**: `SetVariableValueCommandHandler.cs`, `SetVariableValueErrors.cs`,
`IVariableRepository.cs`, `VariableRepository.cs`, `InMemoryVariableRepository.cs`,
`VariableSnapshotBuilder.cs`, `:70` in the archived handler, `:109` in the value-changed handler.

## Verification (phase 5)

- `dotnet test tests/SystemVariables.Application.Tests` — C1–C3 green, **unmodified** (diff of the
  test files between the 4a commit and the tip is empty).
- `dotnet build -c Release` clean (analyzers; `TreatWarningsAsErrors`).
- `grep -n "VariableState.Archived" src/SystemVariables/Application/EventHandlers/` → no matches.
- No end-to-end run required for a behaviour-preserving deletion on a unit-covered path; state that
  explicitly in the verification note. Latency: cite the leg, claim no change.

## Parallelism

Single slice, single context, two production files and two test files. No `[P]` fan-out worth
having: 4b depends on 4a's green capture, and the two production edits are too small to split.
