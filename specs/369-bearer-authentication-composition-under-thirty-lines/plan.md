# Plan 369: The bearer authentication composition under thirty lines

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2952.

## 1. Constitution and ADR alignment

| Rule | How this plan holds it |
|---|---|
| ADR-0084 | Clears 2 of the 7 S138 hits in `src/ServiceDefaults`, by shape only; S107 stays at 4; every new helper <= 4 parameters and <= 30 lines. |
| ADR-0144 | Phase 4a characterisation, observed green; no gate weakened (no suppression added, the existing `S1075, S5332` pragma pair moves verbatim); no ADR, no constitution edit. |
| ADR-0160 | The revocation hook, registry, source, refresher and health check keep their wiring, their `TryAdd` precedence and their order. Pinned by spec §4.2-§4.4. |
| ADR-0143 | `revocation-list-token` keeps `RetryEveryMethod()` and its stated reason; `revocation-list-fetch` gains none. Pinned by attempt counts (spec §4.4). |
| ADR-0154 | `revocation-snapshot`: name, `Degraded`, `["ready"]` unchanged. |
| ADR-0113 | `ConcurrencyConflictExceptionHandler` before `UniqueConstraintExceptionHandler`, with its comment. |
| ADR-0050 | `TokenRefusedAsRevoked` call untouched; the logger category literal unchanged. |
| §III | ServiceDefaults only; no project reference added. |
| House rules | `var`; chained registrations where calls already chain, but no reflow of existing statements into chains (shape only, smallest change); one parameter per line when a signature wraps; no drive-by comments; comments move with their code. |

## 2. Shape

`src/ServiceDefaults` only (cross-cutting, every service). Not a bounded context: no Domain or
Application layer, no entity or value object, no messaging, no migration, no AppHost, no csproj
change. One production file: `src/ServiceDefaults/AuthenticationDefaults.cs`.

The invariants that bind are existing ones:
- **Fail closed at composition**: no Keycloak address, no registrations (the throw precedes the
  first `builder.Services` call).
- **The hook chains**: every `Configure` of the `Bearer` named options runs in registration order on
  one instance, so the DI-aware revocation `Configure` must stay registered after `AddJwtBearer`'s
  and before `AddMetadataTransportCheck`'s `Validate` (which is where today's sequence puts it), and
  must keep reading `options.Events.OnTokenValidated` at configure time.
- **A test double or Identity's `Replace` wins**: every `TryAdd` stays a `TryAdd`.
- **Order of `IExceptionHandler`s is precedence** at runtime (first handler that returns true wins).

## 3. Refactor shape per method

Registration order is the whole contract, so each extraction is a **contiguous run of statements**
moved verbatim into a private method called from the exact spot it left. Nothing is reordered.

### G1: `AddBearerAuthentication` (46 → about 15)

| Extracted (private static, same class) | Statements moved, verbatim with their comments |
|---|---|
| `string KeycloakBaseUrl(IConfiguration configuration, string keycloakResourceName)` | The "Aspire publishes Keycloak under one of three keys" comment and the `?? … ?? throw` chain, returned. |
| `void AddJwtBearerScheme(IHostApplicationBuilder builder, string authority)` | `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(…)` with all four option assignments and their comments. Needs `builder` for `builder.Environment`. |
| `void AddRevocationHook(IServiceCollection services)` | The spec 270 comment block and the `AddOptions<JwtBearerOptions>(…).Configure<IRevokedClientRegistry, IServiceProvider>(…)` statement, including the logger category literal. |
| `void AddExceptionHandlers(IServiceCollection services)` | The five `AddExceptionHandler<…>()` lines with both comments ("First: a request the caller got wrong…", "After the concurrency handler…"). |

What stays in `AddBearerAuthentication`, in this order: the two `Ensure` guards;
`var keycloakBaseUrl = KeycloakBaseUrl(builder.Configuration, keycloakResourceName);`; the
`authority` line; `AddJwtBearerScheme`; `AddRevocationHook`; `AddMetadataTransportCheck`;
`AddRevocationCheck`; `AddSingleton<IFabAuthorizationGuard, DefaultFabAuthorizationGuard>()`;
`AddExceptionHandlers`; `AddProblemDetails()`; `AddAuthorizationBuilder().AddScopePolicies(Scope.All)`;
`return builder;`.

### G2: `AddRevocationCheck` (32 → about 14)

| Extracted | Statements moved |
|---|---|
| `void AddRevocationListOptions(IHostApplicationBuilder builder, string keycloakBaseUrl, string realm)` | The "Validated only against whichever IRevokedClientSource…" comment and the `AddOptions<RevocationListOptions>()…ValidateOnStart()` statement. |
| `void AddRevocationListClients(IServiceCollection services)` | From the ADR-0143 comment through `TryAddSingleton<IRevokedClientSource, HttpRevokedClientSource>()`: the token client with `RetryEveryMethod()`, `TryAddSingleton<RevocationListTokenProvider>`, `TryAddTransient<RevocationListAuthorizationHandler>`, the S3 comment, the fetch client with its S1075/S5332 comment and pragma pair, and the source `TryAdd`. |

What stays in `AddRevocationCheck`, in this order: the `TimeProvider` comment + `TryAddSingleton`,
the two registry `TryAdd`s, `AddRevocationListOptions(builder, keycloakBaseUrl, realm)`,
`AddRevocationListClients(builder.Services)`, the refresher `TryAddSingleton` + `AddHostedService`,
the health check.

Method names are the engineer's to adjust; the boundaries are not. Rules for the engineer:
- Keep `TryAdd*` vs `Add*` exactly as written; keep `AddHostedService(sp => sp.GetRequiredService<…>())`.
- Keep the logger category as the **string literal** it is today (not `typeof(…).FullName`, not a
  `nameof`), and keep `services.GetService<ILoggerFactory>()?` optional.
