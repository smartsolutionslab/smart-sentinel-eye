# Spec 235 — The branches the lookup never reaches

**Issue**: #2359 · **Branch**: `fix/2359-unreachable-archived-branches` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `396c4fa7` (`origin/develop`, fetched 2026-09-24)
**Context**: SystemVariables — `Application` layer only. No Domain, Infrastructure, Api, contract or frontend change.
**Engineer**: `backend-engineer` · **Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **characterisation, observed green** (§6). Nothing a caller or a wall observes changes.
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane; "implements decisions, does not make them"),
ADR-0036 (smallest change; a refactor changes shape, not behaviour), ADR-0139 (§Testing — two
obligations), ADR-0048/0141 (`Option<T>` from repository lookups), ADR-0115 (siblings resolve in the
fab that changed)
**Constitution**: §IV — **no leg's budget changes**. Both edited handlers sit on the
`event → overlay state` leg; the change deletes one in-memory comparison per sibling and adds no I/O.
§Testing — behaviour-preserving, so the covering tests are captured green first and pass unmodified.
**New ADR needed**: **No**.

---

## 1. The premise, re-checked against `396c4fa7`

`IVariableRepository.GetByNameAsync` excludes Archived rows by contract
(`src/SystemVariables/Domain/Variable/IVariableRepository.cs:11-13`) and in the implementation
(`src/SystemVariables/Infrastructure/Persistence/VariableRepository.cs:30`,
`.Where(variable => variable.State != VariableState.Archived)`). The test fake mirrors it
(`tests/SystemVariables.Application.Tests/Fakes/InMemoryVariableRepository.cs:26-27`).

| # | Issue claim | Status at `396c4fa7` |
|---|---|---|
| 1 | `SetVariableValueCommandHandler.cs:43`, fed by `:21` | **Holds exactly.** Unreachable. **Out of scope — see §2.** |
| 2 | `VariableArchivedDomainEventHandler.cs:81`, fed by `:73` | **Holds; lines drifted to `:88`, fed by `:80`.** Unreachable — but see §1.1: for this site it is the name-skip at `:70`, not the lookup alone, that makes it so. |
| 3 | `VariableValueChangedDomainEventHandler.cs:131`, fed by `:124` | **Holds; lines drifted to `:138`, fed by `:131`.** Unreachable. |

`ExistsIncludingArchivedAsync` exists (`IVariableRepository.cs:38`, `VariableRepository.cs:35-44`),
returns `bool`, and is used only by `VariableSnapshotBuilder` (`:115`). This spec does not need it.

### 1.1 Site 2 is unreachable in every configuration — and not only because of the lookup

The issue's argument (the lookup excludes Archived) is sufficient for sibling rows committed as
Archived. There is one case it does not cover, and it matters for what the tests must pin.

`VariableRepository.SaveAsync` dispatches domain events **before** `commit.CommitAsync`
(`VariableRepository.cs:70-77`). While `VariableArchivedDomainEventHandler` runs, the just-archived
variable is **Archived in memory and still Defined in the database**. A `GetByNameAsync` for *that*
name passes the SQL filter on the database row, and then:

- **tracked query, shared `DbContext`** — EF identity resolution returns the tracked instance:
  `State` Archived, but `Variable.Archive` also sets `Value = VariableValue.Unset.Instance`
  (`Variable.cs:141`), so the **unset check at `:93`** skips it. `:88` is redundant.
- **untracked or separate scope** — the database row comes back `State` Defined, **with its old
  value**. `:88` does not fire (state is Defined); the unset check does not fire. Only the name-skip
  at `:70` (`string.Equals(name, variableName.Value, …)` → `continue`) keeps FR-014's literal.

So `:88` protects nothing under either answer, and the real guard for the archived variable's own
placeholder is `:70`. (Which answer holds in production — whether the dispatcher shares the handler's
`DbContext` — is assumed, not verified; the conclusion does not depend on it.)

**No existing test pins `:70`.** `Re_resolves_each_affected_overlay_with_the_archived_variable_reverted_to_literal`
never adds the archived variable to the repository, so removing `:70` leaves it green. §5's C3 adds
the test, modelling the untracked case — the one where `:70` is load-bearing.

