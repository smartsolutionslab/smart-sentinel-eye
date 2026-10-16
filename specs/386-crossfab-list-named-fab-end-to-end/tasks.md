# Tasks 386: Named-fab list reads refused end to end on /devices and /webhook-integrations

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3001 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green in CI** (existing behaviour; tests are the
deliverable). Not observable locally (Docker); CI shard 2 is the first run.

**Roles:** test-writer only (T001-T003). **No engineer pass.** Phase 6: `/code-review` plus a
security-reviewer read of the assertions.

**Forbidden:** any file under `src/`; any `ci-shards/*.filter`; the fixture; a new test class or
helper; renaming existing facts.

No `[P]` markers: every task edits the same file, so they are sequential.

## US1 (P1)

- [ ] **T001 [US1]** Add I5-devices and I5-webhooks to
  `tests/Integration.Tests/Identity/CrossFabListIntegrationTests.cs` (plan §2).
- [ ] **T002 [US1]** Add I6-devices and I6-webhooks to the same file (plan §2), including the
  `ShouldNotContain(dresdenId)` assertion. Depends on T001 (same file).
- [ ] **T003 [US1]** Rewrite the class `<summary>` I5/I6 paragraph (FR-004). Then run plan §4 local
  commands; quote output. Depends on T002.

## Verify / PR

- [ ] **T010** Open the PR to `develop`; state that shard 2 is the first run. After CI, download the
  shard-2 TRX and quote the four new facts' outcomes.
- [ ] **T011** Plan §3.2 counterfactual on `throwaway/3001-counterfactual` via `workflow_dispatch`;
  quote which shard-2 facts went red (expected: the two new I5 facts only); delete the branch.
  Recommended; may run in parallel with T010's CI.
