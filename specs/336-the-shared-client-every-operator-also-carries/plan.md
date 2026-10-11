# Plan — Spec 336 (#2814)

Phase 2 of ADR-0037.

## 1. Shape

One bounded context: EventIngestion. No `Shared.Contracts` change, no
AppHost change, no frontend change.

| Layer | Change |
|---|---|
| Domain | `IWebhookIntegrationRepository.IsRevokedByKeycloakClientIdAsync` (new method on the existing interface) |
| Infrastructure | `WebhookIntegrationRepository.IsRevokedByKeycloakClientIdAsync` implementation |
| Api | `EventsEndpoints.Writes.IngestManual` gains a revocation check; `IngestManualServices` gains the repository dependency; new private helper `IsRevokedWebhookCallerAsync` |
| Tests | New integration test class; a cleanup addition to an existing one (§3) |

## 2. The check

Placed immediately after the idempotency-key read, before fab resolution —
same ordering `AuthenticateWebhookAsync` uses (credential/revocation before
fab), so a revoked caller is refused before learning anything about fab
membership.

```csharp
string? azp = user.FindFirst("azp")?.Value;
if (string.IsNullOrWhiteSpace(azp) || azp.Length > KeycloakClientIdentifier.MaximumLength)
{
    return false; // no azp, or not shaped like one — not a webhook-sourced caller
}

return await webhookIntegrations
    .IsRevokedByKeycloakClientIdAsync(KeycloakClientIdentifier.From(azp), cancellationToken);
```

`azp` is read directly off the already-validated `ClaimsPrincipal`, not
reparsed into a VO with a try/catch — it is a verified JWT claim, not user
input crossing a trust boundary (mirrors `ValidateJwtAsync`'s own plain
string read of `azp`). The length guard exists only so `KeycloakClientIdentifier.From`
is never called with a value its own `Ensure.That(...).HasMaxLength(255)`
would reject — not because a real Keycloak client id could realistically
exceed it.

A caller with no `azp` (not every token has one; `integration-test-admin`'s
client-credentials grant may not either) or one that names no revoked
integration is unaffected — the repository call returns `false` and the
check is a no-op. This never narrows a human operator's access; it only
ever adds a refusal for a caller whose `azp` happens to equal some
integration's own rotated client id, and that integration is revoked.

Returns 401 (`Results.Unauthorized()`), not 403: this mirrors both
`AuthenticateWebhookAsync`'s own revoked-credential answer and ADR-0160's
stated convention ("a revoked token gets the answer any other rejected
token gets: 401"). It is not a fab/entitlement refusal (which this
codebase answers with 403 via `FabAuthorizationException`) — it is "this
credential should not be trusted," the same class of refusal as a fab
resolution never reaches for a revoked credential.

## 3. The repository lookup is fail-closed, not recency-ranked

```csharp
return await dbContext.WebhookIntegrations
    .AnyAsync(
        integration => integration.KeycloakClientId == keycloakClientId
            && integration.RevokedAt != null,
        cancellationToken);
```

An earlier revision of this change picked one row per client id (ordered by
`RotatedAt` descending) and checked only that row's `RevokedAt`. Review
correctly flagged that as the wrong security rule: it was shaped to tolerate
this repo's own test fixtures, not because production ever has the
ambiguity it was resolving. Production never ties on this predicate (see
spec.md's production-uniqueness argument), so there is no row to rank by
recency in the first place — `AnyAsync` over the whole set is both simpler
and fail-closed: a revoked row sharing a client id with a live one always
refuses, instead of risking being outranked by whichever row happens to be
newest.

**That still leaves the test-environment hazard spec.md records**, just
with a different fix than "seed a fresher row". Since any revoked row
sharing a client id now refuses regardless of recency, a test that revokes
a `management-web`-tagged row and does nothing else would poison every
later test in the same process that presents that `azp` — including every
real-operator login through `management-web`
(`AspireFixture.GetAccessTokenAsync`/`CreateAuthenticatedClientAsync` mint
through it too) — no matter how many fresh rows are seeded afterward. So
both tests that revoke such a row instead **detach it from the shared
client id entirely**, in a `finally` so it still runs if the test's own
assertion fails first:

```csharp
integration.MarkAsRotated(KeycloakClientIdentifier.From($"webhook-{name}"), clock);
```

`MarkAsRotated` has no revoked guard, so this moves the already-revoked row
onto a client id derived from its own (globally unique) name — off
`management-web` for good, rather than relying on an ordering that no
longer exists.

- The new test (`ManualIngestRefusesRevokedWebhookCallerIntegrationTests`)
  does this immediately after its own revoke+refusal assertion, in a
  `finally`.
- `WebhookRevocationRefusesDeliveryIntegrationTests.A_rotated_integrations_token_stops_working_once_it_is_revoked`
  gains the identical `finally`-wrapped cleanup — it predates this change,
  and is the one existing test in the repo that revokes a
  `management-web`-tagged row, so it is the one existing landmine this
  change makes live.

This is a test-hygiene fix scoped to exactly the hazard this change
introduces. It does not touch #2206 (why the real rotate path lacks a
usable `groups` claim, which is *why* tests reach for `management-web` in
the first place) — that is pre-existing and separately filed.

## 4. Phase 4a colour

Behaviour-changing: `/events/manual` gains a new refusal it did not have.
Expect red — a webhook-sourced caller's revoked credential currently
succeeds; the new test asserts it must not.

## 5. Out of scope

See spec.md.
