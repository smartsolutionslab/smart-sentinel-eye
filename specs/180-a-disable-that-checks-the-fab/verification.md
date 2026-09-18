# Verification 180 — A disable that checks the fab (#2240)

**Latency: N/A.** Identity admin write path (kiosk/device disable), not on the
event→overlay path constitution §IV budgets.

## Run A — unfixed code (the gate, ADR-0139)

Reproduced independently at phase-6 close (2026-09-18), because the value in
this run is that a *fresh* reader can still see the defect, not only that
phase 4a once saw it. Isolated in a throwaway worktree at commit `4edd546b`
(has the tests, not the fix), built and run against a **freshly booted**
Aspire stack — the persistent dev stack (pid 3312) was two days stale and
would not have exercised the pre-fix binaries faithfully
([[live-stack-can-outlive-the-code-it-runs]]).

First attempt failed to boot at all (Postgres/Keycloak/MinIO/RabbitMQ health
checks all timed out) — Docker was carrying orphaned containers from an
unrelated crashed fixture run alongside the 3-day-old persistent stack. A
second attempt against the same worktree booted cleanly. Recorded because a
failed-to-boot run and a genuine red run produce superficially similar
"Failed!" summaries; only the assertion content tells them apart.

```
Test run for D:\Github\sse-red-check\tests\Integration.Tests\bin\Release\net10.0\SmartSentinelEye.Integration.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:03:10.41]     SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.A_dresden_operator_naming_their_own_fab_cannot_disable_a_munich_kiosk [FAIL]
  Failed SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.A_dresden_operator_naming_their_own_fab_cannot_disable_a_munich_kiosk [9 s]
  Error Message:
   Shouldly.ShouldAssertException : attack.StatusCode
    should be
HttpStatusCode.NotFound
    but was
HttpStatusCode.OK

Additional Info:
    naming a fab the caller genuinely holds must pass the guard, so only a fab-scoped lookup can refuse; a munich kiosk must be invisible from dresden's own fab. got 200 "01a0b3b3-63d0-78d0-88a5-df5b70ec34ed"

[xUnit.net 00:03:12.01]     SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.The_victims_kiosk_stays_enabled_in_keycloak_after_a_cross_fab_disable_attempt [FAIL]
  Failed SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.The_victims_kiosk_stays_enabled_in_keycloak_after_a_cross_fab_disable_attempt [1 s]
  Error Message:
   Shouldly.ShouldAssertException : enabled
    should be
True
    but was
False

Additional Info:
    a refused cross-fab request must never reach Keycloak — 't180-wall-01a0b3b36b377f3f824292dcb2b20c13' must still answer enabled:true; a false here means the attack actually disabled the victim's client, the availability defect this spec exists to close

[xUnit.net 00:03:14.73]     SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.A_dresden_operator_cannot_disable_a_munich_device_naming_either_fab [FAIL]
  Failed SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.A_dresden_operator_cannot_disable_a_munich_device_naming_either_fab [1 s]
  Error Message:
   Shouldly.ShouldAssertException : namingVictim.StatusCode
    should be
HttpStatusCode.Forbidden
    but was
HttpStatusCode.OK

Additional Info:
    naming the victim's fab must be refused by the guard; got 200 "01a0b3b3-7b29-7617-b239-201eceb4eb36"

[xUnit.net 00:03:16.07]     SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.A_dresden_operator_naming_munichs_fab_cannot_disable_a_munich_kiosk [FAIL]
  Failed SmartSentinelEye.Integration.Tests.Identity.CrossFabDisableIntegrationTests.A_dresden_operator_naming_munichs_fab_cannot_disable_a_munich_kiosk [1 s]
  Error Message:
   Shouldly.ShouldAssertException : attack.StatusCode
    should be
HttpStatusCode.Forbidden
    but was
HttpStatusCode.OK

Additional Info:
    a dresden-only operator naming munich must be refused by the fab guard before any lookup runs; got 200 "01a0b3b3-7f73-77ab-bb1b-e6bbfcf26e25"

Failed!  - Failed:     4, Passed:     1, Skipped:     0, Total:     5, Duration: 15 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

I1, I2, I3 (both sub-cases) all answer **200/`OK`**, not merely a status-code
mismatch — a dresden-only operator genuinely disabled a munich kiosk and a
munich device, naming either fab. I5 reads the victim's own Keycloak client
back through the Admin API and gets `enabled: false` — the attack reached
Keycloak, not just this codebase's row. I4 (legitimate munich-on-munich) is
the one pass, unaffected either way. This is the red the phase-4a task
declaration required: "not a missing 403 — a 200."

## Run B — fixed code (branch tip, commit `8316b040`)

Re-run against a fresh boot of the same worktree pattern, on the actual PR
branch tip rather than the pre-cleanup commit, to confirm the phase-6 doc and
parameter-order edits didn't regress anything:

```
Test run for D:\Github\smart-sentinel-eye\tests\Integration.Tests\bin\Release\net10.0\SmartSentinelEye.Integration.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 14 s - SmartSentinelEye.Integration.Tests.dll (net10.0)
```

Also re-run at the branch tip: `RegisteredClientConcurrencyIntegrationTests`
(7/7 passed — confirms the `?fabId=munich` addition at line 54 didn't disturb
the existing rotation-concurrency gate), `Identity.Application.Tests` full
suite (63/63), and `Architecture.Tests` full suite (401/401, including
`HandlerDeconstructionTests` and `ConcurrencyConflictDeclarationTests`).

## Counterfactual — does T003 actually catch a regression back to the bug?

The backend-engineer who closed T003 (coverage for the message-parity
requirement — a foreign-fab client must read exactly like an unknown one)
reported it could not run this check itself: "the sandbox's security
classifier blocked editing the handler files even transiently." Performed
directly instead: reverted
`DisableKioskCommandHandler.cs`'s lookup from `GetWithinFabAsync` back to the
unscoped `GetByClientIdAsync` (discarding the now-unused `fab` local so the
counterfactual would compile), rebuilt, and ran
`DisableKioskCommandHandlerTests`:

```
[xUnit.net]     SmartSentinelEye.Identity.Application.Tests.Commands.DisableKioskCommandHandlerTests.Another_fabs_kiosk_returns_KioskNotFound_with_the_same_message_as_unknown [FAIL]
  Error Message:
   System.InvalidOperationException : Result is a success; no error.
Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4
```

Confirmed the new test genuinely exercises the regression it claims to catch.
Reverted the counterfactual (`git checkout --`), confirmed `git diff
--exit-code` clean, forced a rebuild (a restored file keeps its old
timestamp), and confirmed all 8 disable-handler tests pass again in the
known-good state before proceeding.

## T004 — not delivered as specified, and why that's the right call

`tasks.md` T004 asked for a new file under `tests/Identity.Infrastructure.Tests`
unit-testing `GetWithinFabAsync` directly, "mirroring
`CameraRepository.GetWithinFabAsync`." That mirror doesn't exist:
`CameraCatalog` has no dedicated infrastructure-level repository test file at
all — its `GetWithinFabAsync` equivalent is exercised only through
integration tests, the same way this PR's is. `tests/Identity.Infrastructure.Tests`
has no established pattern for a query that must actually execute against
Postgres (ADR-0103 rules out Testcontainers, and the one existing
`Infrastructure.Tests` repository test in the repo,
`EventRepositoryOutboxTests`, deliberately "never connects" — it tests
dispatch ordering, not a translated LINQ predicate). Adding a new DB-connected
test-harness pattern to that project, for coverage the integration suite
already provides more strongly (an isolated unit test can't prove the EF
`HasConversion` mapping translates to SQL; `CrossFabDisableIntegrationTests`
already does, against the real database), would be new test infrastructure
introduced to close a gap that isn't actually open. Recorded here rather than
silently dropped, since T004 was a written task and a reviewer reading only
`tasks.md` would expect the file to exist.

Cross-fab exclusion is covered by I1-I3. Disabled-row exclusion in
`GetWithinFabAsync` is unchanged, pre-existing logic copied verbatim from
`GetByClientIdAsync`'s own `DisabledAt == null` clause — not new behaviour
this PR introduces, so it carries no new risk this PR needs to newly cover.
