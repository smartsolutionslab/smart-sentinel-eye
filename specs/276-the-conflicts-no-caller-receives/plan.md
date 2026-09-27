# Plan 276 — The conflicts no caller receives

**Spec**: [spec.md](spec.md) · **Issue**: #2200 · **Phase**: 2 (Plan)
**Base**: `1a51bd18`

## Summary

Pin, green, the 404 that callers already receive when they set a value on an archived variable or
publish an archived rule (two Aspire-fixture integration tests). Then delete the two 409 error types
no caller can receive, and the handler code that returns them; state on both repository contracts that
name lookups treat an archived name as gone, deliberately; and drop the stale `RuleAlreadyArchived`
mention from one architecture-test mechanism string. The domain invariants that refuse the same
transitions stay.

## Phase 4a colour — declared

**Characterisation, observed green. There is no red task in this spec.**

| Part | Kind | How it is proven |
|---|---|---|
| Delete `VariableArchived` / `RuleAlreadyArchived` + handler branches | **Behaviour-preserving** (dead code) | C1, C2 green before; counterfactual 1 shows each goes red (409) when the lookup filter is removed, i.e. each test sits exactly on the path the deleted branch claimed; C1, C2 and both contexts' unit suites green **unmodified** after. |
| Repository doc comments | **Behaviour-documenting** (no executable change) | Comment-stripped hash of the four touched files identical before/after, plus a counterfactual that the hash moves when a code token moves. No test asserts prose. |
| Architecture-test mechanism string | **Message-only** | `Architecture.Tests` green; the route stays in `CanAnswerConflict`; no assertion edited. |

A test arriving **red** in 4a means the premise is wrong: stop and report, do not "fix" it into green.
An assertion needing an edit after 4b means behaviour moved: **block**.

## Constitution / ADR check

| Gate | Result |
|---|---|
| §I bounded contexts — no cross-context reference | Pass. Each edit stays inside its own context; the one test file touching both (`Architecture.Tests`) already reads both by source scan. |
| §II primitives in the domain model | Not touched. `IVariableRepository` / `IRuleRepository` gain XML comments only; `PrimitiveBoundaryTests` unaffected. |
| §IV latency | Event → overlay state leg: one in-memory comparison removed from `SetVariableValueCommandHandler`. No I/O change. Not claimed. |
| §Testing — behaviour-preserving | Characterisation (above). |
| ADR-0047 / ADR-0089 — `Result<T, Error>`, `ApiError` | Each error hierarchy keeps its base record and its `*Failures` factory class; one variant and its factory method removed from each. The factory pattern (base-typed return for invariant generics) is unchanged. |
| ADR-0041 — repository contract in Domain | The contract statement goes on the Domain interface, where handlers read it; Infrastructure's inline comment points at it rather than restating it. |
| ADR-0048 / ADR-0141 — `Option<T>` | Unchanged: both handlers keep `Option<T> found = … GetByNameAsync(…)` and `!found.HasValue → NotFound`. |
| ADR-0113 — `If-Match` 409 | `VariableStale` / `RuleStale` untouched; still the routes' 409. |
| ADR-0036 — smallest change | Deletions + comments + one string + two tests. No new type, method, abstraction or lookup. |
| ADR-0053 / ADR-0103 — naming, integration via Aspire fixture | Sentence-style names; tests added to existing fixture-backed classes. |
| ADR-0144 — lane may not make decisions | Supervised lane; the decision is the product owner's, recorded in #2200. |
| New ADR | None (spec §9). |

## Bounded contexts and layers

### SystemVariables

- **Application** — `Commands/SetVariableValueErrors.cs`: remove `VariableArchived` record (`:15-19`)
  and `SetVariableValueFailures.VariableArchived` (`:50-51`).
  `Commands/Handlers/SetVariableValueCommandHandler.cs`: remove the `if (variable.State ==
  VariableState.Archived)` block (`:43-46`). The `VariableStale` check (`:39-42`) and everything after
  stay. That block is the file's only use of `VariableState`; the `using` for
  `SmartSentinelEye.SystemVariables.Domain.Variable` stays (still needed for `Variable`,
  `VariableValue`, `VariableName`).
- **Domain** — `Variable/IVariableRepository.cs`: add a `<summary>` on `GetByNameAsync` (see wording
  below). The type-level paragraph at `:11-13` may be tightened to point at it; no signature change.
- **Infrastructure** — `Persistence/VariableRepository.cs:26`: the inline comment stays one line and
  references the contract (e.g. "Archived rows excluded by contract — see IVariableRepository.GetByNameAsync.").
  Query unchanged.

### Automation

- **Application** — `Commands/PublishRuleCommand.cs`: remove `RuleAlreadyArchived` record (`:20-24`)
  and `PublishRuleFailures.RuleAlreadyArchived` (`:49-50`).
  `Commands/Handlers/PublishRuleCommandHandler.cs`: replace the `try { rule.Publish(clock); } catch
  (InvalidOperationException) { … }` (`:39-46`) with the bare call `rule.Publish(clock);`.
