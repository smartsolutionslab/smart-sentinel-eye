# Spec 462: The rotate race compensation stands alone

**Issue:** #3138 (feature-level; no per-task issues) · **Branch:** `refactor/3138-rotate-race-class`
**ADRs:** ADR-0084 (code metrics, advisory — S104 ≤ 300 LOC/file is the trigger), ADR-0036 (smallest change; a
refactor changes shape, not behaviour), ADR-0144 (autonomous lane; characterisation colour), ADR-0139 (§Testing:
refactors stay green), ADR-0049 (post-commit work keeps `CancellationToken.None` / the compensation bound), ADR-0113
(the `DbUpdateConcurrencyException` exits are moved, not changed), ADR-0093 (Application layout).
**Specs built on:** 318 (pre-flight status), 426 (create-branch race-disable), 434 (compensation bound), 443 (a
disabled client is never announced rotated), 451/#3125 (rotate-branch re-check), 454 (replay version re-check), 348
(the order characterisation suite).
**Lane:** autonomous · **Phase-4a colour:** **CHARACTERISATION, observed green** (behaviour-preserving).
**Security-sensitive:** yes — webhook credential lifecycle. `security-reviewer` at phase 6 confirms no exit moved.
**New ADR needed:** no. **Contract change:** none (`Shared.Contracts`, HTTP, migrations, AppHost, DI untouched).
**Written 2026-10-25 against `develop` at `c683329c`** (after #3140 merged #3125).

---

## Premise, checked at `c683329c` — holds

1. `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs` is 639 physical lines; the issue
   records S104 at 377 LOC. The post-commit compensation cluster — `CompensationBound`, `RaceCheck`,
   `DisableIfRevokedSinceCommitAsync`, `DisableIfRevokedAsync`, `ShouldDisableAsync`, `RetryAfterFailedDisableAsync`,
   `Detach`, `PublishRetryAsync`, `AcknowledgeRaceDisableAsync`, `LogAlreadyDisabled`, `LogCouldNotDisable` — is
   cohesive and depends only on `integrationStatus`, `disableWebhookClient`, `events`, `commit`, `timeProvider`,
   `logger`.
2. The `IsExistingClient` flag forks behaviour in two places (`ShouldDisableAsync`: cancellation filter and
   `Unverifiable` handling) and logging in three (`AcknowledgeRaceDisableAsync`, `LogAlreadyDisabled`,
   `LogCouldNotDisable`). `WebhookClientRaceDisableFailedHandler` already passes a record of log delegates
   (`WebhookClientDisablingLogging`); that is the pattern to reuse.
3. **The constructor (S107, 9 parameters) is not in reach of a characterisation refactor.** Three test sites construct
   the handler positionally — `tests/Architecture.Tests/IdentityEndpointHost.cs:99`,
   `tests/Identity.Application.Tests/Commands/RotateWebhookClientCancellationTests.cs:44`,
   `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs:79`. Changing the
   signature edits those files, which the characterisation rule forbids. **The public constructor stays
   byte-identical; S107 on it remains** and is out of scope (see "Out of scope").

## User story (P1, the only one)

**As** a maintainer of the Identity context, **I want** the post-commit race compensation of a webhook rotation in its
own internal class, with the create/rotate difference expressed as data rather than a flag tested in five places,
**so that** the handler reads as the rotation it is and stays under ADR-0084's 300-LOC file limit — with every
observable behaviour exactly as it is today.

### Acceptance scenarios

```gherkin
Scenario: happy — every characterisation test passes unmodified
  Given the covering suites listed below, captured green on c683329c (plus the two added in T002/T003)
  When the compensation is moved into WebhookClientRaceCompensation
  Then every one of them passes with no edit to any existing test or fake file
  And `git diff origin/develop -- tests/` shows only the new characterisation file

Scenario: conflict — a concurrent async disable still reads as already disabled
  Given a revoke lands after the rotation's save and the async handler's save wins
  When the race disable's save throws DbUpdateConcurrencyException
  Then the failed entry is detached, "already disabled concurrently" is logged with the rotate-branch text,
       nothing is published and no commit runs
  (pinned by A_concurrency_conflict_from_the_inner_disable_on_a_rotation_is_read_as_already_disabled and
   A_concurrent_disable_leaves_the_repository_backed_commit_unrun)

Scenario: bad request — refusals before any write are untouched
  Given an invalid integration name, a revoked/unverifiable pre-flight status, or a version mismatch
  When the rotation is requested
  Then the same error codes are returned in the same order, before any port the compensation owns is called
  (pinned by the Order characterisation suite's refusal tests; the compensation is never reached)

Scenario: auth — fab scoping is untouched
  Given a Munich operator naming a Dresden-registered client
  When the rotation is requested
  Then the identical not-found refusal is returned
  (pinned by A_munich_operator_naming_its_own_fab_cannot_rotate_a_dresden_registered_webhook_client)

Scenario: the status re-check throws something other than a cancellation (characterised in T002)
  Given the create's row has committed and the post-commit status read throws HttpRequestException
  When the handler runs
  Then the exception propagates unconverted, nothing is announced, no retry is published, no second commit runs

Scenario: the bound expires during a create-branch re-check (characterised in T003)
  Given a new client whose post-commit status read never answers
  When the 30 s compensation bound expires
  Then "could not disable" is logged with the create-branch text, one WebhookClientRaceDisableFailed is published,
       the rotation is announced and committed once, and the call returns success
```

### Covering characterisation — must pass UNMODIFIED before and after

| File | Tests | Pins |
|---|---|---|
| `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs` | 40 | port order, catch filters, exact log text per branch (incl. `RotatedWebhookIntegration` still firing after a race disable — #3139's line) |
| `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs` | 29 | outcomes, create-branch race exits |
| `tests/Identity.Application.Tests/Commands/RotateWebhookClientCancellationTests.cs` | 11 | compensation bound (one timer, 30 s, `TimeProvider`), token unlinking, existing-client cancellation filter |
| `tests/Identity.Application.Tests/Commands/RotateWebhookClientRaceDisableOutboxCharacterisationTests.cs` | 3 | retry-publish-before-commit |
| `tests/Identity.Application.Tests/Commands/StaleVersionRejectionTests.cs` | 7 | Layer-1 refusals |
| `tests/Architecture.Tests/Persistence/IdentityRaceDisableDetachCharacterisationTests.cs` | 1 | `Detach` on a non-conflict `DbUpdateException` (real EF context) |
| `tests/Architecture.Tests/Persistence/IdentityRegistrationCharacterisationTests.cs` | 3 | real-context registration path |
| `tests/Architecture.Tests/WebhookRotationEndpointCharacterisationTests.cs`, `IdentityRouteTableCharacterisationTests.cs` | — | endpoint shape via `IdentityEndpointHost` (constructor signature) |
| `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerSupersededTests.cs`, `tests/Architecture.Tests/WebhookRotationReplaySupersededEndpointTests.cs` | — | spec 454 replay re-check — depends on `RollSecretAsync`'s save-before-roll order, which **stays in the handler untouched** |

Plus `The_rotate_branch_saves_then_rolls_the_secret_then_publishes_then_commits` specifically for the spec 454 order.

### Sites with no covering test (added green in T002/T003, before the move)

- **G1 — `catch (Exception ex) when (disabling)`, filter false.** A non-cancellation exception from the post-commit
  status read escapes `HandleAsync` unconverted. No status double in the repo can throw a non-OCE
  (`FakeWebhookIntegrationStatusLookup`, `RecordingWebhookIntegrationStatusLookup`,
  `TokenHonouringWebhookIntegrationStatusLookup`, `SecondCallNeverAnswersStatusLookup`). Moving the `disabling` flag is
  the easiest place to change this silently.
- **G2 — `catch (OperationCanceledException) when (isExistingClient)`, filter false.** Only the rotate side is covered
  (`A_bound_that_expires_during_the_rotate_recheck_leaves_the_live_client_enabled`). The create side — bound expiry
  during the re-check read reaches the outer OCE catch → failed-disable path — has no test. This is exactly the
  branch the flag→`DisablesOnUnverifiable` rename inverts.

## Independent end-to-end test procedure

Behaviour-preserving with no wire change, so "end to end" is the characterisation set above plus one live check:
run `Identity.Application.Tests` and `Architecture.Tests` (filter `FullyQualifiedName~Rotate|FullyQualifiedName~Identity`)
on `c683329c` + T002/T003 (green), apply T004–T006, re-run (green, unmodified). Phase 5 additionally boots the stack
and rotates an Active integration via `POST /webhook-integrations/{name}/rotate` once (create) and once (rotate,
`If-Match`), observing 200 and one `WebhookIntegrationRotatedV1` each — the same evidence spec 451 recorded.

## Locked tech choices

C# 14 / .NET 10, existing `ILogger` `[LoggerMessage]` extensions (no new log methods, no `Log.cs` edit), `Result`,
`Option`, `Ensure.That`, xUnit + Shouldly + hand-written fakes (ADR-0052/0054). No new package, no DI registration.

## Latency-budget impact

N/A — webhook credential rotation is an admin path, not on the event→overlay path (constitution §IV).

## Out of scope — separate issues

- **#3139** ("a 'Rotated webhook integration' log line still fires after a rotate-branch race disable") is a
  behaviour change. This refactor keeps `logger.RotatedWebhookIntegration` firing unconditionally in `HandleAsync`,
  pinned by `RotateWebhookClientOrderCharacterisationTests.cs:313`. Mixing the two would make that test change.
- **S107 on the handler constructor.** Removing it needs the 3 positional construction sites above edited — not
  characterisation-safe. If wanted, it is its own issue (likely injecting the compensation via DI, which also changes
  `IdentityInfrastructureModule`).
