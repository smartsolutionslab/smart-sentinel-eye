# Tasks 192 — A form the masker cannot read

`spec.md` / `plan.md`. Issue [#2278](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2278).
Branch `2278-masker-assumptions-guard`, worktree `D:/Github/sse-2278`, cut from
`origin/develop`.

**Phase-4a colour: RED** (spec §6), with one companion test declared green.
**Phase roles:** 4a `test-writer`, 4b `backend-engineer`, 6 `backend-reviewer`.

---

## 1. US1 (P1) — the corpus stays inside what this reader can mask

Every task below edits exactly one file,
`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`.

### Phase 4a — `test-writer` (tests only; may not be edited afterwards to pass)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T001** | — | US1 | Add `A_raw_string_in_a_chain_runs_one_mapping_into_the_next` — the hazard characterisation (plan §2.2). Inline `const string` fixture built by **escaped concatenation**, not a raw string (plan §2.2's delimiter trap). Asserts: `StatementEnd` returns `masked.Length` (the `EndOfText` sentinel, asserted by name — never by the literal 262); the span contains `RequireAuthorization`; the span contains `Status403Forbidden`. **Run it on its own and capture it GREEN before T002 exists** — it characterises current `SourceMask` + `RouteChainReader` behaviour and must pass unmodified. | — |
| **T002** | — | US1 | Add `A_planted_unmaskable_form_is_reported_with_its_file_and_line` — the detection counterfactual. Calls the not-yet-existing `FormsThisReaderCannotMask` (plan §2.3) with two in-memory `(File, Text)` pairs: one containing the three-quote delimiter, one clean. Asserts exactly one offender, that it names the file, the line, the form and the reason; and that the clean pair yields nothing. | T001 |
| **T003** | — | US1 | Add `The_api_sources_use_only_the_string_and_comment_forms_this_reader_can_mask` — the real-corpus `[Fact]` (plan §2.1), including the two non-vacuity assertions (non-empty file list, non-empty ban list) **before** the emptiness assertion, and the failure message per plan §4 — written for this two-stage reader, **not copied from `ConcurrencyConflictDeclarationTests.cs:776-783`**, whose explanation is false of this reader. | T002 |
| **T004** | — | US1 | Run `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~EndpointScopeDeclarationTests"`. **Capture the verbatim failure** — the project does not compile, naming the missing `FormsThisReaderCannotMask`. Return that output verbatim as the engineer's brief. This is the weakest form of red and the spec says so: the **behavioural** red is taken at T008 and both are quoted in the PR body. | T003 |

### Phase 4b — `backend-engineer` (may not edit T001-T003)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T005** | — | US1 | Implement `private static string[] FormsThisReaderCannotMask(IEnumerable<(string File, string Text)> sources)` per plan §2.3. Reads **raw** text, never `Masked(...)`. One offender line per (file, form), first occurrence only via `IndexOf`, `RouteChainReader.LineOf` for the line. | T004 |
| **T006** | — | US1 | Read the ban list for **both** stages the reader applies — `CommentsBlankedLiteralsIntact` and `LiteralInteriorsOnly` — distinct by `Form` (plan §3). Not `CommentsOnlyLiteralsIntact`, not `CommentsAndLiteralInteriors`: this guard applies neither. | T005 |
| **T007** | — | US1 | Doc-comment the two new `[Fact]`s in this file's house style: what is asserted, why the form is unsafe *here specifically* (the bracket-depth boundary since #2183), and — on T001 — that if it goes red because the masker learned raw strings, the correct response is to delete the ban, not edit the assertion. Check plan §5.1: no identifier or comment may contain `allowlist`, `whitelist`, `skiplist`, `baseline`, `exempt`, `waiver`, `waived`, `knownViolation`, `suppress` or `#pragma warning disable`, or the guard's own self-scan fails pointing at the wrong rule. | T005 |

### Phase 5 — verification (`backend-engineer`)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T008** | — | US1 | Spec §5's procedure, end to end: run green; plant a three-quote `.WithSummary` in `src/CameraCatalog/Api/CameraEndpoints.cs`; **observe the new guard fail naming that file and line** — the behavioural red; `git checkout --` the file; re-run green. Write `verification.md` quoting all three outputs verbatim. Not "tests are green": the observation is that the failure **names its own cause and location**, which A15 does not. | T006, T007 |
| **T009** | — | US1 | Run the whole `Architecture.Tests` project (not the filter) — plan §5.2: the new fixture puts a three-quote sequence into a file other guards also read, and "checked and reasoned" is not "ran". | T008 |

### Phase 6 — `backend-reviewer`

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T010** | — | US1 | Review. Specifically: is the helper reading **raw** text? Does the message explain *this* reader rather than the sibling's? Are both non-vacuity assertions present? Does any assertion check its own input (T002 must be able to fail if `FormsThisReaderCannotMask` is wrong)? Is T001 genuinely a characterisation and unmodified since it was captured green? Nothing under `src/` changed? | T009 |

### Phase 7

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T011** | — | US1 | **Re-check the spec number** against `origin/develop` and every remote branch before opening the PR — PR #2465 holds 191 and may have merged since (spec §Spec number). Then `gh pr create --base develop`, quoting T004's compile failure and T008's planted-corpus failure. Commit messages per ADR-0030; rebase-only per ADR-0087. | T010 |

### Follow-up, not part of this slice

| ID | Task |
|---|---|
| **F001** | File an issue: the identical missing assertion in `PreconditionDeclarationTests`, `RouteValueRefusalDeclarationTests` and `StatusProducerDeclarationTests` — same `CommentsAndLiteralInteriors` + `AlreadyMasked` exposure (spec §7). |
| **F002** | File or fold in: `EndpointScopeDeclarationTests.cs:1246` still names `MaskLiterals`, deleted by #2257 (spec §7). Comment-only, own phase-4a obligation. |

---

## 2. Independent test criterion ("done")

The guard fails, naming `src/CameraCatalog/Api/CameraEndpoints.cs:<line>` and the
form, when a raw string is planted there; passes when it is removed; and the
counterfactual at T001 shows, permanently, what that form would have done to a
statement boundary. Not "it compiles", not "the suite is green".

---

## 3. Foundational / blocking

**None.** No `Shared.Kernel`, no `Shared.Contracts`, no `AppHost`, no Aspire
resource, no migration. Nothing else in the repository is blocked on this and
this is blocked on nothing.

---

## 4. Parallelism (ADR-0109)

**No `[P]` markers inside this slice.** Every task edits the same file,
`tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`, so they are
strictly serial by the disjoint-file rule.

**The slice as a whole is fully parallel with any other in-flight work.** It owns
that one file exclusively; it touches no shared plumbing (`SourceMask.cs`,
`RouteChainReader.cs`, `RepositorySource.cs` are read, never edited) and nothing
under `src/`. Confirmed 2026-09-20: the only open PR is #2465, whose files are
`specs/191-…/*` and `tests/Integration.Tests/Identity/*`.
