# Tasks — Spec 128

Feature-level issue #2137 is the tracking artefact (CLAUDE.md, Phase 3).
No per-task issues since spec 028.

| # | Task | File | Depends |
|---|---|---|---|
| T001 | Red: run-mode `WaitAnnotation` guard for the nine services | `tests/Integration.Tests/AppHostMigrationGateTests.cs` | — |
| T002 | Red: script guard — a stack with no applied migrations must not satisfy the wait | `scripts/wait-for-e2e-stack.test.mjs` | — |
| T003 | Record that gap 3 has no honest red, and the observed run that stands in for one | `specs/128-.../{spec,plan}.md` | — |
| T004 | Gap 3: `if: failure()` → `if: failure() \|\| cancelled()` | `.github/workflows/ci.yml` | T003 |
| T005 | Gap 2: migrations probe, placed first, failing loudly and on inability to ask | `scripts/wait-for-e2e-stack.sh` | T002 |
| T006 | Gap 1: lift the `WaitForCompletion(migrations)` loop out of `if (isE2ETests)` | `src/AppHost/AppHost.cs` | T001 |
| T007 | Verify: `dotnet build -c Release`, `Category=FixtureLogic`, `pnpm test:guards`, and a run-mode boot with a deliberately failing `migrations` | `specs/128-.../verification.md` | T004–T006 |

**[P]** T001 and T002 are independent (different languages, different files).
T004–T006 touch three disjoint files and could be done in any order; they are
sequenced above only so each commit is self-contained.
