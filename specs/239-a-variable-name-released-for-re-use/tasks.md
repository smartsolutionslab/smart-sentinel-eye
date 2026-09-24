# Spec 239 — Tasks

**Issue:** #2446 · **Branch:** `fix/2446-variable-archived-name-disagreement`
**Phase:** 3 (Tasks) · **Date:** 2026-09-24
**Spec:** `specs/239-a-variable-name-released-for-re-use/spec.md` ·
**Plan:** `specs/239-a-variable-name-released-for-re-use/plan.md`

## Declarations

**Phase 4a colour: RED.** Behaviour-changing bug fix. Every new assertion is
observed failing against the branch base before any `src/` edit; the failure text
is quoted verbatim in the PR. A new test arriving green is a phase-4 failure.

**One characterisation component inside the red slice (T004).** Two existing
tests are re-routed from the by-name read to the archived listing. Those are
observed **green before** the `src/` change and **green after**, unmodified
between the two runs. This is not a refactor mixed into a fix: it is the
declared consequence of the narrowing (`spec.md`, *What option 1 costs*), done by
the test-writer so the 4b engineer still edits no test.

**Agents:** `test-writer` (4a: T001–T004) → `backend-engineer` (4b: T005–T006).
The 4b engineer receives the verbatim 4a output and **may not edit any test**.
Backend only — no frontend or infra work.

**Option: 1** — exclude Archived from the by-name read (see `spec.md` reader
table). **New ADR: no.**

**Files phase 4 may touch — exhaustive.**

Production:
- `src/SystemVariables/Application/Queries/Handlers/GetVariableQueryHandler.cs`
- `src/SystemVariables/Application/Queries/GetVariableErrors.cs` — doc comment
  only, optional (`plan.md`)

Tests:
- `tests/SystemVariables.Application.Tests/Queries/GetVariableQueryHandlerTests.cs`
- `tests/Integration.Tests/SystemVariables/SystemVariableLifecycleIntegrationTests.cs`
- `tests/Integration.Tests/SystemVariables/VariableResidueCleanupTests.cs`

Artefacts: `specs/239-a-variable-name-released-for-re-use/*`.

**Nothing else.** Not `VariableRepository.cs`, `IVariableRepository.cs`,
`VariableConfiguration.cs` or any migration (the fix depends on
`ux_system_variables_fab_name_active` as it stands); not
`ListVariablesQueryHandler.cs` (the archived-visibility path); not
`VariableRequests.cs` (its behaviour for an archived name is unchanged in
outcome — `spec.md`); not `VariableSnapshotBuilder.cs`; not `apps/`; not
`src/Automation/`. Touching a file outside the list is stop-and-report.

**Foundational / blocking:** none in the wide sense — no `Shared.*`, AppHost or
Aspire-resource work. T001–T004 block T005. This is a small serial slice;
`[P]` appears only where files are genuinely disjoint (ADR-0109).

---

## User story US1 (P1) — a re-defined archived name is manageable again

### T001 [US1] — unit reds on `GetVariableQueryHandler`

`tests/SystemVariables.Application.Tests/Queries/GetVariableQueryHandlerTests.cs`

Arrange archived rows exactly as `ListVariablesQueryHandlerTests:57-61` does:
`VariableBuilder builder = new VariableBuilder()...; Variable v = builder.Build();
v.Archive(builder.Operator, builder.Clock);` — **no new builder method.**

1. **`Resolves_the_live_variable_when_an_archived_one_holds_the_same_name`** —
   two `"reused"` rows in `munich`, first archived; caller `[munich]`. Expect
   success, `State == "Defined"`, and `VariableIdentifier` equal to the **second**
   row's `Id.Value` (the identifier, not only the state, so the test cannot pass
   by picking the wrong row).
2. **`Reports_an_archived_variable_whose_name_was_never_re_used_as_not_found`** —
   one archived `"gone"` in `munich`. Expect `GetVariableError.VariableNotFound`,
   `Status == HttpStatusCode.NotFound`. Pins the deliberate narrowing.

**Expected red:** (1) fails with `VariableFabAmbiguous` where success was
expected; (2) fails with success where `VariableNotFound` was expected. Any other
failure is a broken arrangement — fix the arrangement. All eight existing tests
in the file green in the same run.

### T002 [P] [US1] — integration red: the issue's reproduction over HTTP

`tests/Integration.Tests/SystemVariables/SystemVariableLifecycleIntegrationTests.cs`

`A_name_freed_by_archiving_is_readable_and_writable_again`, reusing the file's
`DefineNumberAsync` / `ReadAsync` / `UniqueName` and `VariableRequests`; Aspire
fixture (ADR-0103), the file's `ResetSystemVariablesAsync` isolation. As the
single-fab munich admin:

1. `DefineNumberAsync(name, "1")` → 201.
2. `VariableRequests.ArchiveAsync(name)` → 200.
3. `DefineNumberAsync(name, "2")` → **201** — arrange, asserted explicitly with a
   diagnostic message so a failure here cannot masquerade as the defect.
4. `GET /system-variables/{name}` → **200**, `state == "Defined"`,
   `value == "2"`, and `ETag.Tag == "\"{version}\""` from the body. Send this GET
   by hand — **not** through `VariableRequests.VersionAsync`, which would hide
   the status behind `EnsureSuccessStatusCode`.
5. `PUT /system-variables/{name}/value` with `VariableRequests.Conditional(...,
   version)` and body `{ value = "3" }` → 200; a re-read reports `"3"`.

