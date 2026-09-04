# Tasks — Spec 068, a brief that cites checkable things

**Phase:** 3 (Tasks) · **Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2058

**Engineer:** `backend-engineer` — a C# guard in `tests/Architecture.Tests`. No
frontend, no infra: the CI workflow is **read** by the guard, never edited.

**Phase 4a colour:** **red.** Behaviour-changing (constitution §Testing).

**Parallelism:** almost none, and that is honest rather than a missed
opportunity. ADR-0109 marks `[P]` for tasks owning **disjoint files**; T002–T007
all edit the same single file, `AgentBriefClaimTests.cs`. Only the four brief
corrections (T008–T010a) touch disjoint files and are genuinely parallel — and
they are one line each, so the marker is bookkeeping, not a fan-out
opportunity. **The
orchestrator should not expect to fan this out.**

## Foundational — blocks everything

- **[T001] [US-1]** Create `tests/Architecture.Tests/AgentBriefClaimTests.cs`
  with the corpus enumeration and the shared readers: `RepositoryRoot()`,
  relative-path normalisation to `/`, the Markdown **block splitter** (bullet item
  with continuations, or paragraph), and the inline-code-span matcher. Add the
  class-level XML doc stating the guard's purpose **and its declared limits**,
  following `PaginatedConsumerTests`.
  *Depends on: nothing. Blocks: T002–T007.*

  **Done when:** the project compiles and a scratch assertion can list the 13
  brief files by repository-relative path.

## Registers — derived, never declared

- **[T002] [US-1]** Register A, decisions that exist: parse the `NNNN` prefixes of
  `docs/adr/*.md` **and** the `| NNN |` decision rows of
  `0000-initial-decisions.md`, union them.
  *Depends on: T001.*

  **Done when:** the register contains 0007, 0024 and 0026 (rows) as well as 0144
  (a file). **If it does not contain the first three, stop** — a file-only
  register makes three correct citations red and the guard fails on correct work.

- **[T003] [US-1]** Register B, path anchors: enumerate the entry names of the
  repository root at run time. No literal list.
  *Depends on: T001.*

  **Done when:** `src`, `apps`, `docs`, `specs`, `.github`, `.specify`,
  `global.json` are all anchors, and nothing is hard-coded.

## Assertions

- **[T004] [US-1]** **A1 + A2** — every `ADR-NNNN` / `adr/NNNN-slug` citation
  resolves against Register A (per file, `[Theory]`); every ADR-shaped token that
  the strict pattern did not match is reported (FR-007).
  *Depends on: T002.*

  **Done when:** green on today's corpus, and inserting `ADR-141` produces a
  failure naming the token and the accepted spellings.

- **[T005] [US-1]** **A3** — every anchored path span resolves; a span carrying a
  glob metacharacter must match ≥1 entry. Per file, `[Theory]`.
  *Depends on: T003.*

  **Done when:** **red**, naming exactly `src/app/auth.ts`
  (`frontend-engineer.md:11`), `specs/NNN-x/spec.md` (`next-issue.md:67`) and
  `specs/NNN-x/` (`architect.md:11`), and nothing else. If it names a **fourth**
  span, the anchoring rule is over-recognising — fix the recogniser before
  touching any brief.

  > **Corrected during phase 4a.** This done-when said two spans and warned that
  > a third meant over-recognition. There are three, and the third is not
  > over-recognition: `specs/NNN-x/` in `architect.md:11` is anchored at a real
  > top-level entry and resolves to nothing — the same defect class as the other
  > two. `spec.md`'s assumption A2 already recorded "21 anchored spans, 3
  > unresolvable, all three genuine defects"; this line was the one that
  > disagreed with it. A task list that contradicts its own spec is precisely the
  > drift this issue exists to catch.

- **[T006] [US-1]** **A4 + A5 + A6** — CI blocks: job-set equality with `ci.yml`'s
  `jobs:` keys, per-job attribute agreement (`continue-on-error` / blocking,
  `needs`), and a loud failure for a CI block that enumerates jobs but does not
  parse.
  *Depends on: T001.*

  **Done when:** **red**, naming `infra-reviewer.md`'s `integration` job as
  claimed `continue-on-error` against a `ci.yml` that has no such key. It must
  **not** flag `infra-reviewer.md:19`'s hypothetical or `infra-engineer.md:12`'s
  negative claim — if it does, the claim is not bound to a named job and the
  recogniser is wrong.

