# Spec 239 — A variable name released for re-use

**Issue:** #2446 · **Branch:** `fix/2446-variable-archived-name-disagreement`
**Base:** `origin/develop` (`a54b11d0`) · **Phase:** 1 (Specify) · **Date:** 2026-09-24
**Mirrors:** spec 177 (#2216, merged as `3a7c64ad` / `7557fb19` / `36e77771`) —
the identical defect in Automation. Spec 177 found this one and deliberately
left it for its own issue (`specs/177-a-name-released-for-re-use/spec.md`, *Found
while checking*).
**ADRs:** ADR-0037 (phases and gates), ADR-0047 (`Result<T, Error>` + `ApiError`,
`*Failures` builds the base type), ADR-0052 (xUnit + Shouldly), ADR-0053
(sentence-style test names), ADR-0054 (hand-written builders), ADR-0093
(`Queries/` + `Handlers/` + paired `*Errors.cs`), ADR-0103 (integration against
the Aspire fixture), ADR-0109 (disjoint files for `[P]`), ADR-0113 (two-layer
optimistic concurrency — the `If-Match` this defect makes unreachable), ADR-0114
(fab resolution), ADR-0139 / ADR-0144 (two testing obligations; the architect
declares the colour).
**Constitution:** §II (value objects — compare `VariableState`, never `.Value`),
§Testing (this slice is **red**).
**Specs:** 005 FR-005 (Archived is terminal; the name is freed for re-use),
005 US-archive scenario 3 (re-define after archive succeeds), 014 FR-003
(archiving releases the name within its fab), 014 FR-008 / FR-009 (fab-scoped
reads; another fab's variable reads as not-found).
**§IV latency:** **N/A.** `GET /system-variables/{name}` is an operator-console
read. The kiosk's opening label comes from `GET /system-variables/snapshot` and
the live path from `VariableValueChangedDomainEventHandler`; both resolve through
`IVariableRepository.GetByNameAsync` / `VariableSnapshotBuilder`, which this
slice does not touch.
**New ADR needed:** **No.** Spec 005 FR-005 and spec 014 FR-003 already decided
that an archived name is released; `VariableRepository.GetByNameAsync` and the
partial unique index `ux_system_variables_fab_name_active` already implement it.
This slice makes the one by-name read handler agree.

## The defect in one line

`VariableRepository.GetByNameAsync` excludes Archived rows; `GetVariableQueryHandler`
does not. So re-defining an archived name — which FR-005 explicitly permits —
leaves the new variable unreadable by name, and therefore unwritable through the
API, because `PUT /{name}/value` and `POST /{name}/archive` need the `ETag` only
that read supplies.

## Premise check — every claim in #2446, verified by reading the code

### Confirmed

- **`GetByNameAsync` excludes Archived.**
  `src/SystemVariables/Infrastructure/Persistence/VariableRepository.cs:26-30` —
  `.Where(variable => variable.State != VariableState.Archived)` with the comment
  *"FR-005: archived names are released for re-use; only return non-Archived
  rows."* The interface doc (`IVariableRepository.cs:10-14`) says the same.
- **`GetVariableQueryHandler` does not.**
  `src/SystemVariables/Application/Queries/Handlers/GetVariableQueryHandler.cs:16-19`
  — two predicates, fab scope and name. `State` is never mentioned.
- **Two matches become `VARIABLE_FAB_AMBIGUOUS`.** `:31-36`, keyed on
  `matches.Count > 1`.
- **The `.Distinct()` question: it is missing here too.** The issue asked whether
  `GetVariableError.VariableFabAmbiguous` is fed a de-duplicated list. It is not.
  The error type already takes `IReadOnlyList<string> Candidates`
  (`GetVariableErrors.cs:20`) — the shape spec 177 had to *introduce* for rules
  — but the handler builds that list inline as
  `[.. matches.Select(match => match.Fab.Value).OrderBy(name => name, StringComparer.Ordinal)]`
  (`:35`): one entry **per matching row**, no `Distinct`. So a single-fab
  archived collision renders *"exists in more than one of your fabs (munich,
  munich); name one with ?fabId="*. Same second bug as #2216, one layer
  shallower: the error type is right, its caller is wrong.
- **Naming the fab does not help.** With `?fabId=munich` the query's `Fabs` is
  `[munich]`, both rows still match, and the refusal tells the caller to name a
  fab they already named.
- **The variable is unmanageable, by the same mechanism as #2216.**
  `SetVariableValueCommandHandler:21` and `ArchiveVariableCommandHandler:26`
  resolve through `GetByNameAsync`, so server-side they find the live variable.
  But both compare `variable.Version` against the caller's `If-Match`
  (`:39-42`, `:37-40`), the HTTP endpoints refuse a missing one with 428, and the
  only place a caller learns the version is the `ETag` set in
  `SystemVariableEndpoints.GetOne` (`:305-311`) — the read that now returns 400.
- **The scenario is reachable in four calls:** `POST /system-variables` →
  `POST /{name}/archive` (If-Match from a read) → `POST /system-variables` again
  with the same name (201 — `CrossFabVariableIntegrationTests.An_archived_name_is_free_for_reuse_within_the_same_fab`
  already proves the row lands) → `GET /{name}` → 400.
- **The database already guarantees the invariant the fix relies on.**
  `VariableConfiguration.cs:113-116` — `ux_system_variables_fab_name_active`,
  unique on `(Fab, Name)` filtered `state <> 'Archived'`. Once Archived is
  excluded, two matches can only mean two fabs, which is exactly what the
  refusal's sentence claims.
- **No existing test covers it.** `GetVariableQueryHandlerTests` contains no
  archived variable at all; its one ambiguity test
  (`A_name_held_in_two_of_the_callers_fabs_names_its_candidates`, `:104`) seeds
  munich + dresden, one row each, where `Distinct` makes no difference.

### Found while checking, not in the issue — and where this differs from #2216

**Two existing integration tests read an archived, never-re-used variable by
name and assert `200` with `state: "Archived"`.** #2216's investigation found no
such test for rules; this one does, and it is the one place the fix cannot be a
verbatim mirror:

| Test | What it reads | Why |
|---|---|---|
| `SystemVariableLifecycleIntegrationTests.Archiving_moves_the_variable_out_of_Active` (`:88-99`) | `GET /system-variables/{name}` after archiving; asserts `state == "Archived"` | Verifies the archive landed. The by-name read was a convenience, not a decision: `e5d6f56a` changed only the assertion (`ShouldNotBe("Active")` → `ShouldBe("Archived")`), never the route. |
| `VariableResidueCleanupTests.A_swept_variable_is_archived_and_gone_from_the_listing` (`:36-54`, via `StateAsync` `:85-92`) | the same, for each swept name | Verifies the sweep archived every name. |

Neither is a production reader, and neither pins a decision that archived
variables are readable by name — no spec says so. Spec 005's archive scenario 1
("the admin opens the variable **detail page**") describes a page that was never
built: the RTK `getVariable` endpoint has **zero** production call sites. Both
tests' intent — *this variable reached Archived* — is fully expressible through
`GET /system-variables?state=Archived`, the route the console itself uses and
the one `An_archived_variable_leaves_the_default_listing` (`:132-142`) already
asserts against.

So both tests move their **read** to the archived listing, keeping the
**positive** `state == "Archived"` assertion on the named row. That is declared
here, in phase 1, as intended consequence of the narrowing — not discovered in
phase 4 and adjusted to reach green. See *Phase 4a colour* in `tasks.md` for how
the change is evidenced.

**`VariableRequests.ArchiveAllAsync` changes failure route, not outcome.** For a
name that is already archived it today GETs 200, then the archive POST 404s
(through `GetByNameAsync`) and `EnsureSuccessStatusCode` throws. After the fix
the GET 404s and the same call throws the same `HttpRequestException` one request
earlier. `A_variable_that_cannot_be_archived_fails_the_sweep_rather_than_shrinking_the_count`
uses a never-defined name and is unaffected either way.

**Unreachable, noted, out of scope:** `SetVariableValueCommandHandler:43-46`
checks `variable.State == VariableState.Archived`, but the variable came from
`GetByNameAsync`, which never returns one. Dead branch; not this slice.

## The decision — option 1, and what the investigation found

Every reader of a `system_variables` row, enumerated before choosing:

| Reader | Reads Archived? | Effect of option 1 |
|---|---|---|
| `ListVariablesQueryHandler` (`GET /system-variables`, `?state=`, `?includeArchived=`) | **Yes — the archived-visibility path** (#2015). Default excludes; `state=Archived` or `includeArchived=true` returns them. | **None.** Different handler, untouched. The console's Archived and All tabs (`SystemVariablesPage.tsx:35-41`) read it. |
| `GetVariableQueryHandler` (`GET /system-variables/{name}`) | Yes, by omission — the defect. | Fixed. |
| `DefineVariableCommandHandler`, `SetVariableValueCommandHandler`, `ArchiveVariableCommandHandler` | **No** — all via `GetByNameAsync`. | None; they already hold option 1's semantics. The read comes to agree with every write. |
| `SystemVariableValueRequestedV1Handler` (automation-driven set) | **No** — dispatches `SetVariableValueCommand`. | None. |
| `VariableValueChangedDomainEventHandler:131`, `VariableArchivedDomainEventHandler:80` | **No** — `GetByNameAsync`; the comments say Archived siblings are deliberately None. | None. |
| `VariableSnapshotBuilder` (`/snapshot`, `/resolve`) | `GetByNameAsync` for resolution; `ExistsIncludingArchivedAsync` **only** to label an outcome `Archived` vs `Unknown` (spec 148). | None. It uses the repository, not `IVariableQuerySource`; untouched. |
| `VariableRepository.GetByIdentifierAsync` | Would, if called. | None — **zero callers** in `src/`. |
| Frontend `getVariable` (`apps/shared/src/api/systemVariables.api.ts:163-170`) | — | **None.** `useGetVariableQuery` appears only as a mock in `App.test.tsx:140`. No UI reads a variable by name. |
| Other contexts | — | **None.** No `src/` project calls `/system-variables/{name}` over HTTP; cross-context flow is `Shared.Contracts` messaging. |
| Integration tests (two, above) | Yes, by name. | Re-routed to `?state=Archived`, assertion unchanged. |

**Nothing in production needs an archived variable from a by-name read.**
Option 1, for the four reasons spec 177 gave, each of which holds here verbatim:
it is what FR-005 / 014 FR-003 already decided; it restores the invariant the
refusal sentence depends on (via `ux_system_variables_fab_name_active`); it makes
the read agree with the repository, the index and every write handler; and it is
one `Where` clause.

A fifth reason specific to this context: **the two contexts would otherwise
disagree with each other.** Since #2216, `GET /rules/{name}` 404s an archived
rule. Leaving `GET /system-variables/{name}` returning it would make the same
lifecycle state mean different things on two sibling routes.

**Option 2 considered and rejected** — *prefer a live match, fall back to an
archived one*. It would keep both tests unmodified and avoid the narrowing, but
the fallback re-opens exactly the rows the unique index does not govern: archive,
re-define, archive again leaves two Archived rows in one fab, and the fallback
needs a tie-break the domain has never defined. That is new behaviour to invent,
not a defect to fix.

**What option 1 costs, stated plainly:** `GET /system-variables/{name}` stops
answering for an archived variable whose name was never re-used — `200` today,
`404 VARIABLE_NOT_FOUND` after. Deliberate, pinned by its own scenario below. The
row stays fully readable via `GET /system-variables?state=Archived`, which is
where the console already reads it.

## User story US1 (P1) — a re-defined archived name is manageable again

As an operator who archived a variable and defined a new one with the same name —
which FR-005 says I may — I can read it by name, get its `ETag`, and set its
value or archive it.

Independently shippable: one handler, one `Where` clause, unit + integration
tests. No migration, no contract, no frontend.

### Acceptance scenarios

**Happy — the live variable wins.**

```gherkin
Given a variable "reused" in fab "munich" that is Archived
  And a second variable "reused" defined afterwards in "munich"
When an operator holding only "munich" reads GET /system-variables/reused
Then the response is 200
  And the body's variableIdentifier is the second variable's
  And state is "Defined"
  And the ETag matches the body's version
```

**Happy — the consequence: the re-defined variable can be written.**

```gherkin
Given the 200 and ETag above
When the operator PUTs /system-variables/reused/value with that ETag as If-Match
Then the response is 200
  And a re-read reports the new value
```

**Happy — naming the fab resolves it too.**

```gherkin
Given the same two rows
When the operator reads GET /system-variables/reused?fabId=munich
Then the response is 200 for the second variable
```

**Conflict — genuine cross-fab ambiguity still refuses.**

```gherkin
Given "shared" Defined in "munich" and "shared" Defined in "dresden"
When an operator holding both reads GET /system-variables/shared without fabId
Then the response is 400 VARIABLE_FAB_AMBIGUOUS naming dresden and munich
```

**Conflict — an archived namesake does not rescue or distort a real collision.**

```gherkin
Given "shared" Archived in "munich", "shared" Defined in "munich",
  And "shared" Defined in "dresden"
When an operator holding both reads without fabId
Then the response is 400 VARIABLE_FAB_AMBIGUOUS
  And its candidates are exactly ["dresden", "munich"]
```

**Bad request — an archived variable whose name was never re-used is not-found.**

```gherkin
Given a single variable "gone" in "munich", Archived
When an operator holding "munich" reads GET /system-variables/gone
Then the response is 404 VARIABLE_NOT_FOUND
```

The deliberate narrowing, recorded as intended behaviour so it cannot later be
mistaken for a regression.

**Bad request — an illegal name.** `GET /system-variables/9-not-legal` →
`400 VARIABLE_INVALID_INPUT` from `BoundaryParse` (`SystemVariableEndpoints.cs:284-291`),
before the handler runs. Unchanged; not re-tested here.

**Auth — another fab's variable reads as not-found.** Spec 014 FR-009,
`Another_fabs_variable_is_reported_as_not_found` (`GetVariableQueryHandlerTests:82`)
stays green unmodified; the new predicate sits after the fab scope and must not
reorder or weaken it (ADR-0114). `sse.variables.read` scope unchanged. No
`Idempotency-Key` — reads (ADR-0142).

### Independent end-to-end test procedure

1. **Red, before any `src/` edit** — the new unit and integration assertions fail
   as predicted in `tasks.md`; every pre-existing test is green in the same run.
2. **Green after** the `Where` clause and the distinct-fab condition.
3. **Live stack, phase 5** — as a munich admin: define `v<guid>`, read it, archive
   it with the `ETag`, define it again, `GET` it (expect 200 + `ETag`), `PUT` a
   value with that `ETag` (expect 200). Quote the status codes and the `ETag`.

## User story US2 (P2) — an ambiguity that cannot name one fab as several

As anyone reading `VARIABLE_FAB_AMBIGUOUS`, the fabs it names are distinct and
there are always at least two, so *"exists in more than one of your fabs"* is true
whenever it is printed.

Separate from US1 because US1 removes today's reachable cause and US2 removes the
capability: after both, no future input can make the sentence false.

### Acceptance scenarios

```gherkin
Given matches for "shared" whose fabs, per row, are munich, munich, dresden
When the by-name read evaluates ambiguity
Then it refuses because two distinct fabs hold the name
  And the candidates are exactly ["dresden", "munich"]
```

```gherkin
Given every match lies in one fab
Then VARIABLE_FAB_AMBIGUOUS is not returned
```

**Bad request / auth — N/A.** No new input, no new boundary.

### Independent end-to-end test procedure

Assert the `Candidates` collection, never the rendered message: a
`Message.ShouldContain("munich")` passes against `(munich, munich)` and cannot see
the defect. The US1 cross-fab-plus-archived scenario is the red for US2 — it is
only reachable while archived rows are still visible to the query.

## Locked choices

xUnit + Shouldly + `TestVariableQuerySource` (ADR-0052); the existing
`VariableBuilder` plus the aggregate's own `Archive(builder.Operator,
builder.Clock)`, exactly as `ListVariablesQueryHandlerTests.Filters_by_state_when_a_state_is_provided`
(`:57-61`) does — **no new builder method**. Sentence-style names (ADR-0053).
Integration on the Aspire fixture (ADR-0103), reusing
`SystemVariableLifecycleIntegrationTests`' `DefineNumberAsync` / `ReadAsync` /
`NamesAsync` / `UniqueName` and `VariableRequests` rather than a new file.

Compare the value object (`candidate.State != VariableState.Archived`), the exact
expression `VariableRepository:30` already sends through EF (§II).

**No new ADR. No migration. No contract change. No frontend change.** The error
type `GetVariableError.VariableFabAmbiguous(string, IReadOnlyList<string>)`
already has the right shape and does not move — code, status and wording stay.

## Explicitly not in scope

- `ListVariablesQueryHandler` — the archived-visibility path; the reason option 1
  is safe.
- `VariableRepository`, `VariableConfiguration`, any migration — the fix relies on
  both as they stand.
- `VariableSnapshotBuilder` and the resolution path (§IV).
- The unreachable Archived branch in `SetVariableValueCommandHandler`.
- The unused frontend `getVariable` endpoint.
- Extracting a shared "fab candidates" helper: one handler uses it here (spec 177
  extracted one because two handlers did), and none may cross the context
  boundary into Automation.
