# Tasks 361: One client wire row; citations that name their member

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2939 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green.** The covering tests exist (spec §3) and were
observed green on `6f26a43c`. They must pass **unmodified** after the change; an assertion needing
an edit means behaviour moved — block, do not adjust.

**Engineer:** backend-engineer (T003-T004). **Test-writer:** T002 (re-observe green; write
nothing new).

**Commits.** Conventional Commits, no `Co-Authored-By` (ADR-0086), never `--no-verify`. Each commit
builds and passes on its own. Change A and Change B are separate commits (one is a type removal,
the other is text).

**Forbidden:** any file under `tests/Identity.Infrastructure.Tests`, any assertion (only the
`customMessage` literal in `KioskPrivilegeSweepRegistrationTests` may change), any ADR, the
constitution, analyzer or threshold changes, files outside those listed in plan §2-§3.

Single slice; all tasks `[US1]`. No `[P]`: T003 and T004 are tiny and sequential on one branch.

- [ ] **T001** [US1] Re-check spec number 361 against `origin/develop`, every remote head and
  local branches (360 is on `fix/2938-event-cursor`).
- [ ] **T002** [US1] Run the spec §3 filters on the untouched branch; return verbatim output
  (expect 53 + 3 green).
- [ ] **T003** [US1] Change A (plan §2): delete `ClientDetailRow`, retarget the two
  `ReadFromJsonAsync` calls in `HttpKeycloakAdminClient.Sweeps.cs` to `ClientRow[]`. Re-run T002's
  filters (unmodified, green) and `dotnet build -c Release`. Commit
  `refactor(identity): read every client listing into the one ClientRow`.
- [ ] **T004** [US1] Change B (plan §3): four text edits. Run the plan §3 hash proof (three
  comment-only files, pairs match) plus its counterfactual; re-run the Architecture filter; run the
  two greps (no matches). Commit
  `docs(identity): name TryDeleteClientAsync instead of a line, and stop claiming it delegates to the sweep`.
- [ ] **T005** [US1] Quote T002/T003 test output, the hash pairs and the counterfactual in the PR
  body.

Dependencies: T001 → T002 → T003 → T004 → T005.
