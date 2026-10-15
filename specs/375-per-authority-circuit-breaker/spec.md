# Spec 375: Each upstream authority gets its own circuit breaker

**Feature Branch**: `fix/2959-per-authority-circuit-breaker` (cut from `origin/develop`, fast-forwarded
to `911501d5`)

**Created**: 2026-10-15

**Status**: Implemented on this branch (phases 4-6 done); awaiting PR review. No `[NEEDS CLARIFICATION]`
remains. The owner's decision is recorded on the issue; the judgement calls are marked (§6).

**Input**: Issue [#2959](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2959), found
while reviewing the #2958 fix (spec 370). Owner decision (issue comment, 2026-10-15): per-authority
pipelines, wired in `ConfigureHttpClientDefaults` only; same authority still shares a breaker; nested
token-mint retry multiplication is accepted and documented. The decision is per-authority pipelines;
the **mechanism** is a custom `SelectPipelineBy` keyed on lower-cased `scheme://host:port`, not the stock
`SelectPipelineByAuthority()` the comment named: the stock key carries URL userinfo into the Polly
`pipeline.instance` metric tag and log lines (reproduced, §6 J1). That is the plan's own pre-approved
fallback, and it deviates from the letter of the recorded decision.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing**, so Phase 4a is **red** (constitution
§Testing): a failing upstream stops opening the breaker for a healthy upstream on another authority.
**Adversarial pass required** (failure mode: **outage** — the change exists to bound one upstream's
outage to that upstream, and a mistake either keeps the blast radius process-wide or removes the
breaker altogether). Engineer: **backend-engineer**. **Security-sensitive**: the revocation-list
refresher is fail-static (ADR-0160 §3), so a breaker opened by an unrelated upstream silently freezes
the revoked-client snapshot. Phase 6 runs `security-reviewer` as well as `/code-review`.

**Spec number.** 375. Checked 2026-10-15 after `git fetch`: `origin/develop` (`911501d5`) tops out at
373 (merged during this phase); every remote head was listed with `git ls-tree specs/` — 374 is
`origin/test/2914-widen-handler-deconstruction-guard` (open PR #2985); nothing at 375 on any local or
remote head. Open PRs #2970, #2811 and the dependabot PRs carry no spec. Re-check before the PR is
opened.

**ADRs.** **ADR-0143** (retry only idempotent methods; "its share of the circuit breaker" — this spec
makes that phrase literally true), **ADR-0160** (revocation checked at request time; §3 fail-static),
ADR-0088 (the standard handler on every client through `ConfigureHttpClientDefaults`), ADR-0144 (lane;
red colour; no ADR written by the lane), ADR-0036 (smallest change), ADR-0052/ADR-0103 (xUnit +
Shouldly; no Testcontainers).

**No new ADR, no ADR amendment.** ADR-0143:45 says a `POST` "still gets its per-attempt timeout, its
total budget and its share of the circuit breaker". Under today's wiring there is one breaker per
process, so "its share" reads as something it is not; after this change the breaker is per upstream
authority and the sentence is true as written. The owner recorded that reading on the issue. The lane
writes no ADR (ADR-0144); if review concludes otherwise, the outcome is **blocked**, not an ADR.

**Prior art reused.** `RetryEveryMethodAdversarialTests.Compose` (real `AddServiceDefaults()`, zero
delay and breaker tuning on the `-standard` options name) and its `CountingServer`; spec 370's
marker mechanism (`IdempotentRetry.RetryEveryMethodMarker`), unchanged.

---

## 1. Premise verified by experiment (2026-10-15)

### 1.1 The hazard reproduces in the repository

`dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~RetryEveryMethodAdversarialTests|FullyQualifiedName~IdempotentRetryTests|FullyQualifiedName~RetryEveryMethodCompositionTests"`
on `38c35d72`: **Passed 24, Failed 0** — including
`Hazard_one_failing_client_opens_the_circuit_breaker_for_a_healthy_sibling`, i.e. the healthy client on
`https://healthy.test` throws `BrokenCircuitException` with **0** attempts after a client on
`https://failing.test` opened the breaker. (`origin/develop` then moved to `911501d5`; nothing under
`src/ServiceDefaults` or `tests/ServiceDefaults.Tests` changed between the two.)

### 1.2 The pinned SDK expresses per-authority selection for the standard handler

**Decompiled** `Microsoft.Extensions.Http.Resilience` 10.10.0 (`lib/net10.0`, informational version
`10.10.0+02107c65…`, `Directory.Packages.props:91`) with `ilspycmd`:

- `AddStandardResilienceHandler(IHttpClientBuilder, Action<HttpStandardResilienceOptions>)` returns
  `IHttpStandardResiliencePipelineBuilder` whose `PipelineName` is `PipelineNameHelper.GetName(builder.Name,
  "standard")` — **`-standard`** for the unnamed defaults builder.
- `HttpStandardResiliencePipelineBuilderExtensions.SelectPipelineByAuthority(this
  IHttpStandardResiliencePipelineBuilder)` exists and registers `PipelineKeyOptions` under that same
  name with `ByAuthorityPipelineKeyProvider.GetPipelineKey`.
- The handler's `CreatePipelineSelector` reads the key provider for the pipeline name; with one set it
  resolves `ResiliencePipelineProvider<HttpKey>.GetPipeline(new HttpKey("-standard",
  instanceName))` **per request**. Each new `HttpKey` instance is built from the same builder and the
  same options name, so it gets **its own strategy instances — its own circuit breaker and its own
  concurrency limiter**, and the **same options** (`-standard`) as every other instance.
- `ByAuthorityPipelineKeyProvider` keys a cache on `(uri.Scheme, uri.Host, uri.Port)` and returns
  `uri.GetLeftPart(UriPartial.Authority)` as the instance name. Neither that cache nor Polly's
  registry evicts.

### 1.3 Probe (scratch console outside the repo, real pinned packages, the repo's built `IdempotentRetry`)

Composed exactly as `src/ServiceDefaults/Extensions.cs:43-55` does — `AddServiceDiscovery()`, then
`ConfigureHttpClientDefaults(http => { http.AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly)[.SelectPipelineByAuthority()]; http.AddServiceDiscovery(); })`
— zero retry delay, breaker `MinimumThroughput = 2`, `FailureRatio = 0.5` on `-standard` (E1-E3, E5;
E4 leaves the breaker at its defaults so it does not interfere with attempt counting).

| # | Scenario | Today (no selector) | With `SelectPipelineByAuthority()` |
|---|---|---|---|
| E1 | failing `https://failing.test` GET | `BrokenCircuitException`, 2 attempts | same — **the breaker still opens for the failing upstream** |
| E2 | then healthy `https://healthy.test` GET (other client) | **`BrokenCircuitException`, 0 attempts** | **200, 1 attempt** |
| E3 | two clients, same `https://keycloak.test:8443`; one fails, the other calls | `BrokenCircuitException`, 0 | `BrokenCircuitException`, 0 (**shared, intended**) |
| E3b | same host, other port `:9000` | `BrokenCircuitException` | **200** |
| E3c | same host and port, other scheme (`http://keycloak.test:8443`) | `BrokenCircuitException` | **200** (scheme is part of the key) |
| E4 | opted-in POST to `https://mediamtx.test:9997` (503) / plain POST to `https://other.test` | 4 / 1 | **4 / 1** |
| E4b | plain POST to the opted-in client's authority | 1 | **1** — the marker rides the request, not the pipeline |
| E4c | opted-in **synchronous** `Send` POST | 4 | **4** |
| E5 | client with `BaseAddress = http://identity`, service discovery resolving it to `http://localhost:5555` | 200, registry holds `(-standard, "")` | 200, registry holds **`(-standard, "http://identity")`** — the key is the **logical** name, taken before discovery rewrites the URI |

Registry dump for E1-E2 with the selector: `_pipelines: [(-standard, https://healthy.test), (-standard,
https://failing.test)]`; without: `[(-standard, "")]`.

**Conclusion.** The SDK expresses the owner's decision for the standard handler, in one chained call
at the existing registration, and the #2958 marker keeps reaching the pipeline that serves its
request. Not blocked.

### 1.4 Every `HttpClient` authority in `src` (bounded — no dynamic authority)

Every request URI is a `BaseAddress` or a token URL built from configuration, or a logical Aspire
resource name; per-request variation is in the **path only** (e.g. MediaMTX `/v3/config/paths/add/{path}`
for ~250 cameras is **one** authority). Searched: `AddHttpClient`, `CreateClient(`, `BaseAddress =`,
`new Uri($`, `RequestUri =` across `src/**/*.cs`.

| Host | Client | Authority | Source |
|---|---|---|---|
| every host with revocation (ServiceDefaults) | `RevocationListTokenProvider` (opt-in) | Keycloak | `KeycloakUrl` option |
| same | `HttpRevokedClientSource` | `http://identity` | literal, `AuthenticationDefaults.cs:280` |
| EventIngestion | `MqttTokenProvider` (opt-in) | Keycloak | option |
| Identity | `KeycloakAdminTokenProvider` (opt-in) | Keycloak | option |
| Identity | `HttpKeycloakAdminClient` | Keycloak (admin `BaseUrl`) | option — **same authority as its own token mint** |
| Identity | `EventIngestionWebhookIntegrationStatusLookup` | `http://event-ingestion` | literal |
| LayoutComposition | `CameraCatalogFabGuard` | `http://camera-catalog` | literal |
| StreamDistribution | `IRtspGateway` (MediaMTX, opt-in) | MediaMTX `ManagementUrl` | option |
| StreamDistribution | `CameraCatalogTokenProvider` (opt-in) | Keycloak | option |
| StreamDistribution | `CameraCatalogFabLookup` | `http://camera-catalog` | literal |
| SystemVariables | `OverlayDesignerTokenProvider` (opt-in) | Keycloak | option |
| SystemVariables | `overlay-designer` | `http://overlay-designer` | literal |
| ScenarioSimulator | `KeycloakTokenProvider` (opt-in), `CameraCatalogClient`, `CameraSimProvisioner` + `CameraSimPathClient` (one authority), four seeding clients | Keycloak, six service URLs | `SimulatorOptions` |

So a process holds at most ~7 pipeline instances (the simulator: six service URLs plus Keycloak). `JwtBearer`'s metadata/JWKS fetch uses its own
backchannel `HttpClient`, not the factory, and is unaffected. The unbounded-cache risk is real **only**
for a future client addressing caller-supplied hosts (e.g. outbound webhooks to arbitrary URLs); FR-007
makes that a stated constraint rather than a surprise.

## 2. User stories

### US1 (P1) — One upstream's outage stays that upstream's outage

As the operator of a fab, I want a MediaMTX or service outage to open the breaker for calls to
**that** upstream only, so that the revocation-list refresh, token mints and camera lookups to healthy
upstreams keep working — in particular so that a just-revoked client is not kept accepted because an
unrelated upstream was failing (ADR-0160 §3).

**Independent test.** `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~Resilience"`
— composed through the real `AddServiceDefaults()`, breaker tuned to open after 2 failures, against
counting fake upstreams. No stack needed.

```gherkin
Feature: The circuit breaker is per upstream authority

  Background:
    Given a host composed with AddServiceDefaults()
    And the "-standard" options set a zero retry delay and a breaker that opens at 2 calls and a 0.5 failure ratio

  Scenario: (happy) a healthy upstream on another host is still called after a sibling's breaker opens
    Given a client "failing" whose upstream at https://failing.test answers 503
    And a client "healthy" whose upstream at https://healthy.test answers 200
    When "failing" GETs until its breaker opens
    And "healthy" GETs https://healthy.test
    Then "healthy" gets 200 and its upstream sees exactly 1 attempt

  Scenario: (happy) the authority includes the port
    Given two clients addressing the same host on ports 8443 and 9000
    When the client on 8443 fails until its breaker opens
    Then the client on 9000 is still called

  Scenario: (outage) an opted-in client's failing POSTs do not freeze the revocation fetch
    Given a client "mediamtx" registered with RetryEveryMethod() whose upstream at http://mediamtx:9997 answers 503
    And a client "revocations" whose BaseAddress is http://identity, answered 200
    When "mediamtx" POSTs until its breaker opens
    Then "revocations" GET gets 200 and its upstream sees exactly 1 attempt
    And a further "mediamtx" POST is refused with BrokenCircuitException without reaching its upstream

  Scenario: (conflict — intended sharing) two clients to the same authority share one breaker
    Given clients "token" and "admin" both addressing https://keycloak.test:8443
    When "token" fails until the breaker opens
    Then "admin" is refused with BrokenCircuitException and its upstream sees 0 attempts

  Scenario: (conflict — the breaker is not removed) the failing upstream's own breaker still opens
    Given a client whose upstream answers 503
    When it has failed enough to open the breaker
    Then its next call is refused with BrokenCircuitException without reaching the upstream

  Scenario: (bad request) a 400 does not count toward any breaker
    Given a client whose upstream answers 400
    When it calls more times than the breaker's minimum throughput
    Then every call reaches the upstream and none is refused

  Scenario: (opt-in still reaches its pipeline) the marker and the selection compose
    Given "opted" registered with RetryEveryMethod() on authority A and "plain" without it on authority B
    When both POST to upstreams answering 503
    Then A's upstream sees 4 attempts and B's sees 1
    And "plain" POSTing to authority A sees 1 attempt
```

**Auth scenario.** The auth-relevant outcome is the outage scenario: the revocation fetch (an
authenticated inter-service call, ADR-0160) is not refused by a breaker another upstream opened. No
identity, scope or fab check changes.

### US2 (P1, same slice) — The nested token-mint multiplication is written down where it happens

As the next maintainer, I want the four call sites that mint a token **inside** another client's
retrying pipeline to say so and why it is accepted, so the multiplication is a known cost rather than
a discovery during an outage. Not separately testable (comment-only); proved comment-only by the
strip-comments hash (T012). Not separable from US1 because the per-authority split is what decides
whether the inner mint and the outer call share a breaker (§5).

## 3. Requirements

- **FR-001** `ConfigureHttpClientDefaults` in `src/ServiceDefaults/Extensions.cs` remains the only
  place the standard handler is added, and it selects the pipeline instance by request authority
  (`scheme://host:port`, lower-cased, never userinfo, path or query) with the custom selector
  `SelectPipelineByUpstreamAuthority()` (`src/ServiceDefaults/Resilience/UpstreamAuthority.cs`) chained
  on that one `AddStandardResilienceHandler(...)` call. The SDK's `SelectPipelineByAuthority()` is not
  used: see J1. Exactly one `AddStandardResilienceHandler(` across `src`
  (`ResilienceRegistrationTests`, unmodified).
- **FR-002** Requests to different authorities never share a circuit breaker: a breaker opened by one
  authority's failures does not refuse a call to another.
- **FR-003** Requests to the same authority share one breaker, whichever client sends them (owner
  decision: intended).
- **FR-004** The breaker still opens for the failing authority (no breaker is removed or disabled).
- **FR-005** Options are unchanged and uniform: every instance reads `HttpStandardResilienceOptions`
  named `-standard`; retry predicate, timeouts and breaker thresholds are those of today.
- **FR-006** `RetryEveryMethod()` (spec 370) behaves exactly as before under the selection: opted-in
  `POST`/`PATCH` 4 attempts on a transient failure, non-opted 1, on the async and synchronous paths,
  including when the opted-in and plain clients address different authorities.
- **FR-007** The selection key is the **pre-discovery** authority (the logical Aspire name where one
  is used). The set of keys a process can create must stay bounded: a client whose request authority
  is caller-supplied must not be added under these defaults without revisiting this spec. Recorded at
  the registration as a comment.
- **FR-008** The four authorization handlers that mint a token inside another client's pipeline carry
  a comment at their registration saying that the mint's retries nest inside the outer call's, why
  that is accepted (single-flight mint gate; the outer attempt timeout cancels the inner mint), what it
  costs (a mint failure counts against the outer upstream's breaker when it surfaces as the attempt's
  timeout or as the `HttpRequestException` from `EnsureSuccessStatusCode` on a 401; a credential the token
  authority rejects is retried by the outer call, 4 token POSTs per outer GET) and, for Identity's admin
  client, that the mint and the outer call share a breaker which can close without a real probe.
- **FR-009** Stale prose updated: `IdempotentRetry` class doc ("its share of the breaker" — now per
  authority), and the `RetryEveryMethodAdversarialTests` class doc's "Hazard" paragraph.

## 4. Tests (Phase 4a, red)

### 4.1 The one existing assertion that changes — declared

`tests/ServiceDefaults.Tests/Resilience/RetryEveryMethodAdversarialTests.cs`,
`Hazard_one_failing_client_opens_the_circuit_breaker_for_a_healthy_sibling`. Its own doc says: "If this
fact goes red, the breaker became per client; that is a change to ADR-0143's 'its share of the circuit
breaker', not a regression." It is **inverted in place** and renamed
`A_failing_upstream_does_not_open_the_circuit_breaker_for_a_healthy_upstream_on_another_authority`.
Arrangement unchanged; the assertions

```csharp
await Should.ThrowAsync<BrokenCircuitException>(
    () => factory.CreateClient("healthy").GetAsync("https://healthy.test/thing", CancellationToken.None));
healthyServer.Attempts.ShouldBe(0, "the healthy upstream was never asked: the shared breaker refused the call.");
```

become: the healthy GET returns 200 and `healthyServer.Attempts.ShouldBe(1)`. The first assertion
(failing client gets `BrokenCircuitException`) stays — it is FR-004. **Red** on unfixed code
(`BrokenCircuitException` thrown where 200 is expected); its output is quoted in the PR. Its doc's
"needs its own ADR" sentence is replaced: the owner decided on the issue that ADR-0143's wording holds.

### 4.2 New: `tests/ServiceDefaults.Tests/Resilience/PerAuthorityCircuitBreakerTests.cs`

Composed with the real `AddServiceDefaults()` (as `RetryEveryMethodAdversarialTests.Compose`).

| Fact | Before fix | After fix |
|---|---|---|
| `The_authority_includes_the_port` (8443 fails, 9000 still called) | **red** | green |
| `An_opted_in_client_failing_its_POSTs_does_not_freeze_the_revocation_fetch` (US1 outage) | **red** | green |
| `A_client_addressed_by_a_logical_service_name_is_isolated_by_that_name` (two logical names discovery resolves to one endpoint; one fails, the other still called) | **red** | green |
| `Two_clients_to_the_same_authority_share_one_breaker` | green | green — **declared guard** (FR-003; catches a per-client key) |
| `The_failing_authority_still_opens_its_own_breaker` | green | green — **declared guard** (FR-004; catches a "fix" that disables the breaker) |
| `A_bad_request_never_opens_a_breaker` | green | green — **declared guard** (calibration: the breaker counts transient failures only) |
| `The_opt_in_reaches_the_pipeline_of_the_authority_it_calls` (opted A = 4, plain B = 1, plain A = 1) | green | green — **declared guard** (FR-006 across authorities) |

Red evidence = 4.1 plus the three **red** rows. Guards are declared as such and are not offered as red
evidence.

### 4.3 Must pass unmodified

- All of `tests/ServiceDefaults.Tests` other than 4.1 — in particular `IdempotentRetryTests`,
  `RetryEveryMethodCompositionTests`, every other fact in `RetryEveryMethodAdversarialTests`,
  `ResilienceHandlerNestingTests`, `Revocation/RevocationCompositionCharacterisationTests` (token 4,
  fetch 1).
- `tests/Architecture.Tests/ResilienceRegistrationTests.cs` (one registration, inside
  `ConfigureHttpClientDefaults`) and the `*RegistrationCharacterisationTests` marker facts.
- `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs`.

### 4.4 Adversarial pass (Phase 4a, `test-adversary`, failure mode: outage)

After the fix, hunt for: a key that collapses distinct upstreams (e.g. discovery rewriting before
selection; IPv6/default-port normalisation — `https://h` vs `https://h:443` must be one key); a key
that splits one upstream into many (per-path, per-query); the synchronous `Send` path under selection;
concurrent first requests to a new authority racing pipeline creation (one breaker, not two); a breaker
opened on one authority surviving into another via the shared limiter. Each finding is a test that
fails or a written "not reproducible" with the attempt.

## 5. Nested token-mint retries (accepted, documented — FR-008)

Four clients run an authorization handler that mints a client-credentials token, through its own
opted-in client, **inside** the outer client's pipeline:

| Registration | Outer authority | Inner (mint) authority | Same breaker? |
|---|---|---|---|
| `AuthenticationDefaults.cs:282` `RevocationListAuthorizationHandler` | `http://identity` | Keycloak | no |
| `IdentityInfrastructureModule.cs:289` `KeycloakAdminAuthorizationHandler` | Keycloak (admin) | Keycloak | **yes** |
| `StreamDistributionInfrastructureModule.cs:298` `CameraCatalogAuthorizationHandler` | `http://camera-catalog` | Keycloak | no |
| `SystemVariablesInfrastructureModule.cs:171` `OverlayDesignerAuthorizationHandler` | `http://overlay-designer` | Keycloak | no |

In a Keycloak outage each outer attempt can drive up to 4 mint attempts, so token POSTs multiply
(the issue measured roughly 9 per outer call, from ~3 before #2958). Accepted because it is bounded: `ClientCredentialsTokenProvider`'s gate makes the mint single-flight and
the outer per-attempt timeout cancels the inner mint.

**Corrected by the adversarial pass (the earlier wording of this section was wrong, and the nested-mint limits are stated here,
both pinned by committed tests in `PerAuthorityPipelineRiskTests`):**

- Keycloak's own breaker is fed by **every transient mint outcome** (5xx, 408, 429, a connection failure, the
  mint's own timeout): the pinned package's `IsTransient` (Microsoft.Extensions.Http.Resilience 10.10.0) treats
  any `HttpRequestException`, any `TimeoutRejectedException`, 5xx, 408 and 429 as transient. A mint failure
  that reaches the outer handler as an `HttpRequestException` (any non-success status after the mint's
  retries, or a connection failure) or as the outer attempt's timeout **also counts against the outer
  service's breaker** and is retried by the outer call. Only a `BrokenCircuitException` from an open
  Keycloak breaker is non-transient to the outer predicate and ends the call.
- **400/401 are retried by the outer call.** The mint's own pipeline does not retry a 401, but the
  exception it raises is transient to the outer predicate, so a credential the token authority rejects
  costs **4 token POSTs per outer GET**, not 1
  (`Hazard_a_credential_the_token_authority_rejects_is_retried_by_the_outer_call`).
- The same-authority row (Identity admin) shares one breaker for mint and call. While it is half-open
  the outer call can take the only probe, the nested mint is refused, and the breaker can close without
  Keycloak having been asked anything
  (`Hazard_a_same_authority_nested_mint_lets_the_breaker_close_without_a_probe`). The selection narrows
  this hazard from every nested pair to this one.

## 6. Judgement calls (marked, not clarifications)

**J1 is superseded by the adversarial pass:** the stock `SelectPipelineByAuthority()` leaks userinfo
(`user:password`) into the Polly `pipeline.instance` metric tag and 12 log lines (verified), so the
fix uses a custom `SelectPipelineBy` keyed on lower-cased `scheme://host:port` only. Proved by
counterfactual with `PipelineTelemetryCredentialTests` (red against the stock selector, green with
the custom one). The J1 text below records the original reasoning.

- **J1 `SelectPipelineByAuthority` over a custom `SelectPipelineBy`.** The built-in is the SDK's own
  idiom, one call, and is what the owner named. Its instance name is
  `uri.GetLeftPart(UriPartial.Authority)`, which **includes userinfo** (`http://u:secret@h:1` verified
  in PowerShell) despite the doc comment's "redacted authority", and Polly emits the instance name in
  resilience telemetry (`pipeline.instance`). No HTTP URL in `src` carries userinfo (searched
  `https?://[^"' ]*@` over `src/**/*.cs` and `*.json`: none); all upstream URLs come from options and
  logical names. Recorded for `security-reviewer`; if review rejects the residual, the alternative is a
  three-line `SelectPipelineBy` keyed on `Scheme`, `Host`, `Port` only — not a hand-built pipeline.
- **N2 Key splits are harmless.** An IDN host, a trailing-dot FQDN and an IPv6 zone id can each split
  one logical upstream into two keys (two breakers). Accepted; `IdnHost` is deliberately not used, because
  it would carry the IPv6 zone text into telemetry.
- **J2 Different timeouts on one authority.** Not reachable: the options are per pipeline **name**, not
  per instance, so every instance reads `-standard`; no client configures its own resilience options
  or a finite `HttpClient.Timeout` (`ResilienceRegistrationTests.No_client_caps_the_resilience_pipeline_with_its_own_timeout`).
  Two clients to one authority therefore run identical budgets by construction.
- **J3 The concurrency limiter splits too — a liability removed, not a cost.** With one shared limiter a
  hung upstream holding its permits refused calls to every other upstream
  (`A_saturated_authority_does_not_use_up_the_concurrency_limit_of_another`). Original text: The standard pipeline's first strategy is a concurrency
  limiter (`HttpStandardResilienceOptions.RateLimiter`, default 1000 permits, no queue); it becomes per
  authority with the breaker. The process-wide ceiling rises from 1000 to 1000 × (authorities, ≤ ~7).
  Accepted: nothing relies on the process-wide cap, and the per-upstream cap is the more meaningful one.

## 7. Latency budget

**N/A.** No leg of the event-to-overlay path (constitution §IV) makes an `HttpClient` call: token
mints run at startup and refresh, MediaMTX `add/`/`patch/` at provisioning, the revocation fetch on a
background timer, camera/fab lookups on stream attribution. The per-request cost the selection adds is
two short strings allocated per request (the key, lower-cased) plus the SDK's pipeline-registry lookup; there is no key cache.

## 8. Out of scope (found, reported, not fixed here)

- `FixtureHttpClients.Configure` (`tests/Integration.Tests/Fixtures/FixtureHttpClients.cs`, the fixture's own defaults) keeps a
  single shared breaker; its comments at `AspireFixture.cs:286` and `:524` describe the fixture, not
  production, and stay true.
- Inter-display or any other leg: untouched.
- The synchronous `HttpClient.Send` path and Aspire service-discovery interplay were observed by the
  adversarial pass (`A_synchronous_send_to_a_healthy_authority_...` is green); nothing further is
  specified for either here.
