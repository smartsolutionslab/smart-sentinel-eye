# Tasks 254 — The copies 248 left behind

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2589 (feature-level; no per-task issues)
**Phase 4a colour**: red, by counterfactual. **No 4b.** Engineer: `test-writer`.

All tasks edit `tests/Integration.Tests` only and touch the same class, so there is **no `[P]`
parallelism** — one agent, in order.

## US-1 (P1)

- [ ] **T001 [US1]** Lift the two inline credential pairs to `private const` fields (spec D3):
      `FabGroupClaimIntegrationTests.cs:178-179`, `StreamFabAttributionIntegrationTests.cs:302-303`.
      Commit alone as `refactor(test): …`; Release build of `Integration.Tests` clean.
- [ ] **T002 [US1]** Add `partial` to `RealmImportMirrorTests.cs`. Nothing else in that file.
- [ ] **T003 [US1]** Create `RealmImportMirrorTests.ProductionDefaults.cs`: P1-P5 theory, J fact
      (spec §4.1, D4, D5). Depends on T002.
- [ ] **T004 [US1]** Create `RealmImportMirrorTests.IntegrationTestCopies.cs`: theories A and B over
      T1-T16 and the reflection helper (spec §4.2, D2, D3). Depends on T001, T002.
      Commit T002-T004 together as `test: …`; run `--filter "Category=FixtureLogic"` -c Release,
      confirm 38 new cases green in the trx.
- [ ] **T005 [US1]** Run counterfactuals C1-C7 (spec §5), `--no-incremental` each; capture verbatim;
      revert each; final `git diff --stat origin/develop` matches SC-3.