Site 3 has the same shape (the changed variable is skipped by name at `:109`); the instance there is
Defined with its new value, and `:138` cannot fire for any sibling the lookup returns.

**Why an "archived sibling stays literal" test is not in the set.** It is covered three times over —
the lookup filter, `Archive()` clearing the value to Unset, and the dead state check — so no single
removal can turn it red, and a test that cannot fail is not evidence. The archived-then-redefined case (C1, C2) **can** fail, and exercises the one
thing the filter decides that nothing else does: which of two same-named rows is rendered.

---

## 2. Scope decision — two branches, not three

**Site 1 (`SetVariableValueCommandHandler:43`) is excluded, deliberately.** Issue **#2200** — open,
**not** `agent:ready`, first line *"Needs a human decision. Not for the autonomous lane"* — already
owns this exact branch and its `SetVariableValueError.VariableArchived` (409 `VARIABLE_ARCHIVED`).
It records the evidence (`PUT /value` on an archived variable answers **404 `VARIABLE_NOT_FOUND`**,
*"does not exist"*, while `GET /system-variables/{name}` answers **200, `state=Archived`**) and asks a
human to choose between:

- **404 is right** → delete the branch and the error type (what #2359 would do); or
- **409 is right** → route through an archived-inclusive lookup (`ExistsIncludingArchivedAsync`
  would suffice) and let the branch earn its place — a **behaviour change** to the HTTP contract and to
  the Automation bridge's log line (`SystemVariableValueRequestedV1Handler.cs:111-113` logs
  `VariableNotInFab` — *"not defined in fab"* — for an archived variable today).

Deleting site 1 here would settle #2200 in the 404 direction from inside the autonomous lane, which
ADR-0144 forbids (the lane implements decisions, it does not make them) and which #2200 says in terms
not to do silently. #2359 does not mention #2200 and was not written as that decision. Site 1 is left
byte-for-byte untouched, with a cross-reference comment on both issues (T008).

**Sites 2 and 3 need no decision.** They are on the push side, where spec 005 **FR-011** already
makes missing, archived and unset render identically (literal `{{name}}`). There is no 404/409
question and no caller who could observe a difference. Deleting them is pure shape.

**Neither push-side branch is preserved.** Neither expresses an intent the not-found path does not
already carry: "archived sibling → keep the literal" is exactly what `!other.HasValue → continue`
produces, and FR-005 says an archived name is released, so the live row (if any) is the right one to
render.

---

## 3. User story

### US1 — A reader of the push handlers is told the truth (P1)