- **[T007] [US-1]** **A7 + A8 + A9** — corpus coverage asserted **per file**
  (naming any present-but-unscanned brief), the claim-count floor, and the
  self-scan forbidding allow-list vocabulary in the guard's own executable lines.
  *Depends on: T001.*

  **Done when:** A7 fails if a brief is added to a subdirectory the sweep misses,
  proven by creating one temporarily; A9 passes on the guard as written.

## The live findings this guard turns red

Each is a one-line correction. **Do not touch anything else in these files** — the
scope is (1) only, and brief content is otherwise out of scope.

- **[T008] [P] [US-1]** `.claude/agents/infra-reviewer.md:18` — the `integration`
  job is **blocking**; there is no `continue-on-error` in `ci.yml`. Correct it to
  match `infra-engineer.md:12`, which #2055 already fixed.
  *Depends on: T006. Disjoint file.*
- **[T009] [P] [US-1]** `.claude/agents/frontend-engineer.md:11` —
  `src/app/auth.ts` → `apps/*/src/app/auth.ts`.
  *Depends on: T005. Disjoint file.*
- **[T010] [P] [US-1]** `.claude/commands/next-issue.md:67` —
  `specs/NNN-x/spec.md` → `specs/*/spec.md`.
  *Depends on: T005. Disjoint file.*
- **[T010a] [P] [US-1]** `.claude/agents/architect.md:11` — `specs/NNN-x/` →
  `specs/*/`. Added during phase 4a: the guard reports this span, T005 above did
  not list it, and T010's parenthetical filed it under the wrong file. **Four
  briefs are corrected in phase 4b, not three.**
  *Depends on: T005. Disjoint file.*

## Phase 4a evidence

- **[T011] [US-1]** Capture the **red** output of T005 and T006 against the
  uncorrected briefs, verbatim, before T008–T010a. This is real red against real
  defects — no reversion needed for these two arms.
  *Depends on: T005, T006. Must precede T008–T010a.*

- **[T012] [US-1]** Demonstrate red for the **A1** arm, which is green on arrival.
  Temporarily edit `.claude/agents/backend-engineer.md`, changing one existing
  `ADR-0105` citation to `ADR-0199`. Run the project, capture the verbatim
  failure, then `git checkout -- .claude/agents/backend-engineer.md`.

  **The reversion must not be committed.** Confirm `git status` is clean before
  the commit in T013. Committing the demonstration re-ships a defect to prove a
  test works.
  *Depends on: T004.*

## Close

- **[T013] [US-1]** Commit. Conventional Commits, **no `Co-Authored-By`**
  (ADR-0086). Each commit builds on its own (rebase-merge lands them individually
  on `develop`).
  *Depends on: T008, T009, T010, T010a, T011, T012.*

- **[T014] [US-1]** Run the spec's independent end-to-end procedure, all six
  steps, and record the result. `git status` clean at the end.
  *Depends on: T013.*

- **[T015] [US-1]** PR body carries the verbatim red output from T011 and T012,
  the green run, and states plainly what the guard **cannot** catch — the semantic
  half, filed as #2081.
  *Depends on: T014.*

## Gate

**Phase 3 gate (ADR-0037, as corrected in CLAUDE.md):** the *feature-level* issue
— #2058 — is on Project #13. It already is (status **In Progress**). **Do not run
`/speckit-taskstoissues`**; per-task issues stopped after spec 028, and adding 15
items would bury the in-flight work the board exists to show.

## Not in this slice

- The semantic claim classes ("NRT is disabled", "the publisher has never been
  run") — **#2081**, which needs an ADR that ADR-0144 bars this lane from writing.
- Any shared `RepositoryRoot()` helper across the five guards that now duplicate
  it — a refactor of four existing files, and a separate issue.
- CLAUDE.md's own drift. Out of scope here.
