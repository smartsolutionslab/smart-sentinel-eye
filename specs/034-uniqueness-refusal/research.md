# Phase 0 Research: Losing the uniqueness race is a refusal, not a fault

**Feature**: `034-uniqueness-refusal` · 2026-08-25

Six questions. **The second confirmed the defect this feature is most likely to
ship**, and the fifth found that the existing test suite already constrains the
design in a way nobody wrote down.

---

## 1. Npgsql types are visible from ServiceDefaults, transitively

**Verified by compiling**, not by reading a manifest. A throwaway file in
`ServiceDefaults` referencing `Npgsql.PostgresErrorCodes.UniqueViolation` and
`Npgsql.PostgresException` builds clean. `WolverineFx.Postgresql` brings Npgsql,
and transitive package references are compile-visible.

**Decision**: use the transitive reference. Do **not** add an explicit
`PackageReference`.

**Rationale**: it is what the codebase already does. No project references bare
`Npgsql` —

```sh
grep -rln "Npgsql" src/*/*.csproj
# (no output)
```

— and `EventIngestion.Infrastructure`, the one place using
`PostgresErrorCodes` today, gets it transitively through
`Npgsql.EntityFrameworkCore.PostgreSQL`. Adding an explicit reference would also
mean pinning a bare `Npgsql` version in `Directory.Packages.props` that has to
stay compatible with whatever the EF provider pulls.

**Alternative considered**: explicit `PackageReference` for robustness against
`WolverineFx.Postgresql` changing its dependencies. Rejected as inventing a
convention for one file, against an existing precedent — but noted, because if
that dependency ever moves, this breaks at compile time rather than silently.

---

## 2. The handler ordering defect is real

**`DbUpdateConcurrencyException` derives from `DbUpdateException`.** Verified
empirically rather than from memory:

```
BaseType=DbUpdateException; IsSubclassOfDbUpdate=True
```

Registration order in `src/ServiceDefaults/AuthenticationDefaults.cs`, unchanged
today:

```csharp
builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
builder.Services.AddExceptionHandler<FabAuthorizationExceptionHandler>();
builder.Services.AddExceptionHandler<UnattributableOperatorExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyConflictExceptionHandler>();
```

ASP.NET Core calls handlers in registration order and stops at the first
returning `true`. So a handler matching **`DbUpdateException`** registered
*before* `ConcurrencyConflictExceptionHandler` would swallow **every lost
update** and report it as a uniqueness collision — the two refusals the spec's
US2 exists to keep apart, silently merged.

**Decision**: two independent defences, because either alone is fragile.

1. **Match on the SQLSTATE, not on the exception type.** The handler answers
   only for `PostgresErrorCodes.UniqueViolation` (`23505`). A
   `DbUpdateConcurrencyException` arises from an unexpected affected-row count
   and carries no Postgres error at all, so it never matches. This makes the
   handler correct **regardless of registration order**.
2. **Register it last anyway.** Order should not be load-bearing, and it is not
   — but a future edit that broadened the match would then still be caught by
   the concurrency handler running first.

**Alternative considered**: match `DbUpdateException` and early-return for
`DbUpdateConcurrencyException`. It works, and it makes correctness depend on
remembering the subclass relationship at the top of the method. The SQLSTATE
match does not require anyone to know it.

---

## 3. The envelope trap, already documented once

`PersistenceLoopHostedService.IsMissingPartition` is the only existing precedent,
and its comment records the trap:

> *"Unwrapped rather than matched directly: the insert goes through EF, which
> wraps every provider exception in `DbUpdateException`. A
> `catch (PostgresException)` never fires — it was written that way first, and
> the envelope got the same 'something faulted' line this exists to replace."*

```csharp
private static bool IsMissingPartition(Exception exception) => exception switch
{
    PostgresException postgres => postgres.SqlState == PostgresErrorCodes.CheckViolation,
    DbUpdateException { InnerException: PostgresException inner } =>
        inner.SqlState == PostgresErrorCodes.CheckViolation,
    _ => false,
};
```

