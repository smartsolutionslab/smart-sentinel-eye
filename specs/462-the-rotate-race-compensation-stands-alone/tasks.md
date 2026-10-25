# Tasks 462: The rotate race compensation stands alone

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3138 (feature-level; no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). Every test in spec §"Covering
characterisation" plus T002/T003 must be seen **passing on `c683329c`** before T004 starts, and pass **unmodified**
after T006. An assertion that has to be edited is evidence the behaviour moved: block, do not adjust.

**Engineer:** backend-engineer. **Test-writer:** T001–T003. **Phase 6:** code-review **and** security-reviewer.

**Commits.** One per task, Conventional Commits, no `Co-Authored-By` (ADR-0086). Each commit builds **and passes**
on its own.

**Forbidden in every task:** any existing file under `tests/` (including fakes and `IdentityEndpointHost.cs`);
`src/Identity/Application/Log.cs`; the handler's public constructor signature; `RollSecretAsync`/`RegisterAsync`
order; the `logger.RotatedWebhookIntegration` call (#3139); `IdentityInfrastructureModule`; `.editorconfig`,
suppressions, thresholds.

No `[P]` tasks: one handler file is the contention point, and the two test tasks share one new file. Strictly
sequential.

---

## Phase 1: Baseline (no files)

- [ ] **T001** [US1] Run `dotnet test tests/Identity.Application.Tests` and the plan §5 Architecture.Tests filter on
  `c683329c`; record pass counts (expected ≥ 91 rotate-family tests green). Record the Release-build S104/S107 lines
  for `RotateWebhookClientCommandHandler.cs`. Output quoted in the PR.

## Phase 2: Fill the two coverage gaps (green on unmodified production code)

- [ ] **T002** [US1] G1 — in new `tests/Identity.Application.Tests/Commands/RotateWebhookClientRaceCompensationCharacterisationTests.cs`:
  `A_non_cancellation_failure_of_the_post_commit_status_read_propagates_and_nothing_is_announced` — create branch,
  status double answers `Active` then throws `HttpRequestException`; assert the exception escapes `HandleAsync`,
  the row is saved once, no `WebhookIntegrationRotatedV1`, no `WebhookClientRaceDisableFailed`, zero commits, inner
  disable never called. Add a rotate-branch twin. Hand-written status double local to the file (no fake file edit).
  Counterfactual: wrapping the re-check in a catch-all turns it red. **Depends on T001.**
- [ ] **T003** [US1] G2 — same file:
  `A_bound_that_expires_during_the_create_recheck_is_a_failed_disable_and_the_rotation_is_announced` — new client,
  second status read never answers, `FakeTimeProvider` advanced 31 s; assert no exception, log
  `CouldNotDisableClientCreatedDuringRevokeRace` exactly once, one `WebhookClientRaceDisableFailed`, one
  `WebhookIntegrationRotatedV1`, one commit, row still enabled. Counterfactual: removing `when (isExistingClient)`'s
  filter-false path (treating it as unverifiable) turns it red. **Depends on T002** (same file).

## Phase 3: The move (refactor; every suite green after each commit)

- [ ] **T004** [US1] Add `src/Identity/Application/Commands/Handlers/WebhookClientRaceLogging.cs` (plan §2). Unused
  record compiles alone. **Depends on T003.**
- [ ] **T005** [US1] Add `WebhookClientRaceCompensation.cs`; move the cluster and `RaceCheck` verbatim from the
  handler, applying only the plan §3 substitutions; handler builds the `RaceCheck` (with the branch's
  `WebhookClientRaceLogging`) and a per-call `WebhookClientRaceCompensation` local, then calls
  `DisableIfRevokedSinceCommitAsync`. Delete the moved members from the handler. Run plan §5 in full.
  **Depends on T004.**
- [ ] **T006** [US1] Tidy: remove now-unused `using`s from the handler (`dotnet format style --diagnostics IDE0005`),
  confirm doc comments moved with their members, re-run plan §5; `git diff --stat origin/develop -- tests/` shows only
  the T002/T003 file. **Depends on T005.**

## Phase 4: Verify

- [ ] **T007** [US1] Phase 5 evidence: boot the stack, `POST /webhook-integrations/{name}/rotate` once without and once
  with `If-Match`, observe 200 + one `WebhookIntegrationRotatedV1` each; quote metric before/after (S104 cleared,
  S107 unchanged and out of scope). **Depends on T006.**
