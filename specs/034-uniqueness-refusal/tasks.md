# Tasks: Losing the uniqueness race is a refusal, not a fault

**Feature**: `034-uniqueness-refusal` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1869 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge, with no closing keyword needed)*

**13 tasks across three phases.** One handler, one registration line, and its
evidence. The list is short because the feature is.

**Six of the thirteen are assertions about what the handler must *not* do.**
That ratio is right: the risk here is not building the wrong thing, it is
building something slightly too wide and breaking two refusals that already work.

**Nothing to add**: no migration, no schema change, no new ADR, no UI, and **no
package reference** — Npgsql types reach `ServiceDefaults` transitively, verified
by compiling ([research.md](./research.md) §1). No project in this repository
references bare `Npgsql`; do not be the first.

---

## Phase 1: The handler

**Goal**: A uniqueness violation becomes an answer instead of a fault.

- [ ] T001 [US1] Create `src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs` as an `IExceptionHandler`, mirroring `src/ServiceDefaults/Persistence/ConcurrencyConflictExceptionHandler.cs` in shape: `public const string ErrorCode`, a `TryHandleAsync` that writes `ProblemDetails` with the code in `Title`, status **409**, and `Ensure.That(httpContext).IsNotNull()` as the guard
- [ ] T002 [US1] **Match the SQLSTATE, never the exception type**, in `src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs`. Two arms, mirroring `PersistenceLoopHostedService.IsMissingPartition`: a bare `PostgresException` whose `SqlState` is `PostgresErrorCodes.UniqueViolation`, and `DbUpdateException { InnerException: PostgresException inner }` with the same test. EF wraps every provider exception, so the bare arm alone never fires — that comment is already in the codebase and cost somebody a debugging session
- [ ] T003 [US1] Name the code `RESOURCE_ALREADY_EXISTS` in `src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs`. It must **not** end `_STALE` and must contain **none** of the six phrases `tests/Architecture.Tests/StaleCodeConventionTests.cs` treats as lost-update vocabulary — `VERSION_MISMATCH`, `VERSION_CONFLICT`, `VERSION_OUTDATED`, `STALE_VERSION`, `REVISION_MISMATCH`, `CONCURRENCY_CONFLICT`. The near miss is real: this failure **is** caused by concurrency, and naming it for its cause rather than its remedy is exactly what ADR-0119 exists to prevent — a caller told "concurrency" re-reads and retries against a name that is not theirs
- [ ] T004 [US1] Write the detail in `src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs` per [contracts/uniqueness-refusal.md](./contracts/uniqueness-refusal.md): says *name or key* rather than *name* (not every unique index guards an operator-chosen name), says to choose a different one, and says retrying unchanged will be refused again. That last clause is what separates it from the stale-version refusal in the caller's hands
- [ ] T005 [US1] **Read nothing from the exception into the response**, in `src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs` — not `ConstraintName`, not `TableName`, not `MessageText`, `Detail` or `Hint`. FR-007 is satisfied structurally by never touching those fields; a stripped field is one edit away from being unstripped

**Checkpoint**: the handler exists and is narrow. Nothing calls it yet.

---

## Phase 2: Registration

**Goal**: It runs, and it runs where it cannot do harm.

- [ ] T006 [US1] Register `UniqueConstraintExceptionHandler` in `src/ServiceDefaults/AuthenticationDefaults.cs`, **after** `AddExceptionHandler<ConcurrencyConflictExceptionHandler>()`. Order should not be load-bearing — T002's SQLSTATE match sees to that — but both defences cost one line between them, and a later edit that widened the match would still be caught by the concurrency handler running first. Comment the ordering with the reason, not the rule

**Checkpoint**: a uniqueness violation over HTTP answers 409 instead of 500.

---

## Phase 3: Evidence

**Goal**: Prove the mapping, prove the handler declines what it must, and prove nothing else moved.

The spec's *"How this is tested"* section settles what evidence is required and
why it is enough. Read it before changing any of these.