- **Domain** — `Rule/IRuleRepository.cs`: `<summary>` on `GetByNameAsync`, same shape as
  SystemVariables. `Rule.Publish` (`Rule.cs:98-113`) **not edited**.
- **Infrastructure** — `Persistence/RuleRepository.cs:27`: inline comment references the contract;
  the "fab first" comment (`:28-29`) and the query stay.

### Architecture.Tests

- `ConcurrencyConflictDeclarationTests.cs:301` — mechanism string becomes
  `"refusal (PublishRuleFailures.RuleStale); lost update "`. Entry, route and every other string
  unchanged. Do **not** remove the entry (spec §1.2).

### Integration.Tests (4a)

- `SystemVariables/SystemVariableLifecycleIntegrationTests.cs` — C1.
- `Automation/RuleLifecycleIntegrationTests.cs` — C2.

## Contract wording (FR-006) — guidance, engineer may tighten

On `GetByNameAsync` in each interface:

> Returns the non-Archived variable (rule) of this name in this fab. Archived rows are excluded
> deliberately: archiving releases the name for re-use (spec 005 FR-005 / spec 007 FR-002), so after
> archiving the name resolves to nothing. Every command that addresses a variable (rule) by name —
> define (create), set value (publish), archive — therefore sees an archived one as not found, and the
> caller receives a not-found error, not a conflict. Use `GetByIdentifierAsync` (or, for variables,
> `ExistsIncludingArchivedAsync`) where an archived row must be seen.

Spec numbers are acceptable in code comments here — the existing comments already cite FR-005/FR-002
and spec 013/014; issue and task numbers are not.

## Entities / invariants relied on (unchanged)

- `Variable.SetValue` throws `InvalidOperationException` when `State != Defined` (`Variable.cs:118-122`).
- `Rule.Publish`: `Active` → no-op; `Archived` → `InvalidOperationException` (`Rule.cs:101-109`);
  pinned by `RuleStateMachineTests.Publish_on_an_Archived_rule_throws` (`:84-91`).
- Both `GetByNameAsync` implementations and both fakes exclude Archived.
- No domain event, integration event, or `Shared.Contracts` type changes; no messaging change.

## Characterisation tests — shape (4a)

**C1** `Setting_a_value_on_an_archived_variable_is_refused_as_not_found`:
define a unique variable (existing helpers in `VariableRequests` / the class), archive it with
`VariableRequests.ArchiveAsync`, then `PUT /system-variables/{name}/value` via
`VariableRequests.Conditional(HttpMethod.Put, name, "value", postArchiveVersion)` with a type-valid
body. Assert 404 and `code == "VARIABLE_NOT_FOUND"`. `postArchiveVersion` must be the **current**
version of the archived row — read it from the archived listing (`/system-variables?state=Archived`,
as `ArchivedRowsNamedAsync` already does) rather than hard-coding it; `VariableRequests.VersionAsync`
cannot be used because it reads by name and would itself 404.

**C2** `Publishing_an_archived_rule_is_refused_as_not_found`:
create a unique rule, archive with `RuleRequests.Conditional(name, "archive", 0)` (as
`A_name_freed_by_archiving_is_readable_and_publishable_again` does), then publish with
`RuleRequests.Conditional(name, "publish", postArchiveVersion)`. Assert 404 and
`code == "RULE_NOT_FOUND"`. Take `postArchiveVersion` from the archive response (ETag / body) if it
carries one; otherwise from a listing that includes archived rules. `RuleRequests.VersionAsync` reads by
name and cannot be used.

The post-archive version is not incidental: it is what makes counterfactual 1 reach the archived
branch rather than the stale one (spec §3).

## Verification environment notes

- Integration tests need the Aspire stack. **One machine, one stack**: check no other AppHost /
  testhost is running before booting; stop the stack before building (MSB3027).
- Counterfactual restores: `git checkout -- <file>`, `git diff` empty, `dotnet build --no-incremental`.

## Commits (ADR-0030; each builds on its own — rebase-merge lands them individually)

1. `test(integration): pin 404 for mutating an archived variable or rule` — C1, C2.
2. `refactor(system-variables): delete the unreachable VariableArchived conflict`
3. `refactor(automation): delete the unreachable RuleAlreadyArchived conflict` — includes the
   `ConcurrencyConflictDeclarationTests` string (it names the type being deleted).
4. `docs(system-variables,automation): state that name lookups treat an archived name as gone`

## Tracking

Issue #2200 **is already on Project #13** (verified 2026-09-27 via `gh issue view 2200 --json
projectItems` → "Smart Sentinel Eye", project 13). No new issue; no per-task issues.
