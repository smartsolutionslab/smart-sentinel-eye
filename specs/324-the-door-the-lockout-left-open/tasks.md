# Tasks 324 — The door the lockout left open

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2508 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: `master` accounts now lock after repeated wrong passwords).
One guard test (plan §5 test 3) is green before and after, unmodified.
**Engineer**: `test-writer` (4a) then `infra-engineer` (4b) · **Reviewers**: `infra-reviewer` +
`security-reviewer` (security-sensitive: identity-provider admin credential).
**Tracking**: feature-level issue #2508. No per-task issues.
**New ADR**: none (plan §1 — the rejected Admin-API mechanism is the one that would have needed one).

Format: `[ID] [P?] [Story] description`. **No foundational task** and **no `[P]` tasks**: one test
file, one realm file, one comment. 4a and 4b need a full Aspire boot (Keycloak). One stack per
machine — confirm no other AppHost is running first.

## Phase 4a — red (test-writer; return verbatim output)

- [ ] **T001 [US1]** Create `tests/Integration.Tests/Identity/MasterRealmBruteForceIntegrationTests.cs`
  with the three facts in plan §5 (names, assertions, cleanup, no token bodies in messages).
- [ ] **T002 [US1]** Add
  `FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.MasterRealmBruteForceIntegrationTests.`
  to `tests/Integration.Tests/ci-shards/shard-2.filter`.
- [ ] **T003 [US1]** Run on unchanged production code:
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~MasterRealmBruteForceIntegrationTests|FullyQualifiedName~BruteForceLockoutIntegrationTests"`.
  Capture verbatim. **Required**: fact 1 red on `bruteForceProtected` false; fact 2 red because the
  correct password after the wrong ones **succeeded**; fact 3 green; every
  `BruteForceLockoutIntegrationTests` fact green; no compile error, no fixture boot failure. Any
  other outcome → stop and report. Commit
  `test(identity): prove the master realm accepts unlimited password guesses`.

Depends: T001, T002 → T003.

## Phase 4b — implement (infra-engineer; tests are read-only)

- [ ] **T004 [US1]** Create `src/AppHost/Realms/master-realm.json` exactly per plan §2 (UTF-8 BOM,
  the fourteen fields, the verbatim user-profile string; nothing else).
- [ ] **T005 [US1]** Update the comment at `src/AppHost/AppHost.cs:156-160` per plan §3. No code
  change.
- [ ] **T006 [US1]** Re-run T003's command on a fresh Keycloak (no persistent volume): all green,
  test file unmodified, fact 3 passing unmodified. Then run the whole `Identity` integration
  namespace to show SC-3. Release build of `src/AppHost`. Commit
  `fix(2508): enable brute-force protection on the Keycloak master realm`.

Depends: T003 → T004 → T005 → T006.

## Phase 5 — verify

- [ ] **T007 [US1]** Plan §6 against a fresh stack; quote redacted responses in the PR. Latency:
  N/A (administrative login path, no §IV leg).