- [ ] T007 [US2] **The ordering trap, asserted in the handler's own tests** — create `tests/ServiceDefaults.Tests/Persistence/UniqueConstraintExceptionHandlerTests.cs` and assert that given a `DbUpdateConcurrencyException` the handler returns **false**. `DbUpdateConcurrencyException` **derives from** `DbUpdateException` (verified empirically: `BaseType=DbUpdateException`), so a type-based match swallows **every lost update** and reports it as a name collision — merging the two refusals US2 exists to keep apart. This assertion is what fails if someone later widens the match, and it belongs here rather than in registration because it must hold regardless of order
- [ ] T008 [P] [US1] In `tests/ServiceDefaults.Tests/Persistence/UniqueConstraintExceptionHandlerTests.cs`, assert the mapping: a `DbUpdateException` wrapping a `PostgresException` with SqlState `23505` produces **409**, `Title` = `RESOURCE_ALREADY_EXISTS`, and `TryHandleAsync` returns **true**. Assert the bare `PostgresException` arm too — cheap, and it documents which arm actually fires
- [ ] T009 [P] [US3] **The leak check, on the rendered JSON** — in `tests/ServiceDefaults.Tests/Persistence/UniqueConstraintExceptionHandlerTests.cs`, serialize the written response body and assert it contains **none** of: the constraint name (`ux_cameras_fab_name_normalized_active`), the table name, and **the colliding value**. Assert the value specifically: it is the one people forget, and Postgres puts it in `Detail` verbatim. **Assert on the JSON, not on the `ProblemDetails` object** — a field set and then not serialized still passes an object-level check
- [ ] T010 [P] [US3] In `tests/ServiceDefaults.Tests/Persistence/UniqueConstraintExceptionHandlerTests.cs`, record **why** T009 checks the value (FR-008): Postgres's detail names the values that collided, and in a multi-fab deployment those can belong to a fab the caller cannot see. Leaking one would turn this refusal into the enumeration oracle several contexts are built to prevent. Write it as a comment on the assertion so nobody relaxes it as over-cautious
- [ ] T011 [US1] **The race, asserted as an invariant** — create `tests/Integration.Tests/ServiceDefaults/UniquenessRaceIntegrationTests.cs`. Fire N concurrent identical creates and assert **exactly one success** and **never a fault** (SC-002). **Do not assert that the race fired.** A test demanding the interleaving occur flakes for reasons unrelated to the code and gets deleted; this one can fail to add information but cannot go green while the bug is present. The spec explains the trade — do not replace it with a forcing test
- [ ] T012 **Prove the handler declines what it must decline.** Temporarily widen T002's match to plain `DbUpdateException`, run `tests/ServiceDefaults.Tests`, watch T007 go red, then revert. Same discipline as spec 031 T010 and spec 033 T006: an assertion that has never failed is a claim, not a check
- [ ] T013 Full verification. **(a)** `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~DirectWriteHonesty"` and the same for `OutboxSharesTheWritesFate` — both drop a table and require **`>= 500`**, and they must stay green: a handler matching `DbUpdateException` broadly turns *"the storage is gone"* into *"choose a different name"*, and their passing is evidence the match is narrow enough. **(b)** The five contexts' uniqueness suites — `tests/CameraCatalog.Application.Tests`, `tests/Automation.Application.Tests`, `tests/SystemVariables.Application.Tests`, `tests/LayoutComposition.Application.Tests`, `tests/OverlayDesigner.Application.Tests` — pass with **`git diff` over those paths empty** (SC-005/FR-009). A context whose check was weakened would answer the generic refusal for *every* duplicate rather than the rare raced one, which is precisely how spec 028's defect happened. **(c)** Release build with analyzers, full unit suite, and the verification note on the PR following [quickstart.md](./quickstart.md)

---

## Dependencies

```
T001 ─▶ T002 ─▶ T003, T004, T005     (the handler)
                   │
                   ▼
                 T006                 (registration)
                   │
          ┌────────┴────────┐
          ▼                 ▼
   T007, T008, T009,      T011        (unit evidence / race)
   T010
          │
          ▼
        T012 ─▶ T013
```

**T012 needs T007**, because it proves that specific assertion fires. **T013 needs
everything.**

---

## Parallel opportunities

- **T003, T004, T005** — one file, but independent decisions; naming, wording and
  the leak guard do not constrain each other.
- **T008, T009, T010** with **T011** — different files entirely
  (`tests/ServiceDefaults.Tests/` vs `tests/Integration.Tests/`).
- **T013(a)** and **T013(b)** — different suites, no shared state.

Genuinely little parallelism here, and saying so is more useful than inventing
some: the chain is one handler and its proof.

---

## Implementation strategy

**MVP is T006.** After registration a uniqueness violation answers 409 instead of
500, which is the whole user-visible change. Everything after it is evidence —
necessary evidence, but not what makes the feature real.

**Do T007 before T008.** The decline assertion is the one that matters, and
writing it first means the mapping test is added to a suite that already refuses
the wrong widening rather than the other way round.

**Do not start by making the race test work.** It is the least informative test
in the feature and the most tempting to over-engineer. Its job is to fail if a
fault ever appears, and nothing more.

---

## Three things most likely to go wrong

1. **The handler swallows lost updates.** `DbUpdateConcurrencyException` derives
   from `DbUpdateException`, and `ConcurrencyConflictExceptionHandler` is
   registered last — so a base-type match placed before it reports every lost
   update as a name collision. The caller is told to choose a different name for
   a change that only needed re-reading. T002 removes the trap by matching the
   SQLSTATE; T007 asserts it; T012 proves the assertion fires.

2. **The response leaks the schema.** `PostgresException.ConstraintName` is one
   line away and genuinely useful in a log. In a response it is an index name in
   front of an operator, a description of the storage to anyone else, and — via
   Postgres's `Detail`, which quotes the colliding values — potentially the
   existence of a resource in a fab the caller cannot see. T005 avoids reading
   the fields at all; T009 asserts on the rendered JSON, because an object-level
   check passes for a field that was set and simply not serialized.

3. **The application-level checks get deleted as redundant.** Once the database
   answers properly, seven `*_NAME_TAKEN` checks look like duplication. Removing
   them replaces seven specific, actionable messages with one generic one on
   **every** duplicate rather than the rare raced one — and it is the same
   reasoning that produced spec 028's defect, where a rule lived in the index and
   not in the repository. T013(b) requires those suites to pass with an empty
   `git diff`.
