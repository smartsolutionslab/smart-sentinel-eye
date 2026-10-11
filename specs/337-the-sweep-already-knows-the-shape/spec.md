# Spec 337 — the sweep already knows the shape (#2797)

Phase 1 of ADR-0037.

## Problem

Spec 320 (#2181) built `OrphanedClientSweep` to disable a Keycloak client a
cancelled registration left behind: `CreateClientAsync` succeeds, then an
`OperationCanceledException` strikes before the matching `RegisteredClient`
row's `SaveAsync` commits, leaving a live, unrevocable client with no row.
That spec deliberately scoped the sweep to `device` and `kiosk` — the two
kinds `RegisterDeviceCommandHandler`/`EnrollKioskCommandHandler` create —
and flagged the identical hole in webhook-integration registration as
out of scope, to be investigated separately (#2797, this spec).

## Investigation

`RotateWebhookClientCommandHandler`'s create branch (the first rotation of
a not-yet-existing integration) has the exact same ordering:

```
KeycloakClientCredentials credentials = await keycloak.CreateClientAsync(representation, ...);
aggregate = RegisteredClientAggregate.Register(clientId, ClientKind.WebhookIntegration, fab, rotatedBy, clock);
clients.Add(aggregate);
await clients.SaveAsync(cancellationToken);
```

A cancellation between `CreateClientAsync` returning and `SaveAsync`
committing leaves a live, stamped (`sse.kind=webhook`) Keycloak client with
no `RegisteredClient` row — identical shape to the device/kiosk hole.

**The row shape is not merely similar — it is the same table.** Webhook
integrations are already tracked as `RegisteredClient` rows with
`ClientKind.WebhookIntegration` (`RegisteredClient.cs:97` — `Rotate` is
guarded to that kind specifically), in the same `RegisteredClients` table
`OrphanedClientSweep`'s `GetActiveClientIdsAsync` already queries, filtered
only by `DisabledAt == null` — no kind filter at the query level.

`HttpKeycloakAdminClient.GetStampedClientsAsync` already returns every
stamped client regardless of kind; `OrphanedClientSweep.SweepAsync` filters
to `SweptKinds = { device, kiosk }` before anything else runs. A
`webhook`-kind orphan is returned by the Keycloak read and then dropped by
that one filter — confirmed by the existing (soon-to-split) test
`A_webhook_stamped_client_and_an_unknown_kind_are_never_candidates`.

**Decision (user, 2026-10-10, on #2797): the architect decides join-vs-
separate as a technical-fit question, same precedent as #2181's own
architect call.** Given the row shape is identical and already flows
through the same active-row query with no kind-specific branching
anywhere else in the sweep, **this spec joins webhook to the existing
sweep** rather than building a second one. There is no second remedy to
design — `SweptKinds` is the entire boundary.

## What does NOT change

- The EventIngestion-side `WebhookIntegration` aggregate (spec 318/#2814's
  territory) and its `RevokedAt`/bearer-validation model are untouched.
  An orphan — by definition — never reached the point of a successful
  `RotateWebhookClientCommandHandler` call (the row never saved, so
  `WebhookIntegrationRotatedV1` was never published), so EventIngestion
  never created a matching row either. Nothing on that side needs to know
  about this sweep.
- The mass-disable guard, the grace window, disable-not-delete — all of
  spec 320 §4's behaviour is unchanged for device/kiosk and applies
  identically to webhook once it's no longer filtered out.

## Acceptance

- A `webhook`-kind stamped client past the grace window, with no active
  `RegisteredClient` row, is disabled by the sweep — characterisation of
  "join", behaviour-changing for webhook specifically (new-behaviour red
  per ADR-0139/ADR-0144).
- A `webhook`-kind stamped client WITH an active `RegisteredClient` row
  (`ClientKind.WebhookIntegration`, `DisabledAt == null`) is left alone —
  the existing active-row check, now exercised for this kind for the
  first time.
- An unknown (`sse.kind` present but neither device/kiosk/webhook) stamped
  client is still never a candidate — unchanged, split into its own test
  so it stops being coupled to webhook's old exclusion.
- Device/kiosk behaviour: unchanged, all existing facts stay green and
  unmodified.
