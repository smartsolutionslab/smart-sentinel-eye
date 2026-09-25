# Tasks 248 — The copies the realm never sees

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2466 · **Phase**: 3 (Tasks)
**Colour**: **red, by counterfactual** (spec §2, §5). The guard arriving green on the unmutated tree
is expected; a counterfactual that stays green is the phase-4a failure.
**Engineer**: `test-writer` (4a) only — no 4b (spec §6). C# fallback: `backend-engineer`.
**Reviewer**: `backend-reviewer`
**Tracking**: feature-level issue #2466 (already `agent:ready` on Project #13). No per-task issues.

Format: `[ID] [P?] [Story] description`. No foundational (Shared.Kernel / Contracts / AppHost) work.
No `[P]` markers: one new file, one agent, strictly sequential.

## Phase 4a — the guard (test-writer)

- [ ] **T001 [US1]** Create `tests/Integration.Tests/Identity/RealmImportMirrorTests.cs` per plan §2:
  six facts, `Trait("Category","FixtureLogic")`, expected values read from
  `src/AppHost/Realms/smart-sentinel-eye-realm.json` via a `RepositoryRoot()` walk mirroring
  `AppHostE2ESwitchTests.cs:218`. Site 4 facts use `new KeycloakAdminOptions()` with no initialiser.
  Site 2 fact composes the AppHost with no arguments and looks the secret up under the
  `KeycloakAdminOptions` default client id. No other file changes.
- [ ] **T002 [US1]** `dotnet build -c Release`; run the FixtureLogic filter (plan §4) and
  `IntegrationTestSelectionTests`. Capture verbatim. Expect all green with six new facts in the count.
  Commit `test(identity): guard the realm-import copies against silent drift`.
- [ ] **T003 [US1]** Counterfactuals C1–C5 exactly as spec §5, one at a time: mutate, rebuild
  `--no-incremental`, run the FixtureLogic filter, capture verbatim, revert, rebuild, `git diff`
  empty. **Required pattern**: C1 → 1c only; C2 → site-2 fact + `A_parameter_with_no_argument_keeps_its_default`;
  C3 → `A_parameter_with_no_argument_keeps_its_default` only; C4 → 4a only; C5 → 1a + 4a. Any other
  pattern → stop and report (do not adjust the guard to match; report the mismatch). No commit.

Depends: T001 → T002 → T003.

## Bookkeeping (orchestrator)

- [ ] **T004** PR body: `Closes #2466`; `Phase 4b: skipped — test-only guard, nothing to implement
  (spec §6)`; quote T002's green and all five T003 reds verbatim; state the premise correction
  (no csproj item needed — spec §1 row 6); list spec §1.1's out-of-scope copies as a possible
  follow-up. Re-check spec number 248 against unmerged branches before merge. After merge, confirm
  #2466 closed.

## Out of scope — do not do

- Any edit to `RealmProbe.cs`, `AppHost.cs`, `AppHostParameterOverrideTests.cs`,
  `KeycloakAdminOptions.cs`, the realm JSON, or `SmartSentinelEye.Integration.Tests.csproj` (except
  the reverted T003 mutations).
- Reading the realm file anywhere outside the new guard (a reader — refused by spec 191).
- Guarding the out-of-scope copies of spec §1.1; extracting a shared `RepositoryRoot` (spec 190's follow-up).