A maintainer reading `VariableValueChangedDomainEventHandler.BuildSnapshotAsync` or
`VariableArchivedDomainEventHandler.Handle` sees exactly the rules that decide a sibling placeholder:
unparseable → skip, not found (which includes archived, by the repository's contract) → skip,
unset → skip, else render. No branch claims to decide something that the lookup already decided.

**Independent test**: the SystemVariables Application test project passes with the characterisation
tests (§5) green before the deletion and **unmodified** and green after it.

**Acceptance scenarios** (all describe behaviour that holds *today* and must still hold after):

```gherkin
Scenario: a sibling archived and re-defined resolves to the live one on a value push
  Given "shift" was defined in munich with value "A" and archived
  And "shift" was defined again in munich with value "B"
  And an overlay's label is "{{shift}} / {{oee}}"
  When "oee" in munich changes to 82.5
  Then the pushed resolved text is "B / 82.5"

Scenario: a sibling archived and re-defined resolves to the live one on an archive push
  Given "shift" was defined in munich with value "A", archived, and defined again with value "B"
  And an overlay's label is "{{shift}} / {{oee}}"
  When "oee" in munich is archived
  Then the pushed resolved text is "B / {{oee}}"

Scenario: the variable being archived stays literal even while its row is still readable
  Given "oee" in munich is Defined with value 82.5 and the repository still returns it
  And an overlay's label is "OEE: {{oee}}%"
  When the archived domain event for "oee" in munich is handled
  Then the pushed resolved text is "OEE: {{oee}}%"
```

The last scenario models §1.1's untracked case: at dispatch time the database row is still Defined
with its value. It pins the `:70` name-skip — the guard that actually holds FR-014, which `:88` never
did.

**Conflict / bad-request / auth scenarios**: **N/A.** No endpoint, command, contract or authorization
path is touched. The only HTTP-visible branch in the issue (site 1) is out of scope (§2).

---

## 4. Functional requirements

- **FR-001** `VariableArchivedDomainEventHandler` no longer tests `variable.State == VariableState.Archived`
  on a sibling returned by `GetByNameAsync`.
- **FR-002** `VariableValueChangedDomainEventHandler.BuildSnapshotAsync` no longer tests
  `variable.State == VariableState.Archived` on a sibling returned by `GetByNameAsync`.
- **FR-003** At each of those two `!other.HasValue` sites, one comment line says *why* the not-found
  path also covers archived rows (`GetByNameAsync` excludes them, FR-005) — the non-obvious fact whose
  absence caused this issue. Mirrors the existing wording at `VariableSnapshotBuilder.cs:43-46`.
- **FR-004** The name-skip at `VariableArchivedDomainEventHandler.cs:70` is unchanged and, after this
  spec, covered by a test (§1.1).
- **FR-005** `SetVariableValueCommandHandler`, `SetVariableValueErrors.cs`, `IVariableRepository`,
  `VariableRepository`, `InMemoryVariableRepository` and `GetByNameAsync`'s contract are **unchanged**.
- **FR-006** Every resolved-text push is byte-identical before and after for every input.

## 5. Characterisation set (phase 4a)

Written first, run against unmodified production code, **observed green**, output quoted verbatim.
Then the production edit; then the same tests, **unmodified**, observed green again.

| ID | File | Test (sentence-style, ADR-0053) |
|---|---|---|
| C1 | `VariableValueChangedDomainEventHandlerTests.cs` | `A_sibling_archived_and_defined_again_resolves_to_the_live_one` |
| C2 | `VariableArchivedDomainEventHandlerTests.cs` | `A_sibling_archived_and_defined_again_resolves_to_the_live_one` |
| C3 | `VariableArchivedDomainEventHandlerTests.cs` | `The_variable_being_archived_stays_literal_even_while_its_row_is_still_readable` |

**Counterfactual.** None of these can fail from deleting the
dead branches — that is what makes them characterisation. Each must be shown able to fail at all:

- **C3** — remove the `:70` name-skip temporarily → C3 red (renders `OEE: 82.5%`). Restore.
- **C1, C2** — remove the Archived filter from `InMemoryVariableRepository.GetByNameAsync`
  temporarily → both red (`SingleOrDefault` over two same-named rows throws). Restore.

Restore by `git checkout -- <file>` and confirm `git diff` is empty before continuing. A restored file
keeps its old timestamp, so MSBuild may skip it — rebuild with `--no-incremental`. Quote both red
outputs in the PR body.

## 6. Colour

**Characterisation, observed green** — whole spec. No piece is behaviour-changing (site 1, the only
candidate, is excluded). An assertion that must be edited after the deletion is evidence behaviour
moved: **block, do not adjust** (CLAUDE.md, ADR-0144).

## 7. Latency (§IV)

Leg: **Event → overlay state (≤ 200 ms)**. Effect: removes one in-memory comparison per sibling
placeholder per affected overlay. No I/O added or removed (the `GetByNameAsync` call stays). Not
measurable and not claimed as an improvement. No dashboard obligation arises.

## 8. Out of scope / follow-ups (for the orchestrator)

- **#2200** — site 1 and `SetVariableValueError.VariableArchived`; human decision, 404 vs 409. Post a
  comment linking this spec so the two issues cross-reference (T008).
- **#2446** (open, `agent:ready`) — `GetVariableQueryHandler` does not exclude Archived, so
  archive-then-redefine in one fab answers `VariableFabAmbiguous`. Found independently during this
  investigation; already filed, **do not re-file**.
- `VariableSnapshotBuilder.cs:12-16` says the push-side handlers "each carry their own copy" of the
  skip policy. Still true after this spec (one rule fewer in each copy); not edited.

## 9. ADR

None needed. The spec deletes two unreachable branches under an existing contract (spec 005 FR-005,
FR-011; `IVariableRepository` doc) and declines to make the one real decision in the neighbourhood
(#2200).
