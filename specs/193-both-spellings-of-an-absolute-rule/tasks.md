# Tasks 193 — Both spellings of an absolute rule

`spec.md` / `plan.md`. Issue [#2292](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2292).
Branch `2292-outbox-sync-commit-guard`, worktree `D:/Github/sse-2292`, cut from
`origin/develop`.

**Phase-4a colour: RED** (`spec.md` §6), with one companion assertion declared
green from the start: the existing nine-assembly theory
`No_repository_commits_without_its_announcements`, which must pass unmodified
throughout.

**Phase roles:** 4a `test-writer`, 4b `backend-engineer`, 6 `backend-reviewer`.
Justified in §5.

---

## 1. US1 (P1) — the rule sees both spellings of the commit it forbids

Two files, and the phase split owns one boundary between them: `test-writer`
creates `OutboxCommitProbe.cs` and the companion `[Fact]`; `backend-engineer`
changes the comparison. Neither may edit the other's work to reach green
(ADR-0144).

### Phase 4a — `test-writer` (tests only; may not be edited afterwards to pass)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T001** | — | US1 | Create `tests/Architecture.Tests/OutboxCommitProbe.cs` with the four types of `plan.md` §3.1 in namespace `SmartSentinelEye.Architecture.Tests.Persistence`: `AsyncOffenderRepository`, `SyncOffenderRepository`, `SeamCommitRepository`, `FailureSubscriptionRepository`, plus `ProbeDbContext : DbContext`. Primary constructors; `CancellationToken` last on the async one (ADR-0049). Names taken **verbatim from the issue** for the two offenders, so the issue's transcription still matches what the tree contains. | — |
| **T002** | — | US1 | Doc-comment `OutboxCommitProbe.cs` in the `PrimitiveBoundaryProbe.cs:6-18` house style: these types deliberately violate the rule and must never be "fixed"; the expected verdicts in T003 are **exact**, so adding or retyping a member here requires updating that assertion in the same change; `ProbeDbContext` is never instantiated, which is why it has no `OnConfiguring`. Say on `FailureSubscriptionRepository` what it is for — it is the only control whose purpose is not self-evident (`spec.md` §3.2). | T001 |
| **T003** | — | US1 | Add `The_rule_sees_both_spellings_of_a_direct_commit` to `OutboxCommitTests` (`plan.md` §3.2). Calls the not-yet-existing `Offenders(typeof(OutboxCommitTests).Assembly)`. Asserts the **exact** two-element list with `ShouldBe(…, ignoreOrder: true)` — never `ShouldContain`, which would pass a detector that reports everything. | T002 |
| **T004** | — | US1 | Run `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~OutboxCommitTests"`. **Capture the verbatim failure** and return it as the engineer's brief. Expect a compile failure naming the missing `Offenders`; if the engineer prefers, T003 may instead be written against the existing `CallsSaveChangesDirectly` to obtain a *behavioural* red first — either is acceptable evidence here, because the corpus red at T009 is the one that proves the shipped rule moved. | T003 |

### Phase 4b — `backend-engineer` (may not edit T001-T003)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T005** | — | US1 | Extract `private static List<string> Offenders(Assembly assembly)` holding steps 1-4 of `plan.md` §2.1, and repoint the theory at it. **Behaviour-preserving**: same four steps, same order, same input. Run the theory before and after and confirm it is green both times — a red here is a regression, not a step (constitution §Testing). | T004 |
| **T006** | — | US1 | Widen the comparison at `OutboxCommitTests.cs:124` to `called?.Name is nameof(DbContext.SaveChanges) or nameof(DbContext.SaveChangesAsync)` (`plan.md` §2.2). Change **nothing else** in `ReferencesSaveChanges` — not the opcode set, not the `catch` filter, not the `i + 4 < il.Length` bound. No `!` on `called.DeclaringType`: NRT still narrows (`spec.md` §3.1). | T005 |
| **T007** | — | US1 | Update the failure message (`:49`), the class doc (`:7`) and the `ReferencesSaveChanges` doc (`:70`) to name both spellings (`plan.md` §3.4). **Keep the existing `<para>` about nested types verbatim** — it records why this rule was wrong the first time. Add one sentence saying why the comparison is exact membership rather than a prefix, naming `add_SaveChangesFailed` and `SaveChangesAndFlushMessagesAsync`. | T006 |
| **T008** | — | US1 | Confirm `Assemblies()` still lists exactly the nine `…Infrastructure` names and the test assembly was **not** added (`plan.md` §5.2). This is the one change that must not have happened; check it rather than assume it. | T006 |

### Phase 5 — verification (`backend-engineer`)

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T009** | — | US1 | `spec.md` §5 steps 1-4 on the **real corpus**: plant `dbContext.SaveChanges();` in a repository under `src/CameraCatalog/Infrastructure/**/Persistence/`; observe the guard **pass** on the pre-change code and **fail** naming that repository after T006; `git checkout --` and observe green again. If the revert stays red, touch the file or rebuild `--no-incremental` — a restored file keeps its old timestamp and MSBuild skips it. Quote all four runs verbatim. | T007, T008 |
| **T010** | — | US1 | Run the **whole** `Architecture.Tests` project, not the `OutboxCommitTests` filter (`plan.md` §5.1). The probe adds types to an assembly other guards walk, and "reasoned that it is fine" is not "ran it". Then build the solution in **Release** (`TreatWarningsAsErrors`) — `plan.md` §7's analyzer risk on `ProbeDbContext` / `FailureSubscriptionRepository` is only visible there. **Stop the Aspire stack first** if one is running, or MSB3027 will read as a broken build. | T009 |
| **T011** | — | US1 | Write `specs/193-both-spellings-of-an-absolute-rule/verification.md` quoting, verbatim: T004's red, T009's four corpus runs, T010's whole-project and Release results. A measurement reported only in a message is invisible to every later grep. | T010 |

### Phase 6 — `backend-reviewer`

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T012** | — | US1 | Review. Specifically: is `Assemblies()` unchanged (T008)? Is the comparison exact membership rather than `StartsWith`/`Contains`, and does a comment say why? Is T003's assertion **exact**, and can it actually fail — could `SeamCommitRepository` or `FailureSubscriptionRepository` be deleted without the test noticing? Did the §3.3 extraction change behaviour, or only shape? Is the existing theory byte-identical in what it executes? Did anything under `src/` change (it must not, outside T009's reverted plant)? Are the probe's doc comments strong enough that the next warning-clearing pass leaves it alone? | T011 |