**Expected red at step 4:** `400` whose problem detail reads *"… exists in more
than one of your fabs (munich, munich); name one with ?fabId=."* — the defect and
the missing `Distinct` in one response. Quote the body.

`[P]` against T001 and T003 (different file). **Not** `[P]` against T004's
lifecycle-file edit — same file; write T002 and T004a together.

### T004 [US1] — re-route the two archived-by-name reads (characterisation)

Test-writer only. Both edits keep the **positive** `ShouldBe("Archived")` on the
named row; only the route changes to `GET /system-variables?state=Archived`,
finding the row whose `name` equals the test's name.

- **T004a** `SystemVariableLifecycleIntegrationTests.Archiving_moves_the_variable_out_of_Active`
  (`:88-99`): replace `ReadAsync(variables, name)` with a read of the archived
  listing; assert exactly one row with that name and `state == "Archived"`.
- **T004b** [P] `VariableResidueCleanupTests.A_swept_variable_is_archived_and_gone_from_the_listing`
  (`:36-54`): change `StateAsync` (`:85-92`) to read the archived listing the same
  way. Keep `A_variable_that_cannot_be_archived_fails_the_sweep_rather_than_shrinking_the_count`
  untouched.

**Evidence required in the 4a output:**
- Both re-routed tests **green** against the unfixed base.
- The **original** by-name form of each, run once after T005 (e.g. from `git
  stash` of T004 on the fixed tree), shown **red with 404** — the narrowing
  observed. Quote it; do not commit that state.

`[P]`: T004b is disjoint from every other task's file; T004a shares a file with
T002.

### T005 [US1][US2] — `GetVariableQueryHandler`: exclude Archived, refuse on distinct fabs

`src/SystemVariables/Application/Queries/Handlers/GetVariableQueryHandler.cs`

Backend-engineer, per `plan.md` *The change*:

1. Add `.Where(candidate => candidate.State != VariableState.Archived)` as a
   third `Where`, after the fab and name predicates. Value object, not `.Value`.
   *Why* comment citing FR-005 and `VariableRepository.GetByNameAsync`.
2. Replace `if (matches.Count > 1)` and the inline candidate list with
   `IReadOnlyList<string> fabsHolding = [.. matches.Select(match => match.Fab.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];`
   and `if (fabsHolding.Count > 1)`, passing `fabsHolding` to
   `GetVariableFailures.VariableFabAmbiguous`.

Optionally one sentence in `GetVariableErrors.cs`'s `VariableFabAmbiguous`
summary that the candidates are distinct. **No signature, code, status or
message change.**

Depends on T001–T004. Turns T001, T002, T003 green.

### T006 [US1] — verify

- `SystemVariables.Application.Tests` in full; the SystemVariables integration
  suite (`Integration.Tests` filtered to `SmartSentinelEye.Integration.Tests.SystemVariables`)
  on the Aspire fixture — one stack per machine; check none is already running.
- Every new assertion green; every pre-existing test green **unmodified** except
  the two T004 re-routes, which are unmodified between their before- and
  after-fix runs.
- `git status` shows changes only in the files listed under *Declarations*.
- Format and analyzers clean; Release build green.
- Phase 5 live reproduction per `spec.md` US1 step 3.

---

## User story US2 (P2) — an ambiguity that cannot name one fab as several

### T003 [US2] — unit red on the candidate collection

`tests/SystemVariables.Application.Tests/Queries/GetVariableQueryHandlerTests.cs`

`The_ambiguity_refusal_names_each_holding_fab_exactly_once` — archived `"shared"`
in `munich`, live `"shared"` in `munich`, live `"shared"` in `dresden`; caller
`[munich, dresden]`. Assert `VariableFabAmbiguous` and
`Candidates.ShouldBe(["dresden", "munich"])`. **Assert the collection, not the
message** — a `Contains` passes against the duplicate.

**Expected red:** `Candidates` is `["dresden", "munich", "munich"]`. Written and
run with T001 as one red (same file). Only reachable before T005 — afterwards it
is a guard against reintroducing either half.

---

## Dependencies

```
T001 ─┐  (same file: one red run)
T003 ─┤
T002 ─┤  (T002 + T004a same file)
T004 ─┘
   └──> T005 ──> T006
```

- **T001–T004 precede every `src/` edit.** Red first (ADR-0139).
- `[P]`: T002 ∥ {T001, T003}; T004b ∥ everything else. T001/T003 share a file;
  T002/T004a share a file.
- T006 last.

## Deliberately not covered

- The unreachable `VariableState.Archived` check in
  `SetVariableValueCommandHandler:43-46` — dead code behind `GetByNameAsync`;
  worth its own tidy-up issue, not this fix.
- The unused `getVariable` RTK endpoint.
- Any change to the archived listing, the index, or the resolution path.

## Gate (phase 3)

- [ ] Tasks atomic, each naming files and expected red.
- [ ] `[P]` claims only disjoint files.
- [ ] Colour declared: **RED**, with T004 characterised green→green.
- [ ] Engineer declared: **test-writer (4a) → backend-engineer (4b)**.
- [ ] Option 1, grounded in the reader enumeration; the two re-routed tests
      declared in phase 1, not discovered in phase 4.
- [ ] No new ADR.
- [ ] **#2446 on Project #13** — verified 2026-09-24: open, `bug` + `agent:ready`,
      on project "Smart Sentinel Eye".
