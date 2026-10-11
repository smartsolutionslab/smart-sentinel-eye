# ADR-0172: Device and kiosk Keycloak clientIds are fab-qualified

**Status:** Accepted
**Date:** 2026-10-11
**Supersedes:** —
**Superseded by:** —

## Addendum (2026-10-11) — kiosk enrollment's wire contract changes

The implementing work found a wire-contract question this ADR left as
"exact format to be finalized in the implementing spec" and it turned
out to be a real choice, not a detail. Devices build their clientId
server-side from caller-supplied parts (`{deviceType}-{deviceIdentifier}`),
so fab-qualifying them is a one-line change. Kiosks are different:
`EnrollKioskRequest`/`EnrollKioskCommand` take a full caller-supplied
`ClientId` directly, with no fab segment built in.

**Decision: the server composes the kiosk clientId, symmetric with how
devices already work.** `POST /kiosks/enroll`'s request body changes
from a full `clientId` to a bare `name`; the server composes
`kiosk-{fab}-{name}` itself. This is a breaking change to the enrollment
request shape — every caller of `/kiosks/enroll` needs updating — but
it removes any way for a caller to construct the fab-qualified form
incorrectly, matching the guarantee devices already have.

Rejected: keeping the caller-supplied full clientId and validating it
already starts with `kiosk-{fab}-` matching the resolved fab. That
avoids the contract break but pushes the format requirement onto every
caller with no server-side enforcement that they constructed it
correctly — exactly the kind of silent drift this ADR programme exists
to close, not reproduce in a new place.

Found during #2756's implementation, in particular a dedicated existing
test — `tests/Integration.Tests/Identity/CrossFabRegistrationConflictIntegrationTests.cs`
(spec 304/#2626) — which already anticipated this exact ADR by name in
its own doc comment, and whose two facts need their assertions inverted
(a cross-fab registration of the same identifier should now succeed,
not conflict) as part of this work, alongside the mechanical rename
across the ~28 files that reference the `plc-`/`kiosk-` clientId
literals.

## Context

Keycloak clientIds are unique per realm (ADR-0159: one realm per
deployment, several fabs underneath it), but device and kiosk
registration pick clientIds with no fab qualifier (`{plc|inference}-
{identifier}` for devices; any caller-supplied string for kiosks). Two
problems follow, confirmed against current code (#2756):

1. **Disclosure.** `RegisterDeviceCommandHandler` and
   `EnrollKioskCommandHandler` both answer a fab-unscoped `409` when a
   name is already taken — by a client in the caller's own fab, a
   different fab, or a disabled client from a different fab that
   `DisableClientAsync` never deleted. An authenticated caller (any
   operator — the relevant write scopes are default client scopes on
   `management-web`, not role-gated) can probe a name and learn whether
   it exists in a fab they don't belong to. This is lower severity than
   #2542's unauthenticated oracle — probing creates a real, logged
   credential as a side effect, making brute-force noisy — but it is
   real.
2. **Collision, independent of any attacker.** Because the namespace is
   realm-global, two ordinary fabs that both name a device
   `plc-press-01` collide outright: the second plant simply cannot
   register its device. This is a functional defect, not only a
   disclosure concern, and it is the stronger reason to fix it.

Keycloak does not offer per-fab client namespacing short of a realm per
fab, which would reverse ADR-0159. The only real fix is qualifying the
clientId string itself.

## Decision

**Fab-qualify device and kiosk clientIds**: `plc-{fab}-{identifier}`
and `kiosk-{fab}-{name}` (exact format to be finalized in the
implementing spec, consistent with existing identifier conventions).
Both existence checks move to a fab-scoped lookup, so a 409 only ever
means "taken in your own fab" — closing both the disclosure and the
collision.

**This changes what a device's Keycloak clientId is, and the MQTT
broker's Go auth plugin enforces `azp == username` against exactly that
value (ADR-0100)**. Because no production deployment exists yet
(ADR-0118), "migrating existing clients" means dev seeds, fixtures, and
a dev Keycloak volume reset — the cheapest this migration will ever be.
Every existing client already carries `sse.fab`/`sse.kind` attributes
(stamped since #2728), so a future migration tool, if one is ever
needed against a real deployment, has what it needs to reattach a fab
to a legacy clientId.

**Webhooks are explicitly excluded from this decision.** EventIngestion's
own webhook-name registration (`RegisterWebhookIntegrationCommandHandler`)
already answers a realm-wide 409 before the Keycloak rotate path is ever
reached, because the ingest route (`POST /events/webhook/{name}`)
requires a globally-unique name by design. Fab-qualifying the Keycloak
clientId cannot close that oracle — #1545 (already open) owns whether
and how to. This ADR does not reopen or duplicate that question.

## Consequences

**Positive:**
- Closes the cross-fab disclosure oracle for devices and kiosks.
- Fixes the real collision bug: two fabs naming a device the same thing
  no longer blocks the second registration.
- Done before any production deployment exists, so there is no live
  credential migration to perform.

**Negative:**
- Device clientIds (and therefore MQTT usernames, per ADR-0100) change
  shape. Any dev fixture, seed script, or documentation that hardcodes
  the old unqualified form needs updating.
- The webhook third of #2756 stays open, tracked on #1545, not closed
  by this ADR.

## Scope, explicitly

This ADR covers devices and kiosks only. It does not mandate kind-
prefixing beyond what's needed to disambiguate (kiosk clientIds already
read distinctly from device clientIds once fab-qualified); a separate,
narrower kind-prefix-only fix is not pursued since this ADR's fab-
qualification already closes the cross-fab oracle for both kinds.