### Phase 7

| ID | [P] | Story | Task | Depends on |
|---|---|---|---|---|
| **T013** | — | US1 | **Re-check the spec number** against `origin/develop` and every remote branch before opening the PR — PR #2468 holds 192 and may have merged since (`spec.md` §Spec number); two unmerged branches can both claim the next number. Then `gh pr create --base develop`, quoting T004's red and T009's corpus red in the body. Commit messages per ADR-0030, no `Co-Authored-By` (ADR-0086); rebase-only per ADR-0087, and each commit must build on its own. | T012 |

### Follow-ups, filed not folded in (`spec.md` §7)

| ID | Task |
|---|---|
| **F001** | File an issue: the candidate filter misses `src/StreamDistribution/Infrastructure/Attribution/StreamFabAttributionService.cs:81`, which calls `SaveChangesAsync` directly from a non-`Repository` type outside a `.Persistence` namespace. Decide whether that is an offence; it may be a real bug rather than a guard gap. |
| **F002** | File an issue: other escapes the IL scan does not model — `Database.ExecuteSql*`, a `DbContext` reached through an interface-typed field, a `SaveChanges` inside a lambda lifted to a closure class `BodiesOf` does not walk. |
| **F003** | Fold in or file: `src/Shared.CQRS/ITransactionalCommit.cs:8`'s doc comment names only `SaveChangesAsync`. Comment-only, in a production assembly, own phase-4a obligation — file it rather than drive-by. |

---

## 2. Independent test criterion ("done")

A synchronous `dbContext.SaveChanges()` planted in a real
`CameraCatalog.Infrastructure` repository makes
`No_repository_commits_without_its_announcements` **fail**, naming that
repository, and the failure message names both spellings. Removing the plant
returns it to green. The probe holds the issue's counterfactual permanently, so
the next person to widen this rule inherits the evidence instead of
reconstructing it.

Not "it compiles". Not "the filter is green". Not "the probe test passes" —
that proves the detector moved, not the guard.

---

## 3. Foundational / blocking

**None.** No `Shared.Kernel`, no `Shared.Contracts`, no `AppHost`, no Aspire
resource, no migration, no `.csproj` edit. Nothing in the repository is blocked
on this, and this is blocked on nothing.

---

## 4. Parallelism (ADR-0109)

**No `[P]` markers, and the reason is the file set, not the size.** T001-T002
own `OutboxCommitProbe.cs`; T003 and T005-T008 own `OutboxCommitTests.cs`; T003
depends on a symbol T005 creates. Two files, one of them shared by both phases
in strict sequence — there is no disjoint pair to fan out.

`tasks.md` T009's plant touches one `src/CameraCatalog/Infrastructure` file
transiently and reverts it; that is a verification step, not a parallel task,
and it must not overlap with any other agent's work on that context.

**File contention: none** (`spec.md` §File contention). Open PR #2468 owns
`specs/192-…/*` and `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`.
Re-check at T013 — a PR merged in between changes nothing here, but the spec
number is not the only thing worth re-reading before opening a PR.

---

## 5. Why these roles

**4a `test-writer`, not `test-adversary`.** The probe is a deliberate-violation
corpus, which has an adversarial flavour, but what it asserts is the rule's own
stated intent — the class doc already claims to be absolute, and this makes the
instrument match. The adversarial work, finding the *other* ways the seam is
escaped, is `spec.md` §7's F001/F002 and is deliberately not in this slice.
Precedent: specs 184 and 192 used `test-writer` for the same shape.

**4b `backend-engineer`.** The change is three lines of C# and a doc comment,
small enough to tempt a one-shot — and that is exactly the case ADR-0144's split
exists for. The engineer receives T004's verbatim output as its brief and may
not edit T001-T003 to pass. A guard fix authored by the same agent that wrote
its test is a guard nobody has checked.

**6 `backend-reviewer`.** Backend C#, reflection, EF Core semantics, and one
question that needs the repository's history in hand: whether the extraction at
T005 preserved behaviour. No frontend file, no infrastructure file, no HTTP
surface, no scope — so no `frontend-reviewer`, no `infra-reviewer`, and no
`security-reviewer`.
