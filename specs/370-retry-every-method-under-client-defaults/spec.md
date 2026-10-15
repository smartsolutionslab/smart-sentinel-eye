# Spec 370: `RetryEveryMethod()` reaches the pipeline `ConfigureHttpClientDefaults` builds

**Feature Branch**: `fix/2958-retry-every-method-under-client-defaults` (cut from `origin/develop` at `a1554465`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Three judgement calls
are marked (§6).

**Input**: Issue [#2958](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2958), found
while reviewing PR #2957 (spec 369).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing**, so Phase 4a is **red** (constitution
§Testing): an opted-in client's `POST` goes from 1 attempt to 4. Engineer: **backend-engineer**.
**Security-sensitive** (retry safety, ADR-0142/ADR-0143): the fix touches the predicate every
`HttpClient` in every host runs, and a mistake **fails toward duplicate effects** — a non-opted
client whose `POST` starts being retried. Phase 6 runs `/security-review` as well as `/code-review`.

**Spec number.** 370. Checked 2026-10-15 after `git fetch`: highest spec on any local or
`refs/remotes/origin/*` head is 369 (`origin/develop` at `a1554465`). Open PRs: #2837 (dependabot),
#2811 (devcontainer); neither carries a spec. Re-check before the PR is opened.

**ADRs.** **ADR-0143** (retry only idempotent methods; the opt-in this spec makes work), ADR-0088
(the standard handler applied to every client through `ConfigureHttpClientDefaults`), ADR-0142
(idempotency keys — the other half, untouched), ADR-0144 (lane; red colour; no gate weakened),
ADR-0084 (S138 <= 30 lines), ADR-0105 (`Ensure.That`), ADR-0036 (smallest change), ADR-0052/ADR-0103
(xUnit + Shouldly; no Testcontainers).

**No new ADR, no ADR amendment.** ADR-0143 §Decision states the intended behaviour unambiguously:
"`POST` and `PATCH` get one attempt. A client whose non-idempotent calls are idempotent *in fact*
opts back in with `RetryEveryMethod()`", and its opt-in table justifies each opt-in by the retries
it buys ("Losing these retries would let a transient Keycloak blip fail a host's startup outright";
"a MediaMTX blip during provisioning would leave a stream unprovisioned"). The code does not do
what the ADR says; this spec makes it. Amending ADR-0143 to drop the opt-in would reverse a decision
whose stated reasons still hold, and the lane may not do that (ADR-0144). §5 R3 records why it was
not chosen.

**Prior art reused.** `IdempotentRetryTests`' `CountingHandler` and zero-delay attempt counting;
`ResilienceHandlerNestingTests`' `InnerHandler` walk; `MediaMtxProbeLogVolumeTests.BuildGatewayHost`
(`builder.AddServiceDefaults()` on a real host builder); `PostgresComposition.Compose` in the
registration characterisation tests.

---

## 1. Premise verified (2026-10-15, `a1554465`)

A throwaway probe in `tests/ServiceDefaults.Tests` (deleted, not committed) composed exactly as
`src/ServiceDefaults/Extensions.cs:43-55` does — `services.ConfigureHttpClientDefaults(http =>
http.AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly + zero delay))`, then
`services.AddHttpClient("probe")` with and without `.RetryEveryMethod()` — against a server
answering 503:

| Method | Opt-in | Attempts | `-standard` options carry the zero delay | `probe-standard` options carry it |
|---|---|---|---|---|
| GET | no | 4 | yes | no |
| GET | yes | 4 | yes | no |
| POST | no | 1 | yes | no |
| **POST** | **yes** | **1** | yes | no |

**The issue's finding reproduces.** The defaults builder has no name, so the pipeline it adds reads
`HttpStandardResilienceOptions` named **`-standard`** for every client. `RetryEveryMethod()`
(`src/ServiceDefaults/Resilience/IdempotentRetry.cs:88-99`) configures options named
**`{client}-standard`**, which no pipeline reads. Every opt-in in production is inert.

**History.** `git log` on `src/ServiceDefaults/Resilience` and `Extensions.cs`: the opt-in and the
defaults registration both arrive in `9a9d6722` ("fix(http): only idempotent methods are retried")
and are unchanged in substance since (`fc849a03` is `var`/usings only). It has been inert since it
was written.

**Why nothing caught it.** `IdempotentRetryTests.Build` (lines 123-135) says it models "the order
ConfigureHttpClientDefaults produces" but registers the handler **per client**, so its pipeline reads
`probe-standard` and the opt-in wins. The revocation harness
(`RevocationCompositionCharacterisationTests.cs:263-270`) knows and says so. The three registration
characterisation tests pin that a `{client}-standard` named configuration *exists*
(`EventIngestionRegistrationCharacterisationTests.cs:229-233`,
`StreamDistributionRegistrationCharacterisationTests.cs:195-208`), not that it is read.

**Clients affected — every production call site (7):**

| # | Call site | Client | Non-idempotent call |
|---|---|---|---|
| 1 | `src/EventIngestion/Infrastructure/EventIngestionInfrastructureModule.cs:302` | `MqttTokenProvider` | `POST` token |
| 2 | `src/Identity/Infrastructure/IdentityInfrastructureModule.cs:285-286` | `KeycloakAdminTokenProvider` | `POST` token |
| 3 | `src/ScenarioSimulator/Program.cs:41` | `KeycloakTokenProvider` | `POST` token |
| 4 | `src/ServiceDefaults/AuthenticationDefaults.cs:257` | `RevocationListTokenProvider` | `POST` token |
| 5 | `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs:75-102` | `IRtspGateway` (MediaMTX) | `POST add/`, `PATCH patch/` |
| 6 | `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs:282` | `CameraCatalogTokenProvider` | `POST` token |
| 7 | `src/SystemVariables/Infrastructure/SystemVariablesInfrastructureModule.cs:151` | `OverlayDesignerTokenProvider` | `POST` token |

In every one, `.RetryEveryMethod()` is the **first** call on the builder after `AddHttpClient`. None
of these call sites changes.

**Direction of the defect.** Fail-safe: fewer attempts, never a duplicate apply. The cost is the one
ADR-0143 names: a transient Keycloak or MediaMTX blip fails a token mint or a provisioning call on the
first attempt.

**Not on a live stack.** The probe composes the real library at the pinned version
(`Microsoft.Extensions.Http.Resilience` 10.10.0, `Directory.Packages.props:91`) through the same
calls `AddServiceDefaults` makes; it does not boot Aspire. US1 scenario 4 closes that gap with a test
that calls `AddServiceDefaults()` itself.

## 2. User stories

### US1 (P1) — An opted-in client retries its `POST`s and `PATCH`es under the production pipeline

As the operator of a fab, I want a host whose token mint (or MediaMTX provisioning call) hits a
transient 5xx to try again as ADR-0143 says it does, so that a Keycloak or MediaMTX blip does not
fail a host's startup or leave a stream unprovisioned.

**Independent test.** `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~Resilience"`
— attempt counts through a pipeline composed by `ConfigureHttpClientDefaults` (and once by
`AddServiceDefaults()` itself) against a 503 / throwing server. No stack needed.

```gherkin
Feature: RetryEveryMethod under ConfigureHttpClientDefaults

  Background:
    Given the standard resilience handler is applied to every client through ConfigureHttpClientDefaults
      with the idempotent-only predicate, as AddServiceDefaults does
    And the retry delay is zero

  Scenario: (happy) an opted-in client's failing POST is retried
    Given a client "probe" registered with RetryEveryMethod()
    When it POSTs to a server that answers 503 every time
    Then the server sees 4 attempts

  Scenario: (happy) an opted-in client's failing PATCH is retried
    Given a client "probe" registered with RetryEveryMethod()
    When it PATCHes to a server that answers 503 every time
    Then the server sees 4 attempts

  Scenario: (happy, exception path) an opted-in client's POST is retried when the transport throws
    Given a client "probe" registered with RetryEveryMethod()
    When it POSTs to a server whose transport throws HttpRequestException every time
    Then the server sees 4 attempts

  Scenario: (production composition) the opt-in holds through AddServiceDefaults itself
    Given a host builder that calls AddServiceDefaults()
    And a client "probe" registered with RetryEveryMethod()
    When it POSTs to a server that answers 503 every time
    Then the server sees 4 attempts

  Scenario: (conflict — the opt-in must not leak) a sibling client without the opt-in is untouched
    Given a client "opted" registered with RetryEveryMethod()
    And a client "plain" registered without it, in the same container
    When "opted" has POSTed (and been retried)
    And "plain" POSTs to a server that answers 503 every time
    Then "plain"'s server sees 1 attempt

  Scenario: (bad request — a non-transient answer is not retried, opt-in or not)
    Given a client "probe" registered with RetryEveryMethod()
    When it POSTs to a server that answers 400 every time
    Then the server sees 1 attempt

  Scenario: (unchanged default) a client without the opt-in still gets one POST attempt
    Given a client "probe" registered without RetryEveryMethod()
    When it POSTs, or PATCHes, to a server that answers 503 every time
    Then the server sees 1 attempt
    And a GET or PUT to the same server sees 4 attempts
```

**Auth scenario.** Not applicable as a separate scenario: the change is to a client-side retry
predicate and carries no identity. The auth-relevant consequence (a token mint retried) is the
happy path above.

### US2 (P1, same slice) — The registration guards observe the opt-in that is read

As the next maintainer, I want the per-context characterisation tests to fail when a client loses
its opt-in, by observing the mechanism the pipeline actually reads, so that this defect cannot recur
silently.

```gherkin
  Scenario: the MediaMTX gateway, camera-catalog token and MQTT token clients carry the opt-in
    Given the StreamDistribution and EventIngestion hosts composed as in production
    When the named clients "IRtspGateway", "stream-distribution-attribution-token" and the MQTT token client are built
    Then each one's handler chain carries the RetryEveryMethod marker

  Scenario: (counterfactual) removing .RetryEveryMethod() from AddMediaMtxGateway fails the guard
```

US2 is not separable from US1: the existing three facts pin a `{client}-standard` named
configuration that the fix removes, so they go red with US1's production change and must be
rewritten in the same slice (§4.3).

## 3. Requirements

- **FR-001** Under a pipeline added by `ConfigureHttpClientDefaults(http =>
  http.AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly))`, a client registered
  with `.RetryEveryMethod()` retries every method on the library's transient predicate
  (`HttpClientResiliencePredicates.IsTransient`): 4 attempts for `POST`/`PATCH`/`GET` against a
  persistent 503 or transport exception.
- **FR-002** A client **not** registered with `.RetryEveryMethod()` is unchanged: 1 attempt for
  `POST`/`PATCH`, 4 for `GET`/`HEAD`/`PUT`/`DELETE`/`OPTIONS`/`TRACE`, in the same container as an
  opted-in client.
- **FR-003** "Where the method cannot be determined, nothing is retried" (ADR-0143) still holds:
  a null request is not retried, opt-in or not.
- **FR-004** Still exactly one standard resilience handler per client and exactly one
  `AddStandardResilienceHandler(` call across `src` (`ResilienceRegistrationTests`, unmodified).
  Timeouts and the circuit breaker are untouched (ADR-0143 "Only the retry predicate narrows").
- **FR-005** None of the seven call sites in §1 changes.
- **FR-006** `RetryEveryMethod()` no longer configures `{client}-standard` named options (it is the
  inert half; leaving it would be dead configuration that reads as the mechanism).
- **FR-007** `IdempotentRetryTests` composes through `ConfigureHttpClientDefaults`, so its comment
  at lines 123-124 becomes true.

## 4. Tests (Phase 4a, red)

### 4.1 `tests/ServiceDefaults.Tests/Resilience/IdempotentRetryTests.cs` (edited harness + new facts)

`Build` composes through `services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(...))`
— the same lambda body as today — then `services.AddHttpClient(Client).ConfigurePrimaryHttpMessageHandler(...)`
and, when opted in, `.RetryEveryMethod()`. Expected on unfixed code:

| Fact | Before fix | After fix |
|---|---|---|
| the 6 existing method facts (GET 4, POST 1, PATCH 1, PUT 4, thrown GET 4, thrown POST 1) | green | green, **unmodified bodies** |
| `A_client_that_opts_back_in_retries_its_POSTs_again` (existing, 4) | **red: 1** | green |
| new: opted-in `PATCH` -> 4 | **red: 1** | green |
| new: opted-in thrown `POST` -> 4 | **red: 1** | green |
| new: opted-in `POST` answered 400 -> 1 | green | green (guard: opt-in restores the *transient* predicate, not "retry anything") |
| new: sibling without opt-in, same container, after the opted-in one sent -> 1 | green | green (guard against a fix that widens the shared predicate) |

The two green-from-start facts are declared guards, not red evidence. The red evidence is the three
**red: 1** rows; their failure output is quoted verbatim in the PR.

### 4.2 `tests/ServiceDefaults.Tests/Resilience/RetryEveryMethodCompositionTests.cs` (new)

One fact: `Host.CreateEmptyApplicationBuilder` + `builder.AddServiceDefaults()` +
`builder.Services.AddHttpClient("probe").ConfigurePrimaryHttpMessageHandler(...).RetryEveryMethod()`;
zero delay via `Configure<HttpStandardResilienceOptions>("-standard", ...)` (the name the defaults
pipeline reads — §1); POST to 503 -> **4**. Red on unfixed code (1). This is the guard against
`Extensions.cs` drifting from what 4.1 models.

### 4.3 Registration characterisation tests (rewritten assertions — declared)

`EventIngestionRegistrationCharacterisationTests.The_mqtt_token_client_opts_back_in_to_retrying_every_method`
and `StreamDistributionRegistrationCharacterisationTests.The_mediamtx_gateway_client_opts_back_in_to_retrying_every_method`
/ `The_camera_catalog_token_client_opts_back_in_to_retrying_every_method` currently assert a
`ConfigureNamedOptions<HttpStandardResilienceOptions>` named `{client}-standard` exists. That
configuration is the inert mechanism FR-006 removes. They are rewritten to build the named client's
handler (`IHttpMessageHandlerFactory.CreateHandler(name)` on the composed provider) and walk
`InnerHandler` for the marker handler, as `ResilienceHandlerNestingTests` does. **Red** before the
production change (no marker exists), green after. The rewrite is declared here and in the PR body:
it is not an assertion edited to pass, it is the same claim ("this client opts in") observed through
the mechanism that is read. Counterfactual recorded in the PR: delete `.RetryEveryMethod()` from
`AddMediaMtxGateway` -> the gateway fact fails.

### 4.4 Must pass unmodified (characterisation of what does not move)

- `tests/ServiceDefaults.Tests/Revocation/RevocationCompositionCharacterisationTests.cs` — its token
  fact (4) and fetch fact (1) must stay green with **no change to any assertion**. Only the
  now-stale comment at lines 263-270 may change (comment-only; verify by the strip-comments hash).
- `tests/Architecture.Tests/ResilienceRegistrationTests.cs` — unmodified, green (FR-004).
- `tests/ServiceDefaults.Tests/ResilienceHandlerNestingTests.cs` — unmodified, green.
- `tests/Integration.Tests/Fixtures/FixtureRetryPolicyTests.cs` — unmodified assertions, green.
- `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxProbeLogVolumeTests.cs` — unmodified,
  green (its retry facts use `GET`).

## 5. Rejected alternatives

- **R1 `RemoveAllResilienceHandlers()` + `AddStandardResilienceHandler()` inside `RetryEveryMethod`.**
  The library's own idiom for overriding a default per client, and present in 10.10.0. Rejected: it
  is a second `AddStandardResilienceHandler(` call in `src`, which
  `ResilienceRegistrationTests.The_standard_resilience_handler_is_called_exactly_once_across_src`
  forbids and whose message says "configure the existing pipeline's options — do not add another
  handler". Changing that guard is a gate change the lane may not make (ADR-0144). It would also
  give the opted-in client a pipeline of its own, which is more than ADR-0143's "only the retry
  predicate narrows".
- **R2 Reach `{client}-standard` from the defaults pipeline.** Not available: the defaults builder
  is unnamed, the pipeline reads `-standard`, and the standard handler's configure callback receives
  only the options, not the client name.
- **R3 Amend ADR-0143 to drop the opt-in.** A decision reversal against reasons the ADR states and
  that still hold; outside the lane, and not needed.
- **R4 A request header as the marker.** Goes on the wire to Keycloak and MediaMTX. The marker is
  `HttpRequestMessage.Options`, which never leaves the process.

## 6. Judgement calls (marked, not clarifications)

- **J1 Mechanism.** The opt-in becomes a per-client `DelegatingHandler` that sets an
  `HttpRequestOptionsKey<bool>` on the request; the shared predicate retries a marked request on the
  transient predicate regardless of method. Probed on 10.10.0 before this spec was written: under
  `ConfigureHttpClientDefaults`, marked `POST` -> 4 on both the 503 and the thrown path; unmarked ->
  1. It works because the defaults' resilience handler is outermost and re-sends the **same**
  `HttpRequestMessage` instance on retry; the marker is set on attempt 1 before the predicate first
  runs. Hedging would clone the request; nothing here uses hedging.
- **J2 Marker placement.** The marker sets the option on entry, before calling `base`, so a later
  per-client handler that throws still leaves the request marked. All seven call sites call
  `.RetryEveryMethod()` first anyway.
- **J3 Marker visibility.** `internal sealed`. `ServiceDefaults.Tests` sees internals
  (`SmartSentinelEye.ServiceDefaults.csproj:25`); `Architecture.Tests` matches the type by name, the
  precedent `ResilienceHandlerNestingTests` sets. A rename fails those tests loudly (marker not
  found), not silently.

## 7. Latency budget

**N/A.** No leg of the event-to-overlay path (constitution §IV) is touched: token mints run at startup
and token refresh; MediaMTX `add/`/`patch/` run during stream provisioning, not per event. An
opted-in call now spends up to the existing 30 s total-request budget on a failing upstream instead of
failing at the first attempt — the behaviour ADR-0143 already describes.

## 8. Out of scope (found, reported, not fixed here)

- The zero-delay overrides keyed `{client}-standard` in `MediaMtxProbeLogVolumeTests.BuildGatewayHost`
  (line 273) are inert for the same reason: that host calls `AddServiceDefaults()`, so its retry facts
  spend the real 2/4/8 s backoff. Slower, not wrong.
- The single `-standard` pipeline shares one circuit breaker across every client in a process under
  `ConfigureHttpClientDefaults`. **Verified** (reproduced independently by two reviewers and by the
  `Hazard_one_failing_client_opens_the_circuit_breaker_for_a_healthy_sibling` fact): one failing
  upstream opens the breaker for calls to every other upstream. This fix raises the failures fed into
  it from 1 to up to 4 per opted-in call. Making the breaker per client needs an ADR and is out of
  scope; the Hazard fact is a hazard record, and its inversion is the expected outcome of that
  follow-up. It bears on ADR-0143's "its share of the circuit breaker".
- CLAUDE.md's stack table says "five clients opt back in"; there are seven call sites (§1).
