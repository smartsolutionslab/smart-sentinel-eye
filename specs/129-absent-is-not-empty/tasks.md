# Tasks 129 — absent is not empty

Phase 4a colour: **both, in order** — a characterisation GREEN for the shape
change (T002), then a behaviour-changing RED for the verdicts (T004).
plan.md, "Why C1 comes before C2", states what that ordering costs the red
and how the cost is paid.

- **T001** Baseline the characterisation net: run `MigrationRunner.Tests`
  and record all ten passing, with per-test durations. This is also the
  measurement that refutes #2139's "30 s" claim. **Done before any edit.**
- **T002** *(C1, refactor)* Change
  `IKeycloakAdminClient.GetSubGroupNamesAsync` to
  `Task<Option<IReadOnlyList<string>>>` and rewrite the doc paragraph that
  currently claims it "never returns empty to mean 'could not tell'". Map
  404 to `None` and every success to `Some` in `HttpKeycloakAdminClient`.
  Update all five fake implementations across four test files.
  `KeycloakProvisionedFabSource` keeps its single hedged message for both
  cases. **Run the net: ten green, unmodified.** Any edited assertion
  blocks.
- **T003** `dotnet build -c Release` on T002 — 0 warnings (SC-002). AppHost
  stopped first (MSB3027).
- **T004** *(C2, test)* Add
  `Tells_an_absent_fabs_group_apart_from_one_that_has_no_children` to
  `KeycloakProvisionedFabSourceTests`, and the absent-mode factory the stub
  needs. Assert the discriminator across all three fatal verdicts — absent,
  childless, unusable — not the prose. Add `FabGroupTreeTests` in
  `Identity.Infrastructure.Tests` over `StubKeycloakHandler`: 404 gives
  `None`, 200-with-no-children gives `Some([])`, a 403 still throws
  (FR-003). **Run. Capture the failure verbatim** — the MigrationRunner test
  must fail on its assertion, not on a compile error. That output is the
  phase-4a artifact.
- **T005** Prove the Infrastructure pins discriminate by counterfactual on a
  throwaway copy of the tree: revert the 404 arm to `Some([])` and observe
  both go red. Never in the worktree. Record in `verification.md`.
- **T006** *(C3, fix)* `KeycloakProvisionedFabSource` gives three verdicts
  (FR-004), each naming a different thing to go and look at, all three
  aborting (FR-005). No wait, no poll, no retry (FR-006).
- **T007** Re-run `MigrationRunner.Tests`, `Identity.Infrastructure.Tests`,
  `Identity.Application.Tests` and `Architecture.Tests`. Record counts and
  the duration of `Throws_when_the_realm_has_no_fab_groups_at_all` (SC-003).
- **T008** `dotnet build -c Release` — 0 warnings, 0 errors. Write
  `verification.md`.
