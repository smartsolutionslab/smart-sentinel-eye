# Spec 302 — A revoked credential says so in the log

**Issue:** #2205 (split from #603) · **Branch:** `fix/2205-revoked-webhook-audit-line`
**Context:** EventIngestion · **ADRs:** 0050 (logging), 0118 (one OTLP sink), 0141 (`Option<T>`), 0144 (lane — this decision was made outside it)
**Engineer:** backend · **Phase 4a colour:** behaviour-changing → **red first** (§6)

## 1. The decision being implemented (made by a human, 2026-10-06, on #2205)

> Keep the collapsed 401 (revoked vs. never-existed), add a distinguishable
> internal audit line. Zero external disclosure change.

This spec implements that decision; it does not revisit it. No new ADR (§7).

## 2. Problem

`EventsEndpoints.Writes.cs:211` folds "revoked" and "never existed" into one
`null`, and both answer 401 with an empty body. That is deliberate and is pinned
by spec 102. But nothing anywhere records *which* it was, so an operator whose
sensor stopped delivering at 03:00 cannot learn from the system that the
integration was revoked — the 401 is indistinguishable from a typo in the URL.

## 3. User story

**US1 (P1)** — As the on-call operator, when a webhook delivery is refused
because its integration was revoked, I can find a log record saying so — naming
the integration, its plant and when it was revoked — while the caller still
receives exactly the refusal an unknown integration gets.

### Acceptance scenarios

```gherkin
Scenario: A delivery to a revoked integration is logged as revoked
  Given a webhook integration "X" registered in fab "munich"
    And "X" has been revoked
  When a delivery is POSTed to /events/webhook/X?fabId=munich with X's former token
  Then the response is 401 with an empty body
    And the event-ingestion log contains one Warning record naming "X",
        its identifier, fab "munich" and its RevokedAt instant,
        worded distinctly from the revocation-time record

Scenario: A delivery to an integration that never existed logs no revocation
  Given no webhook integration named "Y" exists
  When a delivery is POSTed to /events/webhook/Y
  Then the response is 401 with an empty body
    And no "revoked" refusal record naming "Y" is written

Scenario: The caller cannot tell the two apart (unchanged, spec 102)
  When the revoked and never-existed deliveries above are compared
  Then status, body and WWW-Authenticate are identical

Scenario: The record never carries the credential
  When the revoked-delivery record is written
  Then it contains neither the bearer token nor any part of the request body
```

Bad-request / auth: the route is `.AllowAnonymous()` and authenticates by bearer
itself; every non-revoked failure path (missing/malformed bearer, unparseable
name, unknown name, wrong plant, bad token) is unchanged and writes no new record.

## 4. Independent end-to-end test procedure

Boot the Aspire stack; as `admin`, register then revoke an integration; POST a
delivery to it; observe 401 and, in the `event-ingestion` resource's logs
(Aspire dashboard → Structured logs), the new Warning record. POST to a name
never registered; observe 401 and no such record.

## 5. Locked choices

- `ILogger` + `[LoggerMessage]` source-gen, structured fields, value-object
  arguments (ADR-0050), mirroring `src/StreamDistribution/Api/Log.cs`.
- An application log line to the one OTLP sink (ADR-0118), **not** an
  AuditObservability row — reasoning in plan §2.
- **Latency budget: N/A.** The line is written only on the refusal path, which
  produces no event and so is not on the event→overlay path (§IV).

## 6. Test colour

Behaviour-changing → red. The two new log facts must be observed failing before
the change. Spec 102's three facts, `AnonymousIngestIsRefusedTests` and
`WebhookBearerValidationIntegrationTests` are **characterisation**: they must
pass **unmodified** after the change. Any edit to them is a block, not an
adjustment.

## 7. ADR judgement

None needed. The external contract is byte-for-byte unchanged; the addition is
one internal log record using an existing convention (ADR-0050) into the
existing sink (ADR-0118). It adds no contract, no context flow, no persistence.
Routing it into AuditObservability instead *would* need an ADR — which is one
more reason not to (plan §2).

## 8. Explicit assumption (one guess, flagged)

**One record per refused delivery, not per transition.** A forgotten device
still posting to a revoked integration writes one Warning per attempt. The repo
has twice chosen "log the transition, not each refusal" for flood control
(spec 119, spec 208 under ADR-0118). This spec does not, because the decision
on #2205 explicitly rejected added complexity, and the trigger population is
narrow: the request must name a real, revoked integration. If the reviewer
prefers once-per-integration-per-window, that is a contained change to T003
and does not alter the tests' shape (they assert ≥1 record).
