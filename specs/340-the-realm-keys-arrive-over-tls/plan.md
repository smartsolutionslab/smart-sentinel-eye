# Plan 340: The realm keys arrive over TLS

**Spec**: [spec.md](spec.md) · **Issue**: #2238 · **Branch**: `fix/2238-env-conditional-require-https`

## 1. Constitution and ADR check

| Gate | Holds | Note |
|---|---|---|
| §III No cross-context references | Yes | `StreamDistribution.Infrastructure` → `ServiceDefaults` already exists (`WhepAuthValidator` reads `AuthenticationDefaults.ApiAudience`). No new reference is added. |
| §II Primitives off domain models | N/A | No domain model is touched. These are options classes and composition code in Infrastructure and ServiceDefaults. |
| §IV Latency | N/A | Startup-only check plus metadata transport. Not on any leg. |
| §VI Aspire is the composition root | Yes | No AppHost change. The existing environment flows from the launch profiles. |
| §VIII Safe by default | **Improves** | Outside Development, plaintext realm metadata stops being possible. |
| ADR-0105 guards | Yes | `Ensure.That` on any new public or internal method argument. |
| ADR-0141 `Option<T>` | N/A | No absence is being modelled. |
| ADR-0144 the lane does not decide | Yes | The policy is the owner's. Assumption A1 (spec §3.2) was confirmed by the coordinator on 2026-10-12, not decided silently. |
| Code metrics (ADR-0084, advisory) | Watch | `AddBearerAuthentication` is already long. Put the new validation in its own private method, as `AddRevocationCheck` does. |

**Engineer**: backend only. No AppHost, CI, realm or Helm file is touched, so no infra engineer is
needed.

## 2. Where the change lands

There is no bounded context and no domain layer here. These are two composition and
infrastructure seams.

| File | Layer | Change |
|---|---|---|
| `src/ServiceDefaults/AuthenticationDefaults.cs` | ServiceDefaults | The policy method (FR-001). The flag comes from the policy (FR-002). Startup validation of the named `JwtBearerOptions` (FR-003). Rewrite the comments at `:54-66` and `:195-206` (FR-008). |
| `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` | Infrastructure | The DI constructor takes `IHostEnvironment`. `MetadataSourceFor(options, environment)` sets `RequireHttps` from the policy (FR-004). Rewrite the comment at `:79-86` (FR-008). |
| `src/StreamDistribution/Infrastructure/StreamDistributionInfrastructureModule.cs` | Infrastructure | `BindWhepAuthOptions`: `Configure<WhepAuthOptions>` becomes `AddOptions<WhepAuthOptions>().Configure(…).Validate(…).ValidateOnStart()`, with the same shape as `BindMediaMtxApiOptions` directly above it (FR-005). The method becomes `internal` so the test can compose it without the whole module (§4). |
| `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepMetadataSourceTests.cs` | Test | Rewrite the doc comment (FR-008). Existing call sites move to the new `MetadataSourceFor` signature. This is a compile-only adaptation; no assertion changes. |

`WhepAuthOptions` keeps its single `Authority` property. **No `RequireHttps` property is added**:
a settable flag on an options class would be the configuration knob spec §3.4 rules out.

## 3. Mechanism detail

### 3.1 The policy (one definition)

```text
AuthenticationDefaults.RequiresHttpsMetadata(IHostEnvironment environment) : bool
    Ensure.That(environment).IsNotNull()
    => !environment.IsDevelopment()
```

It is public, because `StreamDistribution.Infrastructure` consumes it across an assembly boundary.
That is the same visibility `ApiAudience` and `KioskClientId` have. `IsDevelopment()` compares
case-insensitively (`HostEnvironmentEnvExtensions`), so `development` counts too.

### 3.2 Bearer (all nine APIs)

1. In `AddJwtBearer`'s configure lambda:
   `options.RequireHttpsMetadata = RequiresHttpsMetadata(builder.Environment);`
