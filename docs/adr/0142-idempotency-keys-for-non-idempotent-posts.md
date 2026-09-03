# ADR-0142: Idempotency keys for non-idempotent POSTs

**Status:** **Accepted**
**Date:** 2026-09-03
**Amends:** the constitution's §Reality-check kiosk bullet — the words *single-reveal secret*
**Amends:** spec 008 FR-010 and FR-012 — the `409` they specify for a repeat registration
**Amends:** ADR-0131's "reveals its secret once" (restated, not reversed)

**Supersedes:** —
**Superseded by:** —

## Context

ADR-0088 and ServiceDefaults give every `HttpClient` the standard resilience
handler. Its retry strategy fires on 5xx, 408 and `HttpRequestException`, and
**it does not look at the HTTP method**. A `POST` is retried exactly like a
`GET`.

A request that reaches the server, is processed, and whose *response* is lost is
indistinguishable from one that never arrived. So a retried `POST` can be
applied twice — or, where the endpoint refuses duplicates, can report a failure
for work that actually succeeded.

**This is not hypothetical.** It was observed while A/B-testing #2040 against
`develop`, and it reproduced on both branches:

```
created.StatusCode should be HttpStatusCode.Created but was HttpStatusCode.Conflict
body: {"title":"DEVICE_ALREADY_REGISTERED",
       "detail":"A device with clientId 'plc-t040-01a065d791f271b48db4372c4048c2bf'
                 is already registered."}
```

The first hypothesis — leftover Keycloak state, since the realm has a persistent
volume — was wrong: the test builds its identifier as
`t040-{Guid.CreateVersion7():N}`, fresh per call. The second — that #2040's
singleton token provider caused it — was excluded by the A/B, which reproduced
the failure on `develop`. What remains is the mechanism above: the first
`POST /devices/register` registered the device and was too slow to answer under
load; the retry got a 409 from its own predecessor's work.

### The spec already had an idempotency story, and this is it

Spec 008 FR-010 says the endpoint is "Idempotent on `(deviceType, deviceId)` —
re-registering **returns 409**". That is a real choice and it is safe: nothing is
duplicated. It is simply the wrong answer for a *transparent retry*, because the
caller never learns that its own earlier attempt is what created the conflict. A
409 is correct for a second human request and misleading for a second TCP
attempt at the first one.

### Why the timing matters more than it looks

The retry exists **because the first attempt was slow**. So the second request
usually arrives while the first is still in flight. Any scheme that only records
"this key completed" replays nothing in the exact window that produced the
failure, and returns a different error instead of the same answer. In-progress is
the interesting state, not the completed one.

### The hard part: three endpoints return a credential

`POST /devices/register`, `POST /kiosks/enroll` and `POST /webhooks/{name}/rotate`
return a plaintext client secret. `ClientSecret` is deliberately write-once —
`Reveal()` throws on the second call, and its own doc says *"We never persist the
plaintext — Keycloak is the system of record."* The constitution repeats the
guarantee for kiosks, and so does ADR-0131.

Replaying a stored response is therefore unavailable for exactly the endpoint
where the failure was observed. Storing the plaintext to replay it would put a
recoverable secret at rest in our Postgres and contradict the VO's stated reason
for existing.

**And these three cannot be fixed from the client side.** Unlike the seven
non-secret creates, their callers are management-web, operator tooling and test
harnesses — none of which we configure. Only the server can make them safe.

## Decision

**A caller may supply an `Idempotency-Key` request header on a non-idempotent
`POST`. When it does, the server guarantees the operation is applied once and
that the same key returns the same answer.**

Four parts:

1. **Opt-in, not mandatory.** No key, no change in behaviour — every endpoint
   keeps the semantics it has today, including FR-010's 409 for a genuine
   duplicate. This is what keeps the amendment below narrow.

2. **Reserve, then complete.** The key is inserted before the work starts and
   updated with the created resource's identifier when it finishes. The three
   states are distinguishable, and the middle one is the point:

   | State | Meaning | Answer |
   |---|---|---|
   | absent | first arrival | do the work |
   | reserved, no identifier | the first attempt is still running | wait briefly, then replay; `409 IDEMPOTENT_REQUEST_IN_PROGRESS` if it has not finished |
   | completed | the first attempt succeeded | replay its answer |

   A reservation is **released** when the work fails or is cancelled, so a
   half-finished attempt cannot wedge the key forever. That is the failure mode
   a naive `INSERT ... ON CONFLICT DO NOTHING` would have.

3. **Nothing sensitive is stored.** The row holds the key, the endpoint, the fab
   and the created resource's identifier — never a response body and never a
   secret. A replay **rebuilds** the answer: for the credential endpoints it
   reads the secret back from Keycloak, which is already the system of record
   and already exposes it (`ReadClientSecretAsync` exists and is used by
   `CreateClientAsync`).

4. **Per context.** The table lives in each context's own schema, like the
   Wolverine outbox and like `variable_value_request_dedup`. There is no shared
   database to put it in, and inventing one for this would be a larger deviation
   than the problem warrants.

### The amendment, stated narrowly

> A client secret is revealed **once per idempotency key**. Absent a key —
> which is every request today — it is revealed exactly once, unchanged.

This is a qualification, not a reversal. What it concedes is real and worth
naming: a caller that supplies a key and retries can receive the same secret
more than once over the wire. What it buys is that the caller receives *its own*
secret instead of a 409 for a device it successfully created and can now neither
use nor re-register.

The alternative reading — that a secret must never cross the wire twice under
any circumstance — cannot coexist with transparent retries, because the retry is
indistinguishable from the original request by construction. One of the two has
to give, and a rule that silently loses a caller's credentials is the worse of
the pair.

## Alternatives considered

- **Stop retrying POSTs** (narrow `ShouldHandle` to idempotent methods).
  Cheapest, and it fixes our own service-to-service clients. Rejected as the
  whole answer because the three credential endpoints are called by clients we
  do not configure; it would leave the observed failure reachable. Still worth
  doing on its own merits and is left open on #2039.
- **Store the response, encrypted** (Stripe's model). Fully general and fixes
  every endpoint without touching the domain. Rejected: it puts
  plaintext-recoverable secrets at rest in Postgres, directly against
  `ClientSecret`'s stated reason for existing, and needs a key-management story
  this system does not have.
- **Return a redeemable ticket instead of a secret.** The cleanest security
  story — replay then carries nothing sensitive. Rejected as out of proportion:
  it is an API redesign affecting every existing device and kiosk enrolment
  client, to solve a retry problem.
- **Put the key on the aggregate** rather than in its own table. Rejected: an
  idempotency key is a transport concern, and `RegisteredClient` should not grow
  a column because HTTP has a retry policy. The separate table follows
  `VariableValueRequestDedupStore`, which is the existing precedent for exactly
  this shape.

## Consequences

- The seven creates that return no credential — cameras, layouts, overlays,
  rules, system variables, webhook integrations, manual events — can adopt the
  same mechanism with no amendment at all, because rebuilding their answer needs
  only the stored identifier.
- Keys are durable and not swept. A create's key is worth keeping for as long as
  the thing it created; a TTL would reintroduce the "same key, different answer"
  hazard the mechanism exists to remove.
- `#2039` stays open for the client-side half — the standard handler still
  retries POSTs to endpoints that have not adopted a key.
- **Rollout is incremental.** Identity's three endpoints go first because they
  carry both the observed failure and the hard case. The remaining seven are
  mechanical against this ADR.
