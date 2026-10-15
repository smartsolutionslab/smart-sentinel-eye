# Plan 375: Each upstream authority gets its own circuit breaker

**Spec**: [spec.md](./spec.md). **Issue**: #2959. **ADRs**: 0143, 0160, 0088, 0144.

## 1. Where it lives

Cross-cutting infrastructure in `src/ServiceDefaults` — no bounded context, no Domain, Application or
Contracts change, no messaging, no persistence, no AppHost resource. Comment-only edits in three
contexts' `Infrastructure` modules (FR-008). No cross-context reference is added; `ServiceDefaults` is
already referenced by every host.

Constitution check: §II (no domain model touched), §IV (no leg touched — spec §7), §Testing (red;
spec §4), boundaries (NetArchTest unaffected). ADR-0084: `AddServiceDefaults` grows by one chained call
and a comment; stays under 30 lines (S138).

## 2. The change (one production line)

`src/ServiceDefaults/Extensions.cs`, inside the existing `ConfigureHttpClientDefaults` callback:

```
http.AddStandardResilienceHandler(IdempotentRetry.RetryIdempotentMethodsOnly)
    .SelectPipelineByAuthority();
```

Plus the comment it needs (why: one breaker per upstream so one outage stays one outage — ADR-0143
"its share", ADR-0160 §3 fail-static; same authority shares intentionally; the key is the pre-discovery
authority; keys never evict, so no client with caller-supplied authorities under these defaults —
FR-007).

Nothing else in production changes behaviour. `IdempotentRetry` is untouched in code; its class doc
sentence "its share of the breaker" gains "— one breaker per upstream authority" (FR-009).

### Why this works with the #2958 marker (verified, spec §1.3 E4-E4c)

Handler order per client is unchanged: defaults' `ResilienceHandler` (outermost) → per-client handlers
(`RetryEveryMethodMarker`, authorization handlers) → service discovery → primary. The selector runs in
`ResilienceHandler.SendAsync` on `request.RequestUri` **before** any attempt; the marker sets
`request.Options` on attempt 1; the shared predicate reads it after the attempt. Selection decides
*which* breaker/limiter instance; the predicate is identical in every instance because every instance
is built from the same `-standard` options.

## 3. Invariants (what the tests pin)

- **I1** distinct `(scheme, host, port)` ⇒ distinct breaker (FR-002) — spec §4.1, §4.2 rows 1-3.
- **I2** same `(scheme, host, port)` ⇒ one breaker, across clients (FR-003) — guard.
- **I3** the breaker still opens for the failing authority (FR-004) — guard.
- **I4** opt-in semantics unchanged and per request, not per pipeline (FR-006) — guard + the whole
  spec-370 suite unmodified.
- **I5** exactly one `AddStandardResilienceHandler(` in `src`, inside `ConfigureHttpClientDefaults`
  (FR-001) — `ResilienceRegistrationTests` unmodified.

## 4. Tests

Spec §4. The test-writer owns `RetryEveryMethodAdversarialTests.cs` (the declared inversion only) and
the new `PerAuthorityCircuitBreakerTests.cs`. The new file copies the small `Compose` /
`CountingServer` shape rather than extracting a shared helper (a refactor of an existing test file is
out of scope for a red pair). For the logical-name fact, configure discovery with
`builder.Configuration["services:identity:http:0"]` and `["services:camera-catalog:http:0"]` pointing at
one `http://localhost:5555`, each client with its own `ConfigurePrimaryHttpMessageHandler` — the probe
(spec E5) showed the key is taken before resolution.

No new test class needs a CI shard entry: shard filters exist for `Integration.Tests` only
(`ServiceDefaults.Tests` runs whole).

## 5. Comments at the nesting call sites (FR-008)

At each `.AddHttpMessageHandler<…AuthorizationHandler>()` registration listed in spec §5, one comment:
the handler mints through an opted-in client, so in a Keycloak outage the mint's retries nest inside
this client's attempts; accepted because the mint is single-flight, the outer attempt timeout cancels
it, and 400/401 are not retried; the mint feeds the Keycloak breaker (Identity: the same breaker as the
call, because it is the same Keycloak). Comment-only, proved by the strip-comments hash.

## 6. Security review focus (Phase 6, `security-reviewer`)

- Revocation refresh (ADR-0160 §3): an unrelated upstream can no longer freeze the snapshot; a
  Keycloak outage still can (mint fails), and an `identity` outage still does — both unchanged and
  inherent to fail-static.
- J1: authority instance name includes userinfo; none in `src` today.
- FR-007: unbounded key growth only for caller-supplied authorities; none exist.
- J3: per-authority concurrency limiter.

## 7. Rejected alternatives

- **Per-client pipelines via naming** (`RemoveAllResilienceHandlers()` + a named
  `AddStandardResilienceHandler` per client): a second `AddStandardResilienceHandler(` in `src`, which
  `ResilienceRegistrationTests` forbids (a gate the lane may not change); and per client is the wrong
  axis — two clients to one Keycloak should share what they learn about it (owner decision).
- **Document the shared breaker and amend ADR-0143**: owner chose otherwise; the lane writes no ADR.
- **Custom `SelectPipelineBy`**: held in reserve for J1 only.
- **Hand-built pipeline**: excluded by the issue.
