# ADR-0174: The webhook integration registry is fab-owned

**Status:** Accepted
**Date:** 2026-10-11
**Supersedes:** —
**Superseded by:** —

## Context

Spec 018 closed every operator-driven event-path leak across fabs — both
reads, the manual write endpoint, and the rejected-delivery listing —
except one, left deliberately open (FR-016): `POST /webhook-integrations`,
`GET /webhook-integrations`, and `DELETE /webhook-integrations/{name}`
are not fab-scoped, and the `WebhookIntegration` aggregate carries no
`FabIdentifier` (#1545).

Two coherent answers existed:

1. **Fab-owned**: an integration belongs to a plant; the three endpoints
   resolve fab like every other fab-scoped endpoint; one plant's
   operators can neither see nor revoke another's integrations.
2. **Shared template**: the per-delivery check (`POST
   /events/webhook/{name}?fabId=`, spec 018 FR-014) already proves
   entitlement on every delivery, so the registry itself carries no
   authority and one integration serving several plants is legitimate.

Answering this was out of scope for spec 018, whose job was closing a
live leak, not widening the feature. It was left as the one question
spec 018 did not settle.

## Decision

**The webhook integration registry is fab-owned.** `WebhookIntegration`
gains a `FabIdentifier`. `POST /webhook-integrations`, `GET
/webhook-integrations`, and `DELETE /webhook-integrations/{name}`
resolve and enforce fab scope the same way every other fab-scoped
endpoint in this isolation programme does (#1155). One plant's
operators cannot list, see, or revoke another plant's integrations.

This is consistent with every aggregate the isolation programme has
touched, and closes the same enumeration-by-name oracle the programme
already closed on every other read path — `GET /webhook-integrations`
no longer discloses another plant's integration by name.

**The per-delivery fab check (spec 018 FR-014) is unaffected and
becomes defence in depth** rather than the only control: a delivery
still proves its own entitlement via `?fabId=` against the caller's
credentials, now backed by a fab-owned registry rather than standing
alone.

## Consequences

**Positive:**
- Closes the last open leak spec 018 identified but did not fix.
- Consistent registry-ownership model across every fab-scoped aggregate
  in the isolation programme.
- The per-delivery check gains a second, independent layer rather than
  being the registry's only protection.

**Negative:**
- A webhook integration can no longer legitimately serve multiple
  plants from one registration — if that pattern is ever wanted, it
  needs its own future decision reopening this question, not a quiet
  workaround.
- Migration cost: `WebhookIntegration` rows need a fab backfilled. No
  production deployment exists yet (ADR-0118), so this is a dev-seed/
  fixture concern now, not a live-data migration — the cheapest this
  will ever be.

## Note on #2756

#2756 (Keycloak clientId namespace leaking cross-fab existence via
status codes) explicitly deferred its webhook third to this issue,
since EventIngestion's own webhook-name registration already answers a
realm-wide 409 before the Keycloak rotate path is ever reached — fab-
qualifying the Keycloak clientId alone (ADR-0172) cannot close that
oracle. Now that the registry itself is fab-owned, the implementing
spec for this ADR should check whether `RegisterWebhookIntegrationCommandHandler`'s
name-uniqueness check can also become fab-scoped, closing #2756's
webhook third as a side effect — or record explicitly why it still
can't (e.g. if the ingest route `POST /events/webhook/{name}` still
needs a realm-global name for routing reasons independent of
ownership).
