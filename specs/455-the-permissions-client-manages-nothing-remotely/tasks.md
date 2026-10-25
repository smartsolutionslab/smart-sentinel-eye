# Tasks 455: The permissions client manages nothing remotely

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #3121 (feature-level, on Project #13)

Format: `[ID] [P?] [Story] description — file(s)`. One story (US1). No foundational tasks: Shared.Kernel,
Shared.Contracts and AppHost code are untouched.

## Phase 4a — tests first (test-writer)

- [ ] T001 [P] [US1] Guard `The_permissions_client_does_not_allow_remote_resource_management` plus counterfactuals
  (`true` seen, property removed seen) — `tests/Architecture.Tests/IdentityAdminAllClientsGrantTests.cs`.
  **Red** on the committed realm (`true`).
- [ ] T002 [P] [US1] Matrix: delete the premise fact and its comment; add `[InlineData("admin-permissions")]`;
  `MintByRotationAsync` via identity-admin — `tests/Integration.Tests/Identity/MigrationRunnerConfinementIntegrationTests.cs`.
  **Expected green** (spec, Testing): quote that sentence with the output.
- [ ] T003 [US1] Fact `A_rotated_permissions_client_credential_is_refused_the_protection_api` (400, `not_supported`)
  — same file as T002, so after it. **Red** on the committed realm (200).
- [ ] T004 [US1] Run T001-T003 (Architecture.Tests; the integration class on the Aspire fixture — one stack per
  machine) and return the verbatim output. Depends on T001-T003.

## Phase 4b — implementation (infra-engineer; may not edit tests)

- [ ] T005 [US1] `allowRemoteResourceManagement: false` on `admin-permissions` — `src/AppHost/Realms/smart-sentinel-eye-realm.json`.
  Depends on T004.
- [ ] T006 [US1] Re-run T001-T003 green; full `Architecture.Tests`; `MigrationRunnerConfinementIntegrationTests`,
  `IdentityAdmin*` and the realm-import integration classes green on a fresh stack. Depends on T005.

## Phase 5-6

- [ ] T007 [US1] Verify end to end on a fresh stack: rotate as identity-admin, mint, protection API 400
  `not_supported`, migration-runner 403 x4; enrolment still 201. Note on the PR.
- [ ] T008 [P] [US1] infra-reviewer. — [ ] T009 [P] [US1] security-reviewer. Both after T007.

## Parallelism

T001 and T002 own disjoint files (`[P]`); T003 shares T002's file. Everything in 4b depends on the red evidence from
T004. Single engineer; no fan-out beyond the two 4a files and the two reviewers.

## Commits (ADR-0030)

1. `test(3121): ...` T001-T003 (red pair head) · 2. `fix(realm): ...` T005 (green) — each builds on its own.
