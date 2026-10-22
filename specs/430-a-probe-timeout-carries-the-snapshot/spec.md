# Feature Specification: A probe timeout carries the resource snapshot

**Feature Branch**: `test/3076-fixture-probe-snapshot`

**Created**: 2026-10-22

**Status**: Draft — phases 1-3, autonomous lane (ADR-0144).

**Issue**: #3076 (*AspireFixture's 60-attempt probes throw TimeoutException and
never produce the resource snapshot*)

**ADRs**: ADR-0103 (integration tests boot the real Aspire stack, no
Testcontainers), ADR-0068 (the fixture), ADR-0037 (phases and gates), ADR-0139 /
constitution §Testing (new behaviour starts red), ADR-0144 (the lane; it may not
write an ADR), ADR-0052 / ADR-0053 (Shouldly, sentence-style test names).

**Predecessor**: spec 424 (#2146, PR #3075) — the bounded snapshot on the
eight-minute timeout, and the Docker-free capture tests this spec reuses.

> Every construct is named, not line-numbered, for the reason spec 098 gives.

---

## 1. The binding decision

Owner, 2026-10-21 (comment on #3076):

> add the resource snapshot (`CaptureResourceStatesAsync`) at the three
> 60-attempt probe throw sites, before each `TimeoutException`.
> Behaviour-preserving for passing runs; the failure message gains the snapshot.
> No ADR.

So this spec **does not** route the probes through the `InitializeAsync` catch,
**does not** change the probes' attempt count, delay, or the exceptions they
swallow, and **does not** add the failed-resource logs or the cause line to the
probe message (see §5, assumption A1). It changes what a probe that runs out of
attempts *says*. A diagnostic inside existing failure paths applies ADR-0103/0068
rather than amending them: **no ADR**.

## 2. Premise, re-verified against `origin/develop` (`6206ecf9`)

Search: `throw new TimeoutException` across `tests/Integration.Tests/Fixtures/AspireFixture*.cs`
(all six partials). Four hits, all in `AspireFixture.cs`:

| Construct | Snapshot today? |
|---|---|
| `InitializeAsync`'s `catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)` | **Yes** — `CaptureResourceStateMapAsync` → `FormatTimeoutMessage` (states, exit codes, bounded health, cause, failed logs). |
| `WaitForKeycloakRealmAsync` — `for (attempt < 60)` then `throw new TimeoutException("Keycloak realm 'smart-sentinel-eye' was not reachable after 60 attempts. …")` | **No.** |
| `WaitForMediaMtxAsync` — `for (attempt < 60)` then `throw new TimeoutException("MediaMTX /v3/paths/list was not reachable after 60 attempts.")` | **No.** |
| `WaitForServiceHealthAsync(resourceName, …)` — `for (attempt < 60)` then `throw new TimeoutException($"{resourceName} /health was not reachable after 60 attempts.")` (called twice: `identity`, `overlay-designer`) | **No.** |

The `when` filter does not match `TimeoutException` (it is not an
`OperationCanceledException`), so each probe's exception escapes `InitializeAsync`
unwrapped, with no snapshot. **Premise holds: exactly three throw sites** (four
call sites, because the health probe is called twice). The only other fixture
loop that gives up, `SendMediaMtxWithRetryAsync` in `AspireFixture.Db.cs`, throws
`HttpRequestException` during a per-test reset — not a probe, not a boot path,
out of scope.

## 3. User story

### US1 (P1) — A probe that gives up says what every resource was doing

As the engineer reading a red integration job, when the fixture's Keycloak,
MediaMTX or `/health` probe runs out of its 60 attempts, I want the failure
message to carry the same bounded resource snapshot the eight-minute timeout
carries, so that "X was not reachable" comes with the state, exit code and health
of every resource instead of naming a symptom.

**Why P1 / only story**: it is the whole issue; one slice.

**Acceptance scenarios**

```gherkin
Scenario: a probe that runs out of attempts carries the snapshot
  Given a resource notification service on which "rabbitmq" is Running with an
        Unhealthy health report "rabbitmq_check", and "camera-catalog" is Waiting
  When the probe-exhausted exception is built for
       "MediaMTX /v3/paths/list was not reachable after 60 attempts."
  Then it is a TimeoutException
  And its message starts with that probe sentence, unchanged
  And its message contains "Resource states:" followed by a line for
      "camera-catalog" and a line for "rabbitmq" naming its Unhealthy check

Scenario: the snapshot is bounded the way spec 424 bounds it
  Given a running resource carrying more health reports than MaxHealthReportLines
  When the probe-exhausted exception is built
  Then that resource's health lines in the message are capped exactly as
       FormatResourceStates caps them for the eight-minute timeout

Scenario: no resource has published anything
  Given a notification service with no snapshots
  When the probe-exhausted exception is built
  Then the message is the probe sentence followed by "Resource states:" and
       "  (app not built)" — the existing empty-map wording — and nothing throws

Scenario: every probe throw goes through the snapshot
  Given the fixture's source files
  Then "throw new TimeoutException(" appears exactly once across AspireFixture*.cs
       (the InitializeAsync catch)
  And each of the three "after 60 attempts" probe sentences is passed to the
      probe-exhausted helper
```

Bad-request / auth / conflict scenarios: **N/A** — test infrastructure, no
endpoint, no caller identity, no concurrent writer.

## 4. Independent test procedure (Docker-free)

Docker is down locally; the stack cannot be booted, and does not need to be.

```sh
dotnet build tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --no-build \
  --filter "Category=FixtureLogic" -- RunConfiguration.TreatNoTestsAsError=true
```

That is CI's "Docker-free fixture logic tests" step verbatim. The behaviour tests
publish real snapshots through an in-process `ResourceNotificationService`, as
`AspireFixtureResourceCaptureTests` does since PR #3075. The passing-run claim
("behaviour-preserving for passing runs") is discharged by the full integration
job in CI: a probe that succeeds returns before the new code runs.

## 5. Assumptions (explicit)

- **A1** — "the resource snapshot" means the state list
  (`FormatResourceStates` over `CaptureResourceStatesAsync`'s states, exit codes
  and health), not the whole eight-minute report. The cause line and
  failed-resource logs are not added. Reason: the owner named
  `CaptureResourceStatesAsync`, and the logs read costs up to five seconds per
  resource. Adding the cause line later is one concatenation.
- **A2** — the capture window is the three seconds `CaptureResourceStateMapAsync`
  already uses, so a failing probe takes ≤ 3 s longer to report. A passing run
  pays nothing.
- **A3** — the probe sentence stays first and unchanged, so anyone grepping CI
  logs for "was not reachable after 60 attempts" still finds it.

## 6. Locked tech / latency

xUnit + Shouldly, `Category=FixtureLogic`, Aspire.Hosting `ResourceNotificationService`
in-process. **Latency budget (constitution §IV): N/A** — test fixture only, no leg
of the event→overlay path.