**Decision**: mirror this shape exactly, with `UniqueViolation`. Both arms —
bare and wrapped — because the bare case is cheap and the wrapped case is the one
that actually happens.

**Not** extracted into a shared helper. Two call sites in different contexts,
each three lines, and the shared version would live in `ServiceDefaults` where
`EventIngestion` would have to reach for it. Worth revisiting at a third caller.

---

## 4. The response shape to match

`ConcurrencyConflictExceptionHandler` writes `ProblemDetails` directly:

```csharp
httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
ProblemDetails problem = new()
{
    Title = ErrorCode,
    Detail = "The resource was modified by another writer. Re-read it and reapply the change.",
    Status = StatusCodes.Status409Conflict,
};
await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken: cancellationToken);
```

**Decision**: identical shape. `Title` carries the code — which is what
`problemCode()` on the client reads (ADR-0089/0119), so this is not cosmetic.

**Consequence for FR-005**: both refusals are `409`. They are distinguished by
**code**, which is exactly what ADR-0119 established and what spec 033 asserted
for `CAMERA_NAME_TAKEN` against `CAMERA_VERSION_STALE`. The status carrying no
distinction is the convention, not an oversight.

---

## 5. The existing suite already constrains the design

**Nothing asserts a 500 for a uniqueness violation.** But two EventIngestion
tests do assert `>= 500`:

| Test | Provokes |
|---|---|
| `DirectWriteHonestyIntegrationTests` | `DROP TABLE events_<fab>` |
| `OutboxSharesTheWritesFateTests` | the same, for the outbox |

Those are **undefined-table** (`42P01`), not unique violations, so a
SQLSTATE-specific handler leaves them untouched.

**This is the finding**: a handler matching `DbUpdateException` broadly would
turn *"the storage is gone"* into *"that name is already taken"* — telling an
operator to pick a different name while the table does not exist. These tests
would catch it, which is a third argument for the SQLSTATE match and worth
knowing before the design is chosen rather than after a red suite.

**Decision**: no existing test needs updating. Their continued passing is
evidence the handler is narrow enough, so they are named in the plan as a check
to run rather than left to the full suite.

---

## 6. The architecture tests are unaffected, but only just

`StaleCodeConventionTests` has two halves:

- **The offender check** matches codes containing `VERSION_MISMATCH`,
  `VERSION_CONFLICT`, `VERSION_OUTDATED`, `STALE_VERSION`, `REVISION_MISMATCH`
  or **`CONCURRENCY_CONFLICT`**.
- **The inventory check** pins an exact set of eight `_STALE` codes.

**Decision**: the new code must contain **none** of those six substrings and must
not end `_STALE`. `RESOURCE_ALREADY_EXISTS` satisfies both; a name like
`UNIQUE_CONSTRAINT_CONFLICT` would be fine too, but anything reaching for
"concurrency" would trip the offender check — correctly, because a caller told
this is a concurrency problem would re-read and retry.

Worth noting the near miss: this **is** a concurrency-caused failure. It is
provoked by two writers racing. The convention deliberately reserves that
vocabulary for **lost updates**, and this is not one — the caller's own resource
was never touched. Naming it for its cause rather than for its remedy is the
mistake ADR-0119 exists to prevent.

---

## Summary of decisions

| # | Question | Decision |
|---|---|---|
| 1 | Npgsql reference | Transitive, matching every other project. Verified by compiling |
| 2 | Handler ordering | Match the **SQLSTATE**, so order cannot matter. Register last anyway |
| 3 | Envelope | Mirror `IsMissingPartition`'s two-arm switch. No shared helper at two callers |
| 4 | Response shape | Identical to `ConcurrencyConflictExceptionHandler`; code in `Title` |
| 5 | Existing tests | None need changing. Two `>= 500` tests become a design check |
| 6 | Naming | Avoid all six stale-vocabulary substrings and the `_STALE` suffix |

**No migration, no schema change, no new dependency.**
