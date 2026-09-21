# Verification — Spec 208 (#2284)

**T013/T014, run against worktree `D:/Github/sse-2284`, branch `2284-stream-authorize-rate-limit`, HEAD `360a0fce`.**
**Machine state while these observations were taken: sustained memory pressure, ~6.3–6.9 GB free of 24 GB (Rider + Docker + browser resident the whole time).** Noted because two of the five findings below turned out not to be about the machine at all — see §2.

---

## T013 — full gate

| Check | Result |
|---|---|
| `dotnet restore` / `dotnet build SmartSentinelEye.slnx -c Release` | **Clean.** 0 Warning(s), 0 Error(s) on the incremental build. A forced `-t:Rebuild` of the touched projects (`Integration.Tests`, and the full solution) surfaced 217 warnings, **all** `S104`/`S107`/`S138`/`S1541` (ADR-0084's advisory code-metric analyzers, `warning`-severity and explicitly carved out of Release's `TreatWarningsAsErrors` — CLAUDE.md's own table says so). No `IDE1006` and nothing else appeared as a build warning; 0 errors either way. |
| `Architecture.Tests` (444 tests, includes `BoundaryTests`, `PrimitiveBoundaryTests`, `HandlerDeconstructionTests`, `StatusProducerDeclarationTests`/M15) | **444/444 passed**, 5 s. Re-confirms the orchestrator's earlier read. |
| Coverage gates (Domain ≥ 90 / Application ≥ 80 / Shared ≥ 90) | **Not run — by design, per T013's own instruction.** `git diff --stat origin/develop...HEAD` touches only `src/AppHost/AppHost.cs`, `src/StreamDistribution/Api/{Program.cs,StreamEndpoints.cs,appsettings.json,Log.cs}`, `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` (comments only — confirmed no line outside a `//`/`///` comment changed), `tests/Architecture.Tests/StatusProducerDeclarationTests.cs`, and the new `tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`. None of Domain, Application or Shared.* is in that list, matching plan.md's own layer table exactly. Movement in those gates would be a signal this diff shouldn't produce; none is expected. |
| `dotnet format SmartSentinelEye.slnx --verify-no-changes` | **Reports findings, none of them a gate this repo enforces, none introduced by this diff.** Whole-solution run: 468 pre-existing files "would be formatted" (whitespace), touching nothing this spec changed. Scoped to just the touched files: no whitespace reformatting, but `IDE1006` naming-rule warnings on every `private const`/`private static readonly` field in the new `WhepAuthorizeRateLimitTests.cs` (`PermitLimit`, `Window`, `PollInterval`, `AdmissionRecoveryTimeout`, `LogAbsenceGraceWindow`, `ThrottleTransitionMarkers`, `StreamDistributionResource`) and in the **pre-existing** lines of `StatusProducerDeclarationTests.cs` this diff only adds 10 lines to (`GuardSource`, `RouteHandlerMappingCount`, `MappingCall`, etc.). Confirmed by `git show origin/develop:...StatusProducerDeclarationTests.cs` that the identical PascalCase-private-field pattern predates this branch. **Verified this is not a real build gate**: a forced `-t:Rebuild` of `Integration.Tests` produced 0 warnings (IDE1006 does not fire under `dotnet build` even with `EnforceCodeStyleInBuild=true`, only under `dotnet format`'s own analyzer pass), and `.github/workflows/ci.yml` has no `dotnet format` step anywhere (`grep -n format` matches only the frontend's `pnpm format:check`). So this does not block CI. Recorded because it was asked for, not because it blocks anything — and because the new test file's naming does not match `.editorconfig`'s `private_fields_are_camel_case` rule any more than the file it edits already didn't. |
| `WhepHandshakeLatencyTests` (NFR-001 / SC-004) | **FAILED — and not a machine artifact. A real, reproducible design defect.** See §2.1. |
| Full `WhepAuthorizeRateLimitTests` class (7 facts), attempted twice as instructed | **2/7 passed both times, identically.** See §2.2 — this is not the "environmental, re-runs differently" pattern the brief described; it reproduced the exact same 2-pass/5-fail split, by name, across two independent stack boots. Root cause identified, not just observed. |

### §2.1 — `WhepHandshakeLatencyTests` fails deterministically under T002's test-mode ceiling

`WhepHandshakeLatencyTests.Whep_auth_hook_p95_stays_under_three_seconds_over_twenty_opens` (spec 002/#2149, **not** part of this spec, must stay green unmodified per plan.md) sends **one warm-up `POST /streams/authorize` plus 20 measured opens — 21 authorize calls total** from the test host's one address (`WhepHandshakeLatencyTests.cs:62-75`). T002's `isE2ETests` override sets `WhepAuthorizeRateLimiting__PermitLimit=20` (`AppHost.cs`). 21 > 20, so the 21st call — the test's own last measured iteration — is refused:

```
Shouldly.ShouldAssertException : response.StatusCode
should be HttpStatusCode.OK
but was HttpStatusCode.TooManyRequests
Additional Info: iteration 19: unexpected status.
```

This is not a re-run-and-it-passes flake. `PermitLimit=20` leaves **zero headroom** against a test whose own request count (21) was fixed before this spec existed. Every future run will hit this on iteration 19. **This is a T013 blocker**, and the fix is a config number, not a test edit — plan.md itself frames T002's ceiling as "the config, not the code" moving if a count assumption is wrong (SC-005's language, same principle). I did not change it: T002/AppHost.cs is outside my T013/T014 scope, and the plan's own gate says the number that needs to move here is `AppHost.cs`'s `isE2ETests` `PermitLimit`, which is exactly the kind of contention-file, cross-task edit I was not asked to make. **Flagging for the orchestrator/engineer**: raising the test-mode `PermitLimit` past 21 (with real margin — e.g. 30, mirroring the production 2.5× logic at a smaller scale) would very likely resolve this without touching the test.

No clean p95 figure was obtained this run because of the above — the run terminates on the assertion failure before printing the `output.WriteLine` summary line. **NFR-001/SC-004 is not currently held.** The historical figures this test recorded when it last ran clean (p95 13 ms / 20 ms, #2149, 2026-09-10, pre-dating this spec) are cited in the test's own doc comment, not re-measured here, and must not be reported as this spec's own observation.

### §2.2 — the `WhepAuthorizeRateLimitTests` circuit-breaker cascade has a mechanism, not just a symptom

Ran twice: once alongside `WhepHandshakeLatencyTests` (2 h34 m into the session), once alone, immediately after. **Both runs: identically `Passed: 2, Failed: 5`**, the exact same five named facts failing both times with `Polly.CircuitBreaker.BrokenCircuitException : The circuit is now open and is not allowing calls.` on the `stream-distribution`-standard resilience pipeline:

- **Passed** both times: `A_partition_entering_the_throttled_state_is_logged_once` (8 s / 10 s — this is the observation §3 below relies on), `Authorize_partitions_the_rate_limiter_by_remote_address_not_a_global_bucket` (asserts the partition-key shape, no HTTP — per T004's own note it "passes trivially", so it never touches the breaker).
- **Failed** both times, all `BrokenCircuitException`, 1–52 ms each (i.e. never reaching the network): `Health_and_readiness_are_never_throttled`, `A_throttled_authorize_never_reaches_the_handler`, `Repeated_refusals_within_the_same_window_do_not_add_a_second_log_record`, `Authorize_declares_the_429_it_can_answer`, `Authorize_refuses_a_source_over_its_window_with_429`.

**This is identical across two independent stack boots (run 1's background health-check noise was RabbitMQ/Postgres; run 2's was Minio/Postgres — different incidental symptom, same test-level result), which argues against pure machine-load flakiness and for a structural cause.** Traced it:

- `ExhaustWindowAsync()` sends `PermitLimit + 1` = 21 no-token requests per call; with `PermitLimit=20` exactly 1 of those 21 is a `429`.
- `WaitUntilAdmittedAgainAsync()` (called at the end of several facts, so the next test isn't poisoned) **polls every 250 ms until admitted**, up to `AdmissionRecoveryTimeout` (`Window + 20 s` ≈ 30 s) — i.e. up to ~120 polls, almost all of which are `429` while the window is still exhausted.
- `IdempotentRetry.cs` (ADR-0143) explicitly narrows only the **retry** predicate to idempotent methods; its own doc comment says outright: *"Timeouts and the circuit breaker are untouched... it simply gets one attempt."* The circuit breaker keeps the library's default `HttpClientResiliencePredicates.IsTransient`, which — undocumented in this repo, but this is `Microsoft.Extensions.Http.Resilience`'s public, documented default — classifies `429 TooManyRequests` as a transient failure for circuit-breaking purposes, independent of whether that outcome is retried.
- All of `AspireFixture`'s HTTP clients share the standard resilience handler per named client (`FixtureHttpClients.cs`), so every fact in this class reuses **one** `stream-distribution`-scoped circuit breaker across the whole test run.

Net effect: this class's own correctness-testing technique — deliberately exhausting the very rate limiter it verifies, then deliberately polling through repeated `429`s to detect recovery — feeds the shared client's circuit breaker enough transient-classified failures, within its rolling sampling window, to trip it after roughly the first one or two facts that call `WaitUntilAdmittedAgainAsync`. Every fact scheduled after that point fails instantly, regardless of what it actually asserts. **This is a test-infrastructure gap, not evidence against the limiter or the log record** — the two facts that never depend on a live exhaust/poll cycle passed both times, cleanly, including the one carrying US2's actual new behaviour.

**Not fixed here** (out of T013/T014's scope — verification only, per my brief), but worth being explicit about the shape of the fix, since it's cheap to get wrong: it is not "run it again," and not "increase retry" (the retry predicate is already correctly narrowed). It's the **circuit breaker's** `ShouldHandle` predicate for this specific test-scoped client, or `MinimumThroughput`/`FailureRatio`, that needs a fixture-local override so a deliberately-triggered `429` from *this* limiter doesn't count against it — mirroring the shape `IdempotentRetry.RetryEveryMethod()` already uses for a *different* per-client override.

**Given both of the above are real, reproducible defects rather than machine noise, I would not expect GitHub Actions CI's dedicated runner to clear this class either** — the mechanism is request-count and predicate-driven, not timing-driven. Recording this explicitly since the brief characterised local failures as probably environmental and CI as the authoritative check; the evidence gathered here says this specific class's cascade is not the load-dependent kind.

---

## T014 — Phase 5 observations

Plan.md §*Phase 5* asks for five observations, each with a figure written down.

### 1. The ceiling fires on the running stack, and exactly one log record is emitted

**Observed, on the real Aspire-orchestrated stack (Postgres, RabbitMQ, Keycloak, MediaMTX, MinIO, Mosquitto, StreamDistribution), twice independently.**

`A_partition_entering_the_throttled_state_is_logged_once`: exhausts the test-mode window (21 requests against `PermitLimit=20`, 10 s window; the 21st is `429`), then reads the `stream-distribution` log tail and asserts **exactly one** throttle-transition record. **Passed both times** (8 s, then 10 s — the difference is the log-tail poll settling, not a retry). This is the FR-009/US2 behaviour observed directly against the running service, not asserted from source.

### 2. A real wall at the production ceiling — nothing throttled

**Not completed. Recorded as an open item, not fabricated.** This needs the full run-mode stack (`dotnet run` on AppHost, production `PermitLimit=2000`, not the `isE2ETests`-gated test ceiling) plus a real wall opening tiles — `e2e/kiosk-shows-a-wall.spec.ts` / `e2e/support/live-video-wall.ts` are the established pattern for this. I chose not to attempt it today: booting the full run-mode stack (9 context services, the gateway at `WithReplicas(2)`, both web apps' dev servers, every container) is materially heavier than the Aspire-fixture integration boot I ran twice above, and those two lighter boots already showed real container-health instability (RabbitMQ dropping its AMQP connection mid-run; Minio and Postgres separately going unreachable) on this machine's ~6.3–6.9 GB-free state. Forcing a bigger boot on top of two already-shown-fragile ones risked a long, likely-failed run for a question §2.1 already answers more cheaply: the production ceiling (2000) has no interaction with `WhepHandshakeLatencyTests`' known 21-call pattern (20 ≪ 2000), so the class of bug found in §2.1 is specific to the test-mode override and would not reproduce at the production ceiling. That is inference, not this observation — flagging for a follow-up run once the machine is free or on a dedicated box.

### 3. SC-005 — authorize POSTs per single WHEP open

**Not completed — open item.** Requires the same run-mode stack as #2, with MediaMTX actually negotiating a WHEP session (a real kiosk tile or a manual WHEP client), so the authorize-POST count can be read off `stream-distribution`'s logs/traces for one open. Not attempted for the same resource reasoning as #2. The spec's assumption (1 POST per open, 2.5× margin covers up to 2) is **unverified by this verification pass** and remains an open risk item for the PR body.

### 4. What MediaMTX does with a `429`

**Not completed — open item.** Same dependency as #2/#3 (needs a live MediaMTX processing a real session against a throttled partition). #2160 already records the identical gap for `5xx`; this spec's assumption 3 (MediaMTX treats `429` as a deny, does not retry the hook) is stated in spec.md as an assumption to be observed, and remains unobserved after this pass.

### 5. Latency — `WhepHandshakeLatencyTests`' figure

**Not obtained. The test failed before reaching its measurement output — see §2.1.** No p95 figure was produced by this run; reporting one would be fabrication. The last recorded clean figure (p95 13 ms / 20 ms, #2149, 2026-09-10) predates this spec's changes and is not evidence for this spec's NFR-001.

---

## Summary for the PR body

- **Build/format/Architecture.Tests: clean.** No blocker there.
- **Two real defects found, both reproducible, neither fixed here (out of scope for T013/T014):**
  1. `WhepHandshakeLatencyTests` fails deterministically under `AppHost.cs`'s `isE2ETests` `PermitLimit=20` — the test's own known 21-call pattern has zero headroom against that ceiling. **Blocks NFR-001/SC-004 as currently configured.** Needs the test-mode `PermitLimit` raised (config, in `AppHost.cs`), not the test touched.
  2. The `WhepAuthorizeRateLimitTests` class's own exhaust/poll technique trips the shared `stream-distribution` HttpClient's circuit breaker (429 is transient by the resilience handler's default predicate, untouched by ADR-0143's retry-only narrowing), cascading `BrokenCircuitException` into 5 of 7 facts after the first one or two run. Reproduced identically twice. The two facts that don't depend on a live exhaust/poll cycle — including US2's actual new logging behaviour — passed both times.
- **Phase 5 observation 1 (429 + one log record) is directly confirmed against the real stack, twice.** Observations 2–4 (real wall at the production ceiling, SC-005's POST-per-open count, MediaMTX's 429 handling) and observation 5 (a clean latency figure) were **not** obtained — resource constraints for 2–4, and defect (1) above for 5 — and are recorded here as open items rather than guessed at.