- Keep `keycloakBaseUrl` untrimmed into `RevocationListOptions` (spec J4).
- Do not touch `AddMetadataTransportCheck`, `RequiresHttpsMetadata`, `RefuseIfRevoked`,
  `IssuedAtOf`, the constants, or the class doc comment.
- No new `using` beyond what the new signatures require (`IServiceCollection`, `IConfiguration` are
  already imported).

## 4. Test design (`tests/ServiceDefaults.Tests`, new files only)

| ID | File | Spec | Notes |
|---|---|---|---|
| C1 | `KeycloakAddressCharacterisationTests.cs` | §4.1 | Empty builder per case. The throw case asserts `Message` with a hand-written literal and `builder.Services.Count` equal before and after. |
| C2 | `BearerOptionsCharacterisationTests.cs` | §4.2 | `IOptionsMonitor<JwtBearerOptions>.Get("Bearer")`, `IOptions<AuthenticationOptions>`, `IAuthenticationSchemeProvider`. Hook driven with a `TokenValidatedContext` built as `BearerRevocationHookTests` builds it (copied privately, not shared); logger captured through a registered `ILoggerFactory` whose provider records category, level and rendered message. |
| C3 | `BearerAuthenticationRegistrationCharacterisationTests.cs` | §4.3 | Slice = `builder.Services.Skip(countBefore)`. Render copied from spec 348's file (private, not shared across projects), **made keyed-safe**: a keyed descriptor's `ImplementationType` throws, so check `IsKeyedService` and read `KeyedImplementationType` / `KeyedImplementationInstance` / factory. Two facts: the ordered render, and the five exception handlers in order. |
| C4 | `Revocation/RevocationCompositionCharacterisationTests.cs` | §4.4 | Resilience defaults registered before the call as `Extensions.AddServiceDefaults` does (`ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(…RetryIdempotentMethodsOnly…))`, delay zero, no jitter); primary handlers replaced after the call with `AddHttpClient("<name>").ConfigurePrimaryHttpMessageHandler(…)`. The token stub answers `{"access_token":"…","expires_in":300}` (the shape `ClientCredentialsTokenProvider` reads). Health check read from `IOptions<HealthCheckServiceOptions>`. |

All expected values are hand-written literals copied once from the observed green output on
`128df804`. Helpers are private nested types; existing test files are not edited.

## 5. Proof strategy

1. **Observed green first**: C1-C4 pass on `128df804` (production untouched) in Release. Run C3
   twice (J2: the render is stable). Quote the output.
2. **Counterfactuals** (scratch edits to production, reverted, never committed; quote the red):
   - C1: swap the `http:0` and `https:0` lookups → precedence case red; move the URL resolution below
     `AddAuthentication` → the "nothing registered" case red.
   - C2: `MapInboundClaims = true` → red; change the logger category literal → red.
   - C3: swap `ConcurrencyConflictExceptionHandler` and `UniqueConstraintExceptionHandler` → both C3
     facts red; move `AddRevocationCheck(...)` above the revocation `Configure` → snapshot red.
   - C4: `TryAddSingleton<IRevokedClientSource, …>` → `AddSingleton` → precedence red; delete
     `.RetryEveryMethod()` → attempt count 1, red; `failureStatus: HealthStatus.Unhealthy` → red;
     `options.KeycloakUrl = keycloakBaseUrl.TrimEnd('/')` → red.
3. **Refactor**, then re-run byte-unmodified (`git diff <green-sha> -- tests/` empty) after each commit.
4. **Build**: `dotnet build src/ServiceDefaults -c Release --no-incremental` → rows 1-2 gone, no new
   ADR-0084 warning; `git grep -c "pragma warning disable" src/ServiceDefaults/AuthenticationDefaults.cs`
   unchanged (1).
5. **Second observer**: spec §4.5's existing tests and the CI integration shards (real Keycloak
   tokens through nine services) green on the PR.
6. Phase 6: `/code-review` + `/security-review`. Reviewer checks every removed comment line reappears
   verbatim (`git diff --word-diff`).

## 6. Commit groups

| Group | Commits |
|---|---|
| G0 | `docs(spec): spec 369 bearer authentication composition under thirty lines` (this directory) |
| Tests | `test(service-defaults): pin the bearer composition's address, options and registrations` (C1+C2+C3) → `test(service-defaults): pin the revocation composition's precedence, clients and health check` (C4) |
| G1 | `refactor(service-defaults): split the bearer authentication composition under thirty lines` |
| G2 | `refactor(service-defaults): split the revocation check composition under thirty lines` |

Each commit builds and passes `tests/ServiceDefaults.Tests` on its own (rebase-merge lands them
individually, ADR-0087).

## 7. Risks

| Risk | Mitigation |
|---|---|
| A reordered registration fails open silently (hook not chained, double not winning) | Extractions are contiguous runs called from the same spot (§3); C3 snapshot + C4 precedence + counterfactuals. |
| The C3 render touches a keyed descriptor and throws | Keyed-safe render (§4 C3). |
| Framework descriptors make C3 flaky across package bumps | Slice-only render (J2); two runs on one SHA must agree; a future bump re-captures in its own commit with the reason. |
| The token mint in C4 needs more than a stubbed response (time, cache) | It is the existing `ClientCredentialsTokenProvider` path; if the test cannot mint, fall back to asserting the `Authorization` header is present with scheme `Bearer` and record why in the PR. Do not edit production to make it testable. |
| A comment or the pragma pair dropped while code moves | FR-007; pragma count check (§5.4); word-diff review. |
| Spec 368 (PR #2956) merges first | Disjoint files; only SC-001's S138 total changes (5 → 1). Rebase after it merges. |
