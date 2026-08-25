# Implementation Plan: Losing the uniqueness race is a refusal, not a fault

**Branch**: `034-uniqueness-refusal` · **Spec**: [spec.md](./spec.md) · **Date**: 2026-08-25
**Issue**: 1869

## Summary

One exception handler, its registration, and its tests.

A uniqueness violation currently reaches the caller as an unhandled **500**.
This makes it a **409** naming what happened, for all twelve unique indexes in
all nine contexts, without the handler needing to know what any of them mean.

**The feature is small. Two things in it are not:** the handler must not swallow
lost updates, and the path it handles only fires in a race.

## Technical Context

**Language**: C# / .NET 10
**Where**: `src/ServiceDefaults/Persistence/`, registered in
`src/ServiceDefaults/AuthenticationDefaults.cs`
**Storage**: PostgreSQL via Npgsql — types reachable **transitively**, verified
by compiling ([research.md](./research.md) §1)
**Testing**: xUnit + Shouldly; integration via the Aspire fixture (ADR-0103)

**No migration. No schema change. No new package.**

## Constitution Check

| Principle | Assessment |
|---|---|
| **§IV Latency budget** | **N/A** — an error path, nothing on the event-to-overlay path |
| **§IX No speculative generality** | One generic refusal rather than a per-constraint map. Argued in the spec's Assumptions from evidence: every context already has its own specific refusal, so a map would restate seven existing messages for a path that fires in a race |
| **No cross-context references** | The handler knows no context's vocabulary. That is the design, not a limitation |
| **Smallest possible change** (ADR-0036) | Two files added, one line of registration. No existing check touched — FR-009 requires it |
| **ADR-0119** | The code must not be, or resemble, a lost update. Enforced by an existing architecture test and asserted directly |

**No violations.** No new ADR: this applies ADR-0047, ADR-0089 and ADR-0119
rather than deciding anything they do not already cover.

## Phases

Three, and the third is the interesting one.

### Phase 1 — The handler

`src/ServiceDefaults/Persistence/UniqueConstraintExceptionHandler.cs`, mirroring
`ConcurrencyConflictExceptionHandler` in shape and response.

**It matches the SQLSTATE, not the exception type.** `PostgresErrorCodes.UniqueViolation`
(`23505`), through both the bare and the `DbUpdateException`-wrapped arms, exactly
as `PersistenceLoopHostedService.IsMissingPartition` already does for check
violations.

This is what makes it correct **regardless of registration order** — see the
first failure mode below.

**Nothing from the exception reaches the response.** Not the constraint name,
not the table, not the detail Postgres supplies. FR-007 is satisfied structurally
by never reading those fields, rather than by remembering to strip them.

### Phase 2 — Registration

One line in `AuthenticationDefaults.cs`, **after**
`ConcurrencyConflictExceptionHandler`.

Order is deliberately not load-bearing — Phase 1 sees to that — but it costs
nothing to have both defences, and a future edit that broadened the match would
still be caught.

### Phase 3 — Evidence

The spec settles what evidence is required and why; this builds it.

**The mapping, proved directly and deterministically.** Given a unique violation,
the response is a `409` carrying the code, with no storage detail anywhere in it.
Given a `DbUpdateConcurrencyException`, the handler declines — which is the
assertion that keeps US2's two refusals apart, and the one that fails if someone
later widens the match to `DbUpdateException`.

**The reachability, proved by an invariant.** Concurrent writers asking for the
same name produce exactly one success and **never a fault** (SC-002). Whether the
race fires on a given run is not asserted.

**Plus the two tests that already constrain this**, named explicitly rather than
left to the full suite: `DirectWriteHonestyIntegrationTests` and
`OutboxSharesTheWritesFateTests` drop a table and require `>= 500`. They must
keep passing. A handler broad enough to catch them would be telling an operator
to choose a different name while the table does not exist.

## Sizing

| Phase | Files | Risk |
|---|---|---|
| 1 | 1 added | **The ordering trap** |
| 2 | 1 changed | Low |
| 3 | 2 added | The race is not forceable |

## Three things most likely to go wrong

1. **The handler swallows lost updates.**
   `DbUpdateConcurrencyException` **derives from** `DbUpdateException` — verified,
   not assumed ([research.md](./research.md) §2). A handler matching the base
   type, registered before `ConcurrencyConflictExceptionHandler`, reports every
   lost update as a name collision. The two refusals US2 exists to separate
   would merge silently, and the caller would be told to choose a different name
   for a change that only needed re-reading. Matching the SQLSTATE removes the
   trap rather than documenting it.

2. **The response leaks the schema.** `PostgresException` carries
   `ConstraintName`, and putting it in the detail is one line and genuinely
   helpful to a developer reading logs. It is also the index name in front of an
   operator, and a description of the storage to anyone else (FR-007). The
   handler must not read it at all — a stripped field is one edit away from
   being unstripped.

3. **The application-level checks get deleted as redundant.** Once the database
   answers properly, seven `*_NAME_TAKEN` checks look like duplication. Removing
   them replaces seven specific, actionable messages with one generic one on
   **every** duplicate rather than on the rare raced one — and it is the exact
   reasoning that produced spec 028's defect, where a rule lived in the index and
   not in the repository. FR-009 forbids it; SC-005 tests it by requiring those
   contexts' suites to pass unchanged.

## Out of scope

Making the check and the write atomic; changing any existing uniqueness check,
index or threshold; other storage failures (foreign keys, check constraints,
deadlocks) — each is a different conversation with the caller; and writes with
no caller to answer.
