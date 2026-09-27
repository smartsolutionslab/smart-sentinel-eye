# ADR-0160: A revoked client is refused at request time, from a local snapshot of `DisabledAt`

**Status:** **Accepted**
**Date:** 2026-09-27
**Decided by:** product owner, 2026-09-26 (comment on #2241). This ADR records
that decision and the mechanism chosen to carry it out; it does not re-open the
choice between the three options #2241 listed.
**Relates to:** ADR-0007 / ADR-0008 (Keycloak, OIDC), ADR-0023 (scopes),
ADR-0106 (the gateway does no auth offload), ADR-0116 (a service-account client
for a cross-context read), ADR-0143 (retry only idempotent methods),
ADR-0153 (one instance per service), ADR-0154 (readiness is liveness; degrade,
don't fail)

**Supersedes:** —
**Superseded by:** —

## Context

Every API in this system validates Keycloak access tokens **locally**: the nine
REST APIs through the `JwtBearer` handler that
`AuthenticationDefaults.AddBearerAuthentication` registers, and the WHEP hook
through `WhepAuthValidator`. Both check signature, issuer, audience and
lifetime against the realm's published keys. Neither asks anyone whether the
client that minted the token is still allowed to act.

Since spec 121 (#2165/#2207), disabling a kiosk, device or webhook client
genuinely disables its Keycloak client, so **no new token can be minted**.
Tokens already minted stay valid until they expire. The realm sets
`accessTokenLifespan: 3600`, so a client revoked at T is still served until
T + 60 min on every API and on stream setup (#2241).

#2241 named three ways to close that window: token introspection at every
enforcement point; a check against Identity's `DisabledAt` at use time; or a
shorter `accessTokenLifespan`. ADR-0144 forbids the autonomous lane from
choosing between them. The product owner chose on 2026-09-26:

> Close via a DisabledAt check at request time, not introspection or a
> shortened token lifespan — cheap, no new live dependency on the WHEP
> media-setup path.

What remains is *how* each of ten enforcement points in eight other bounded
contexts reads a value that lives in Identity's database, without a
cross-context project reference (constitution §III), and without putting
Identity on the path of every request. That mechanism will be copied by
anything that later needs to refuse a token for a reason Keycloak cannot put
into it, which is why it is recorded here and not only in a plan.

## Decision

1. **Every enforcement point refuses a token whose client was disabled after
   the token was minted.** Specifically, a token is refused when Identity has
   a disabled `RegisteredClient` whose `ClientId` equals the token's `azp`
   and whose `DisabledAt` is not earlier than the token's `iat` minus a
   clock tolerance of **5 minutes**. The comparison is against `iat`, not
   against the client id alone, because a disabled row releases its client id
   for re-registration (`RegisteredClientRepository.GetByClientIdAsync`). A
   re-registered client's new tokens are minted after the old `DisabledAt` and
   must pass. The tolerance equals the `ClockSkew` the bearer pipeline already
   extends to `exp`. It absorbs disagreement between Keycloak's clock, which
   stamps `iat`, and Identity's clock, which stamps `DisabledAt`. The cost is
   that a client id re-registered within five minutes of its revocation is
   refused for the rest of those minutes. That errs toward refusal.

2. **The check reads memory, never the network.** Each service holds a
   snapshot of `clientId → latest DisabledAt`. A background refresher in
   `ServiceDefaults` replaces it every **5 seconds** from a new Identity
   endpoint, `GET /registered-clients/revoked`, under a new scope,
   `sse.identity.revocations.read`. Identity reads its own table in process.
   The request path does a dictionary lookup. It performs no I/O and needs no
   lock.

3. **The snapshot is fail-static, not fail-closed.** A failed refresh keeps the
   last snapshot, and it stays correct: revocations only accumulate, there is
   no re-enable, and revoking goes through the same Identity that the failed
   refresh could not reach. A service that has never loaded a snapshot, because
   it started while Identity was unreachable, admits tokens exactly as it does
   today. It reports its `revocation-snapshot` health check **Degraded**
   (ADR-0154) and logs one Warning on each transition, as `WhepAuthValidator`
   does for a realm outage. Failing closed would make Identity a start
   dependency of every wall, which is the live dependency the decision
   excludes.

   **Two narrower cases this does not cover** (phase-6 security review,
   spec 270): a *network partition* isolating one consumer from a still-live,
   still-revoking Identity leaves that consumer admitting newly revoked
   clients until the partition heals — the "same Identity" argument above
   assumes reachability, not just liveness; the exposure is bounded by token
   lifetime. And a *wrong* `RevocationList__ClientSecret` (as opposed to a
   missing one) produces the same Degraded-forever state as an unreachable
   Identity, indistinguishable from it by this health check, with no separate
   signal for "this will never recover on its own." Both are accepted for
   this change; the second is filed as a follow-up (tasks.md).

4. **One shared read-only service account** reads the list:
   `revocation-list-reader`, holding only `sse.identity.revocations.read`.
   ADR-0116's pattern is one account per purpose. The purpose here is a single
   read, identical for every consumer, so eight clients would be eight copies
   of one grant.

5. **Enforcement lives in two places, both of them shared.** For the nine REST
   APIs it is `JwtBearerEvents.OnTokenValidated` inside
   `AddBearerAuthentication`, chained after any handler a service already set.
   For WHEP it is `WhepAuthValidator.ValidateAsync`, after the signature check
   and before a subject is returned. A revoked token gets the answer any other
   rejected token gets: `401` on REST, `WhepAuthFailure.TokenRejected` on WHEP.

## Consequences

**Easier / better**

- The revocation window drops from ≤ 60 min to ≤ one refresh period plus the
  refresh's own latency, 5–10 s in practice, on all ten enforcement points.
- No per-request dependency on Identity or Keycloak. An Identity outage costs
  freshness, never availability.
- `accessTokenLifespan` stays independent of revocation. It can be tuned for
  other reasons without reopening this.
- A webhook client's Keycloak-token route (`POST /events/manual`) gets a
  second, independent refusal on top of spec 264's disable. The check does not
  care about the client kind.

**Harder / constrained**

- Every service now polls Identity. Load is 8 × 0.2 rps against a table of a
  few hundred rows. This is trivial now. Re-measure if `registered_clients`
  passes ~10⁴ rows, where bounding the response to clients disabled within
  `accessTokenLifespan + tolerance` becomes the obvious refinement.
- The snapshot is per process. This is correct under ADR-0153's one instance
  per service. A service that later earns a second replica needs nothing extra,
  because each replica polls on its own.
- **Only connection setup is covered.** An already-established WebRTC session
  or SignalR connection is not torn down when its client is revoked. It is
  refused on its next reconnect or re-authorisation. Closing live sessions is
  not part of this decision.
- **Human principals are not covered.** Operators and wall users are Keycloak
  users, not `RegisteredClient` rows. Disabling one of them in Keycloak still
  leaves their access tokens live until expiry. #1995 and the offline-grant
  note on #2241 remain their own work.
- One more secret for the AppHost to seed, mirrored in the realm import and
  paired by `RealmImportMirrorTests`.

## Alternatives Considered

- **Token introspection at every enforcement point.** Rejected by the product
  owner (2026-09-26). It puts a synchronous Keycloak call on every request and,
  in particular, on WHEP media setup.
- **Shorten `accessTokenLifespan`.** Rejected by the product owner
  (2026-09-26). It narrows the window without closing it, and it multiplies
  token mints across every client.
- **Read-through cache per `azp` with a short TTL.** This is the same data, but
  every miss and every expiry calls Identity on the request path, including a
  revoked client's first request after the TTL. That is a live dependency by
  another name. A refresh-ahead snapshot has the same freshness bound and
  keeps the request path local.
- **Push revocations over RabbitMQ into each service's memory.** This gives
  sub-second propagation and needs no credentials. But a restarted service
  forgets every revocation it heard, and revocations are exactly what must
  survive a deploy. Recovering them needs a pull anyway, or a durable table in
  eight databases. A periodic snapshot broadcast would avoid the pull, but it
  would be an integration event every five seconds that AuditObservability is
  required to record. A pull is one mechanism. Push plus recovery is two.
- **Read Identity's table directly from each service.** This breaks
  constitution §III. It is a cross-context dependency on another context's
  schema.
- **Enforce once at the API gateway.** ADR-0106 deliberately does no auth
  offload. The services are reachable without the gateway, and WHEP never
  passes through it.

## Implementation Notes

Spec 270 (`specs/270-the-token-that-outlives-its-revocation/`) builds this.
No constitution amendment: §Security's "token-bound, short-lived credentials"
and §VIII are unchanged in wording and better met in fact.
