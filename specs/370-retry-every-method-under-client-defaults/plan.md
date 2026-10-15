# Plan 370: `RetryEveryMethod()` reaches the pipeline `ConfigureHttpClientDefaults` builds

**Spec**: [spec.md](./spec.md). **Tasks**: [tasks.md](./tasks.md). **Issue**: #2958.

## 1. Constitution and ADR check

| Gate | Holds | Why |
|---|---|---|
| ADR-0143 | yes | Implements its §Decision as written; no amendment (spec preamble). |
| ADR-0088 / FR-004 | yes | One `AddStandardResilienceHandler(` in `src`, one pipeline per client; only the retry predicate changes. |
| ADR-0144 | yes | No ADR, no constitution change, no gate weakened: `ResilienceRegistrationTests` unmodified; the three rewritten characterisation facts make the same claim through the mechanism that is read, and are red first (spec §4.3). |
| §II (no primitives on a domain model) | n/a | ServiceDefaults infrastructure; no domain model. |
| No cross-context references | yes | All production edits are in `src/ServiceDefaults`; contexts already reference it. |
| §IV latency | n/a | Spec §7. |
| ADR-0084 | yes | Each touched method stays <= 30 lines; the predicate grows by one condition, extracted to a private helper if S138 would trip. |
| ADR-0105 | yes | `Ensure.That(builder).IsNotNull()` kept. |

## 2. Where it lives

Bounded context: none — **`src/ServiceDefaults`** (shared host defaults). Layer: infrastructure
composition. One production file:

- `src/ServiceDefaults/Resilience/IdempotentRetry.cs`

No change to `Extensions.cs`, to any of the seven call sites, to `Shared.Contracts`, `AppHost`, or
any migration.

## 3. Design

```text
ConfigureHttpClientDefaults  ->  [standard resilience handler, options "-standard",
                                  predicate = RetryIdempotentMethodsOnly]           (outermost, shared)
.RetryEveryMethod()          ->  [RetryEveryMethodMarker : DelegatingHandler]        (per client, inner)
primary handler
```

1. `IdempotentRetry` gains a private `static readonly HttpRequestOptionsKey<bool>` (name it after
   what it means, e.g. `RetryEveryMethodRequested`; ADR-0091 no shortcuts).
2. A nested or file-level `internal sealed class RetryEveryMethodMarker : DelegatingHandler` that
   sets the option to `true` on entry and delegates — overriding **both** `SendAsync` and `Send`
   (sync `HttpClient.Send` is a supported path; the spec's probe used it).
3. `RetryEveryMethod(this IHttpClientBuilder builder)`: guard, then
   `builder.AddHttpMessageHandler(() => new RetryEveryMethodMarker())`, return the builder. The
   `Configure<HttpStandardResilienceOptions>($"{builder.Name}-standard", ...)` is **removed** (FR-006).
   The `Func<DelegatingHandler>` overload registers nothing in DI, so no new service descriptor appears
   in any context's ordered registration characterisation.
4. `RetryIdempotentMethodsOnly`'s predicate: request resolved as today; `null` -> `false` (FR-003);
   otherwise retry iff `(IsIdempotent(request.Method) || IsMarked(request)) && IsTransient(outcome)`.
   Keep "Failing closed" comment; the doc comments on both public methods are rewritten to say where
   the opt-in is read (the shared pipeline's predicate), and **why** it is a request option rather than
   named options (the defaults pipeline reads `-standard` for every client — one sentence, no issue
   number in code).

Invariants:

- **I1** An unmarked request's retry decision is byte-for-byte today's (FR-002).
- **I2** A marked request is retried exactly when the library's transient predicate says so — never
  on a non-transient status (spec US1 "bad request").
- **I3** The marker is per client: it exists only in the handler chain of a client that called
  `RetryEveryMethod()`; the shared pipeline carries no per-client state (spec US1 "conflict").

Why it works (probed, spec §6 J1): the defaults' handler is outermost and re-sends the same
`HttpRequestMessage` on each retry, so the option set during attempt 1 is visible to the predicate
after attempt 1, on both the response path (`Outcome.Result.RequestMessage`) and the exception path
(`Context.GetRequestMessage()`).

## 4. Messaging

None. No domain event, no integration event, no contract.

## 5. Test files and what each owns

| File | Kind | Change |
|---|---|---|
| `tests/ServiceDefaults.Tests/Resilience/IdempotentRetryTests.cs` | unit (attempt count) | `Build` composes via `ConfigureHttpClientDefaults`; `Build` gains the ability to register a second plain client in the same container and to answer 400; 4 new facts (spec §4.1). Existing 7 fact bodies unmodified. |
| `tests/ServiceDefaults.Tests/Resilience/RetryEveryMethodCompositionTests.cs` | unit, real `AddServiceDefaults()` | new (spec §4.2). |
| `tests/Architecture.Tests/Persistence/EventIngestionRegistrationCharacterisationTests.cs` | characterisation | rewrite 1 fact to a handler-chain walk (spec §4.3). |
| `tests/Architecture.Tests/Persistence/StreamDistributionRegistrationCharacterisationTests.cs` | characterisation | rewrite 2 facts + the class doc's mention of "named `HttpStandardResilienceOptions` configurations"; drop `NamedStandardResilienceConfigurations` if unused. |
| `tests/ServiceDefaults.Tests/Revocation/RevocationCompositionCharacterisationTests.cs` | characterisation | comment at 263-270 only. |
| `tests/Integration.Tests/Fixtures/FixtureRetryPolicyTests.cs` | — | comment at 107-111 ("the ordering IdempotentRetry.RetryEveryMethod already depends on" is no longer true) only. |

**Shard filters.** Not needed: only `tests/Integration.Tests/ci-shards/shard-*.filter` exist, and
no file here adds an Integration.Tests class (checked 2026-10-15).

The handler-chain walk in Architecture.Tests needs a composed provider; both files already build one
through `PostgresComposition.Compose(...)`. Match the marker by `GetType().Name ==
"RetryEveryMethodMarker"` (spec §6 J3). If both files need the same walk, a small helper under
`tests/Architecture.Tests/Persistence/` is acceptable — no new shared test utility project.

## 6. Risks

- **A fix that widens the shared predicate** (e.g. "retry POST if any client opted in") would pass
  the opt-in facts and duplicate effects for every other client. Guarded by the sibling fact (spec
  §4.1) and called out for `/security-review`.
- **Hedging later added to the defaults** would clone requests and could drop the marker. Not in
  scope; the doc comment states the same-instance dependency.
- **Behaviour now differs in tests that relied on inertness.** Searched (spec §4.4): no test asserts
  1 attempt for an opted-in client under `AddServiceDefaults`. If CI finds one, it is reported, not
  edited to pass.

## 7. Verification (phase 5)

Unit-level is decisive for this change (the pipeline is the library's, composed as production does).
Phase 5 additionally records: `dotnet test tests/ServiceDefaults.Tests` and `tests/Architecture.Tests`
summaries; and the counterfactual (remove `.RetryEveryMethod()` from `AddMediaMtxGateway` -> the
gateway characterisation fact fails; restore). A live-stack observation is optional: provoking a
Keycloak 503 on a token mint is not cheap, and the composition test (spec §4.2) already exercises
`AddServiceDefaults()` itself.
