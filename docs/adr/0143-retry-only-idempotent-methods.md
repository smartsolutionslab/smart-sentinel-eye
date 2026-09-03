# ADR-0143: Retry only idempotent methods, by default

**Status:** **Accepted**
**Date:** 2026-09-03
**Amends:** ADR-0088's resilience defaults — the retry predicate only

**Supersedes:** —
**Superseded by:** —

## Context

ADR-0088 gives every `HttpClient` the standard resilience handler through
`ConfigureHttpClientDefaults`. Its retry fires on 5xx, 408 and transport
exceptions, and **it never looks at the HTTP method**: a `POST` is retried
exactly like a `GET`.

A request that reached the server, was processed, and whose response was lost is
indistinguishable from one that never arrived. So the retry can apply an effect
twice — and it did. `POST /devices/register` answered a caller with
`DEVICE_ALREADY_REGISTERED` for the device that caller's own earlier attempt had
created (#2039), reproduced on two branches while A/B-testing #2040.

ADR-0142 fixed that endpoint by letting a caller opt into replay with an
`Idempotency-Key`. **This is the other half.** ADR-0142 protects the endpoints
someone has got to; this protects the ones nobody has, which is currently all of
them except Identity's three.

### Why the client-side fix is not redundant with ADR-0142

They cover different populations and neither subsumes the other:

- ADR-0142 works for *any* caller, including browsers and operator tooling we do
  not configure — which is exactly why Identity's credential endpoints needed it.
- This works for *every* endpoint without each one having to adopt anything —
  including the seven creates listed in #2042, until they do.

## Decision

**The standard handler retries only methods RFC 9110 §9.2.2 calls idempotent:
`GET`, `HEAD`, `PUT`, `DELETE`, `OPTIONS`, `TRACE`. `POST` and `PATCH` get one
attempt.** A client whose non-idempotent calls are idempotent *in fact* opts back
in with `RetryEveryMethod()`, and says why at the call site.

**Only the retry predicate narrows.** A `POST` keeps its per-attempt timeout, its
total budget and its share of the circuit breaker. It simply stops being sent
twice.

**Where the method cannot be determined, nothing is retried.** A response carries
its request; a transport exception does not, so the predicate falls back to the
resilience context. If both are empty the request is treated as
non-idempotent — the damage this prevents is silent, and the cost of being wrong
is one attempt nobody makes.

### The four opt-ins, and why each is justified

| Client | Non-idempotent call | Why retrying it is safe |
|---|---|---|
| The four `client_credentials` token clients | `POST` token | A second token supersedes the first; nothing accumulates. Losing these retries would let a transient Keycloak blip fail a host's startup outright. |
| `MediaMtxRtspGateway` | `POST` add-path, `PATCH` patch-path | `add/` answers 4xx for an existing path, which is not retried anyway; `patch/` sets the source to a fixed value, so applying it twice lands in the same place. Without the opt-in a MediaMTX blip during provisioning would leave a stream unprovisioned and never try again, which the two-second health sweep would report as a broken camera. |

`PATCH` is excluded by default despite ours being idempotent by construction,
because *by construction* is a property of a particular endpoint rather than of
the method. The opt-in is where an endpoint's own guarantee gets stated.

**`HttpKeycloakAdminClient` deliberately does not opt in.** Creating a client and
rotating a secret are both `POST`s a retry would duplicate — rotating twice
invalidates the secret the first attempt delivered. That is precisely what
ADR-0142's keys handle, and the two decisions meet here: the admin client loses
its `POST` retries and the endpoints in front of it gained replay instead.

## Alternatives considered

- **Leave it and rely on idempotency keys alone.** Rejected: keys are per
  endpoint and seven creates have not adopted them, so the hazard stays live
  for every one of those until someone gets to it. A default protects them now.
- **Retry `POST` but only on connect-time failures**, where the request provably
  never reached the server. Attractive and genuinely safe in principle;
  rejected because the distinction is not reliably available from
  `HttpRequestException` here, and a rule that is *usually* right about whether
  a request was received is the same class of thing this ADR removes.
- **Per-endpoint opt-out instead of a global default.** Rejected: it makes the
  dangerous behaviour the default and requires every future endpoint author to
  know to disable it. The whole point is that retrying a `POST` should be the
  thing that needs justifying.

## Consequences

- Any `POST` or `PATCH` a future client makes is not retried unless its author
  says why it should be. That is the intended friction.
- `#2042`'s second half is closed by this; its first half — the seven creates
  adopting keys — is unaffected and still worth doing, because a key also
  recovers the answer rather than merely avoiding the duplicate.
- The behaviour is verified by counting attempts through a real pipeline rather
  than by reading the predicate back: `IdempotentRetryTests` asserts 4 attempts
  for `GET` and `PUT`, 1 for `POST` and `PATCH`, on both the response and the
  exception paths, and 4 again for a client that opted in.
