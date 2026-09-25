# Tasks 249 — The form three readers assume away

`spec.md` / `plan.md`. Issue
[#2467](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2467).
Branch `fix/2467-masker-assumptions-guard`, worktree `D:/Github/sse-2526`, cut
from `origin/develop`.

**Phase-4a colour: RED** (spec §6), with one companion characterisation declared
**green** (T001).
**Phase roles:** 4a `test-writer`, 4b `backend-engineer`, 5 `backend-engineer`,
6 `backend-reviewer`.

---

## 1. US1 (P1) — three more readers stay inside what they can mask

### Phase 4a — `test-writer` (tests only; may not be edited afterwards to pass)

| ID | [P] | Story | Task | File | Depends on |
|---|---|---|---|---|---|
| **T001** | [P] | US1 | Hazard characterisation (plan §3): `CommentsAndLiteralInteriors` mask + `StatementEnd(…, NotFound, AlreadyMasked)` over a two-mapping fixture whose first summary is `"""see "foo( bar" now"""`; assert `-1`. Escaped concatenation only. **Run alone and capture GREEN** before T002-T004 exist; if red, stop and hand back — the premise is wrong. | `SourceScanCharacterisationTests.cs` | — |
| **T002** | [P] | US1 | Add `A_planted_unmaskable_form_is_reported_with_its_file_and_line` (plan §2.2) and `The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask` (plan §2.3, both non-vacuity assertions first, message per plan §2.4 naming the borrowed **428 / 400**). Both call the not-yet-existing `FormsThisReaderCannotMask` / `BannedForms`. | `PreconditionDeclarationTests.cs` | — |
| **T003** | [P] | US1 | Same two tests; message names the borrowed **400 refusal**. | `RouteValueRefusalDeclarationTests.cs` | — |
| **T004** | [P] | US1 | Same two tests; message names the borrowed **401 challenge and authorized/anonymous classification**. | `StatusProducerDeclarationTests.cs` | — |
| **T005** | — | US1 | `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~PreconditionDeclarationTests\|FullyQualifiedName~RouteValueRefusalDeclarationTests\|FullyQualifiedName~StatusProducerDeclarationTests"`. **Capture the verbatim compile failure** naming the missing members in all three files; return it verbatim as the engineer's brief, together with T001's green output. | — | T001-T004 |

### Phase 4b — `backend-engineer` (may not edit T001-T004)

| ID | [P] | Story | Task | File | Depends on |
|---|---|---|---|---|---|
| **T006** | [P] | US1 | Implement `BannedForms()` (= `SourceMask.UnhandledForms(MaskStrictness.CommentsAndLiteralInteriors)`) and `FormsThisReaderCannotMask(IEnumerable<(string File, string Text)>)` per plan §2.1 — raw text, first occurrence, `RouteChainReader.LineOf`. Doc-comment both in the file's style. Plan §5.1 word list: none in code. | `PreconditionDeclarationTests.cs` | T005 |
| **T007** | [P] | US1 | Same. | `RouteValueRefusalDeclarationTests.cs` | T005 |
| **T008** | [P] | US1 | Same (may reuse the existing `ReadRepositoryFile`, `:767`). | `StatusProducerDeclarationTests.cs` | T005 |

### Phase 5 — verification (`backend-engineer`)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T009** | — | US1 | Spec §5 end to end: filter green; plant a three-quote `.WithSummary` in `src/CameraCatalog/Api/CameraEndpoints.cs`; **observe all three new corpus facts (and spec 192's) fail naming that file and line** — the behavioural red; record verbatim any *other* assertion that goes red; `git checkout --` the file; re-run green. Then run the **whole** `Architecture.Tests` project (plan §5.2). Write `specs/249-the-form-three-readers-assume-away/verification.md` quoting every output verbatim. | T006-T008 |

### Phase 6 — `backend-reviewer`

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T010** | — | US1 | Review: raw text read, not masked? Strictness at every call site `CommentsAndLiteralInteriors`? Each message explains *this* one-pass reader and names its guard's borrowed declaration, not copied from 192 or 075? Both non-vacuity assertions present and first? Can each detection test fail if its helper is wrong (not checking its own input)? T001 unmodified since captured green? No self-scan word introduced? Nothing under `src/` changed? | T009 |

### Phase 7

| ID | Task | Depends on |
|---|---|---|
| **T011** | Re-check spec number 249 against `origin/develop`, every remote branch and sibling worktrees (248 was held uncommitted on 2026-09-25). `gh pr create --base develop`, quoting T005's compile failure, T001's green, and T009's planted-corpus failures; body closes #2467 with a closing keyword. ADR-0030 commits, rebase-only (ADR-0087). | T010 |

### Follow-up, not part of this slice

| ID | Task |
|---|---|
| **F001** | File an issue: the offender loop now exists in five guards (075, 192, and these three). Consider hoisting it — a behaviour-preserving refactor with its own characterisation obligation (spec §7). |

---

## 2. Independent test criterion ("done")

With a raw string planted in `src/CameraCatalog/Api/CameraEndpoints.cs`, each of
the three guards fails naming that file and line and the form; with it removed,
all pass; T001 permanently shows what the form does to a
`CommentsAndLiteralInteriors` chain boundary. Not "it compiles", not "the suite is
green".

---

## 3. Foundational / blocking

**None.** No `Shared.Kernel`, `Shared.Contracts`, `AppHost`, Aspire resource or
migration. Nothing is blocked on this; this is blocked on nothing.

---

## 4. Parallelism (ADR-0109)

T001-T004 own four disjoint files → `[P]`. T006-T008 own three disjoint files →
`[P]`. T005 and T009 are the serial joins (one `dotnet test` over the shared
project). Within one file, 4a precedes 4b by the red rule, not by file
contention. The slice as a whole touches no shared plumbing (`SourceMask.cs`,
`RouteChainReader.cs` read only) and no file any open PR touches (checked
2026-09-25), so it is parallel with all in-flight work.
