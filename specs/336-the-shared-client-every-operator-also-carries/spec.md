# Spec 336 — the shared client every operator also carries (#2814)

Phase 1 of ADR-0037.

## Problem

`POST /events/webhook/{name}` refuses a revoked webhook integration by
checking that integration's own `RevokedAt` directly
(`EventsEndpoints.Writes.AuthenticateWebhookAsync`). `POST /events/manual`
checks only the caller's scope, fab, and ADR-0160's generic `DisabledAt`
snapshot — which is stamped only *after* Keycloak's own disable succeeds
(spec 264, "Keycloak first, then the row"), a window #2629 widened to
~70 min. A webhook integration whose Keycloak client hasn't been disabled
yet (dead-lettered, or mid partial-outage) can mint a fresh token and write
through `/events/manual`, bypassing the per-webhook check the other endpoint
already has.

Found during #2629's security review; filed as #2814.

## Decision

Mirror the webhook endpoint's check onto `/events/manual`: identify a
webhook-sourced caller by the `azp` claim, look up the
`WebhookIntegration` whose rotated `KeycloakClientId` equals it, and refuse
(401) if that integration is revoked. Additive — no change to ADR-0160's
own snapshot check, no change to spec 264's Keycloak-first ordering (both
considered and explicitly out of scope per the issue).

No new ADR: this is an additive authorization check at the API layer, not a
reversal of an intentional decision.

## A finding from investigation, not assumed

Checked before writing any code: in production, `RotateWebhookClientCommandHandler`
derives `KeycloakClientId` as `"webhook-" + integrationName`, and
`WebhookIntegrationName` is globally unique (`ux_webhook_integrations_name`).
So in production, `KeycloakClientId` is 1:1 with one integration — the
lookup this spec adds can never be ambiguous there.

**It can be ambiguous in this repo's own integration tests.** The only
password-grant-capable public client in the dev realm is `management-web`
(every human operator's own login client), and several existing JWT-mode
webhook test fixtures (`WebhookRevocationRefusesDeliveryIntegrationTests`,
`WebhookBearerValidationIntegrationTests`) seed a `WebhookIntegration` row
with `KeycloakClientId = "management-web"` as a stand-in, because the real
rotate path's service-account token carries no usable `groups` claim yet
(#2206). Before this change, nothing ever looked up a row by
`KeycloakClientId` alone, so sharing that value across unrelated test files
was harmless. After this change, revoking such a row makes **every real
operator's token** (`azp` is also `management-web`) look like a revoked
webhook caller to `/events/manual`, for the rest of that test run.

Resolved two ways, both load-bearing (see plan.md §3):
1. The repository check is **fail-closed**, not recency-ranked: it refuses
   if *any* row sharing this client id is revoked
   (`IsRevokedByKeycloakClientIdAsync`), rather than picking one row by
   `RotatedAt` and checking only it. Production never has two rows sharing
   a client id, so this is also the simpler rule — it just happens to also
   be the one that does not depend on test-fixture recency.
2. Because fail-closed means a revoked row sharing a client id poisons
   every later test regardless of what gets seeded afterward, every test
   that revokes a `management-web`-tagged row now **detaches that row from
   the shared client id** afterward (`MarkAsRotated` onto a client id
   derived from its own unique name — it has no revoked guard), in a
   `finally` so the detach still runs if the test's own assertion fails
   first. `WebhookRevocationRefusesDeliveryIntegrationTests` gained this
   because it predates this change and is the one existing test that
   revokes such a row.

Not fixed here, and out of scope: the root cause (#2206, real rotated
webhook tokens carry no `groups` claim) and giving JWT-mode webhook test
fixtures their own dedicated, non-shared Keycloak client. Both are
pre-existing, separately-filed gaps this issue's scope does not extend to.

## Out of scope

- Reversing spec 264's Keycloak-first disable ordering (a different idea
  the #2629 reviewer also raised — that needs an ADR).
- #2206 (service-account JWTs carry no `groups` claim).
- A database-level uniqueness constraint on `KeycloakClientId` — the
  invariant already holds in production via the name-derivation, and
  adding one is unrelated to this issue's ask.