2. A new private `AddMetadataTransportCheck(builder)`:
   `AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)`
   `.Validate(options => options.RequireHttpsMetadata || !RequiresHttpsMetadata(builder.Environment), <FR-006 message>)`
   `.ValidateOnStart()`.
   - **The order of operations is load-bearing. Verify it against the pinned
     `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12, don't assume it.** Options resolution
     runs Configure, then PostConfigure, then Validate. `JwtBearerPostConfigureOptions` throws
     `InvalidOperationException` for "RequireHttpsMetadata and an http MetadataAddress/Authority"
     **before** `Validate` runs. So for the http-authority case the startup failure is the
     framework's exception, surfaced by `ValidateOnStart`. The explicit `Validate` covers only the
     "flag flipped back by a later Configure" case (R4). R3 should therefore assert on "startup
     throws, and the message mentions HTTPS", not on `OptionsValidationException` specifically.
     Phase 4 records which exception type was actually observed.
   - The predicate captures `builder.Environment` at registration time. This is the same capture
     style `AddRevocationCheck` uses for `builder.Configuration`.
3. `LayoutComposition`'s later `Configure<JwtBearerOptions>` replaces only `OnMessageReceived`
   (`AuthenticationDefaults.cs:90`), so it does not interact with this check.

### 3.3 WHEP

1. `WhepAuthValidator(IOptions<WhepAuthOptions>, IHostEnvironment, IRevokedClientRegistry, ILogger)`
   calls `MetadataSourceFor(options, environment)`. The `internal` test constructor (taking
   `IConfigurationManager`) is unchanged.
2. `MetadataSourceFor` sets
   `new HttpDocumentRetriever { RequireHttps = AuthenticationDefaults.RequiresHttpsMetadata(environment) }`.
3. `BindWhepAuthOptions`:
   `.Validate(options => !RequiresHttpsMetadata(builder.Environment) || IsHttpsAbsolute(options.Authority), <FR-006 message>)`
   `.ValidateOnStart()`, where `IsHttpsAbsolute` is
   `Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps`.
   Keep it a private static local helper, not a new shared utility (no speculative generality).

### 3.4 Messaging

None. There is no domain event and no integration event. This is startup configuration.

### 3.5 Boundary rules

No new project reference. `NetArchTest` boundary rules are unaffected.

## 4. How the tests reach the code

These use existing patterns only.

- **Startup validation** is exercised through `IStartupValidator`, which `ValidateOnStart`
  registers (`Microsoft.Extensions.Options` ≥ 8). Call `provider.GetRequiredService<IStartupValidator>().Validate()`.
  This is exactly what `Host.StartAsync` calls before any hosted service starts, so the test proves
  "refuses to start" without starting `RevokedClientRefresher` or touching the network.
- **The environment** is set with
  `Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = … })`.
  This mirrors `DefaultEndpointsTests`' `EnvironmentName` option and its "assert you got the
  environment you asked for" self-check, which is worth copying, because an ambient
  `DOTNET_ENVIRONMENT` must not decide the test.
- **The bearer red tests (R3)** must set `RevocationList:ClientId` and `RevocationList:ClientSecret`.
  Without them, the existing revocation `ValidateOnStart` would fail first, and the test would be
  red for the wrong reason. Assert on the message (HTTPS), not only on "throws".
- **The WHEP retriever (R5, G2)**: use `http://127.0.0.1:1/realms/smart-sentinel-eye`. In
  Production the failure must carry IDX20108 in the inner chain (an `ArgumentException` from
  `HttpDocumentRetriever`, wrapped by `ConfigurationManager` in `InvalidOperationException`
  IDX20803). In Development the failure must not carry it. **Check the wrapping against the pinned
  `Microsoft.IdentityModel.Protocols` 8.19.2.** The validator's own comment at `:135` cites that
  version.
- **The WHEP options binding (R6, R7, G3)**: call the now-`internal`
  `StreamDistributionInfrastructureModule.BindWhepAuthOptions(builder)` directly. `InternalsVisibleTo`
  already covers this test project (the `.csproj:8` attribute, and `MetadataSourceFor` is tested
  the same way).
- **Test locations**: `tests/ServiceDefaults.Tests/MetadataTransportTests.cs` (new),
  `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepMetadataTransportTests.cs` (new). These
  are unit-test projects, so **no `ci-shards/*.filter` entry is needed**. Those filters cover
  `Integration.Tests` only.

## 5. Regression sweep: placeholder authorities

Many in-process compositions use `http://` placeholder Keycloak URLs on an implicitly-Production
host (`Host.CreateEmptyApplicationBuilder(null)`). Examples are `PostgresComposition.ConfigurationFor`,
which `ServedHealthHosts` reuses for `ComposedHost`, and the sweep-registration tests. **Most do not
trip.** `ValidateOnStart` fires only on `IStartupValidator.Validate()` or `Host.StartAsync`, and
`ComposedHost` aborts at `Build()`. Only a composition that **both** includes `AddBearerAuthentication`
or `AddStreamDistributionInfrastructure` **and** starts the host or resolves the options would trip.

If one does, the fix is to switch that fixture's placeholder to `https://…`. It is never dialled,
the strict environment is what ships, and the fixture should compose what ships. Do **not** set the
fixture to `Development`, and do not narrow the validator. Each such edit is listed in the PR body
with its reason. All existing tests that already compose `AddBearerAuthentication` use
`https://keycloak.invalid` (checked: `BearerAudienceTests`, `BearerRevocationHookTests`,
`RevocationListOptionsValidationTests`, `RevokedClientRefresherWiringTests`, `RegisteredPolicyTests`,
`WhepAudienceTests`, `RealmImportMirrorTests.ProductionDefaults`), so they are expected to stay
green untouched.

## 6. Risks and escalation triggers

| Risk | Handling |
|---|---|
| Aspire stops applying launch profiles, or a CI job runs services without one | Every service refuses to start on the http dev authority. That is loud, not silent (spec §3.1 counterfactual). |
| An on-prem fab wants a non-TLS realm outside Development | **Not pre-built.** This is a new decision requiring an ADR. The lane must not add a configuration override to satisfy it. |
| Framework order differs from §3.2 in 10.0.12 | The R3 test observes the actual behaviour. Adjust the assertion's exception type and record it. The requirement ("refuses to start, message names HTTPS") stands. |
| The realm still has `sslRequired: none` | Out of scope (spec §7). Tracked as #2878. |

## 7. Verification (phase 5)

1. **Strict, real host.** Run `src/StreamDistribution/Api` (and one other API, for example
   `CameraCatalog`) with `--no-launch-profile`, `ASPNETCORE_ENVIRONMENT=Production`,
   `ConnectionStrings__keycloak=http://localhost:8080`, and placeholder values for the other
   required keys. Observe the process exiting non-zero with the FR-006 message before it listens.
   Quote the output. Repeat with `https://keycloak.invalid` and observe that it gets past the
   validation (it may fail later on unrelated dependencies; say so).
2. **Permissive, the real stack.** Boot the AppHost (one stack per machine). Make an authenticated
   API call and a WHEP authorize through the gateway, and observe both succeed. Confirm the
   `stream-distribution` resource's environment reads `Development` in the dashboard.
3. Latency: N/A, cite spec header.
