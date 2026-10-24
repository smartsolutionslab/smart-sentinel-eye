# Spec 445: A webhook credential is audited when it is issued

**Issue:** #3110 (feature-level, on Project #13) · **Branch:** `feat/3110-webhook-registered-audit`
**ADRs:** ADR-0185 (per-type durable outbox; decision 3's "out of scope, a follow-up gap to file" paragraph names this
exact path, decision 4 is the payload rule, decision 5 the latency N/A), ADR-0179 decision 5 (a response that
discloses a credential is audited before disclosure), ADR-0102 (`EventMetadata` is what the audit subscriber reads),
ADR-0040 / ADR-0073 (domain event in-process, `V1` integration event in `Shared.Contracts`), ADR-0088 (Postgres
outbox, eager transaction), ADR-0144 (autonomous lane), ADR-0139 (red first). ADR-0142 / ADR-0143 read and **not
engaged**: `POST /webhook-integrations` is the documented route with no `Idempotency-Key` (its token is stored only
as a hash) and `POST` is not retried, so one 201 is one registration.
**Lane:** autonomous · **Phase-4a colour:** **red** (new behaviour) · **Engineer:** backend only
**Security-sensitive:** **yes** — credential-issuing endpoint and the audit trail. `security-reviewer` is mandatory at
phase 6.
**New ADR needed:** **no.** The owner's ruling on #3110 (2026-10-23) is the decision ADR-0185 decision 3 deferred;
the mechanism (a context declares its own durable types) and the payload rule (decision 4) are ADR-0185's. ADR-0185's
enumeration is not amended by this spec (the lane does not edit ADRs); the PR names the 18th durable type so a human
can add an *Implementation Notes* line if wanted.
**Scope:** creation only. The Identity replay half (`ReplayRegisteredClientQueryHandler`) is #3096 and is not
touched. Revoke's null actor is a separate issue and is not touched.
**Written 2026-10-24 against `develop` at `c872a78a`.**

---

## Premise, checked at `c872a78a`

1. **Holds.** `RegisterWebhookIntegrationCommandHandler.HandleAsync` (`src/EventIngestion/Application/Commands/Handlers/RegisterWebhookIntegrationCommandHandler.cs:34-42`)
   calls `WebhookIntegration.Register`, `Add`, `SaveAsync`, logs, and returns the plaintext token. No integration
   event is published.
2. **Holds.** `WebhookIntegration.Register` (`src/EventIngestion/Domain/WebhookIntegration/WebhookIntegration.cs:83`)
   raises `WebhookIntegrationRegisteredDomainEvent(Name, DefaultKind, RegisteredAt)` — no `Identifier`, `Fab` or actor.
   No `IDomainEventHandler<WebhookIntegrationRegisteredDomainEvent>` is registered
   (`EventIngestionInfrastructureModule.cs:111-126` registers two: Ingested, Revoked).
   `EventTypeRegisteredDomainEvent.cs:7-10` cites it as *the precedent for a domain event with no subscriber*.
3. **Holds.** The command carries no actor (`RegisterWebhookIntegrationCommand(Name, Fab, DefaultKind)`); the endpoint
   (`WebhookIntegrationsEndpoints.cs:81-125`) has the `ClaimsPrincipal` and does not call `ToOperatorIdentifier()`,
   which its siblings in the same project do (`EventTypesEndpoints.cs:132`).
4. **Holds.** `ResourceKind.cs:37-47` records that a hand-tweak once named `EventIngestion.WebhookIntegrationRegisteredV1`,
   a type that does not exist in `Shared.Contracts`. `V1ResourceMap.Conventions.cs:66-69` maps the Rotated and Revoked
   types to `ResourceKind.WebhookIntegration` by `IntegrationName`.
5. **The transaction already exists.** `WebhookIntegrationRepository.SaveAsync` dispatches domain events **before**
   `commit.CommitAsync` (`WebhookIntegrationRepository.cs:62-88`); a domain-event handler's publish is captured into
   the same transaction, and a throwing handler fails the write. EventIngestion's durable list is
   `[typeof(WebhookIntegrationRevokedV1)]` (`EventIngestionInfrastructureModule.cs:60`).
6. **Spec number 445.** 440 (`fix/1995-narrow-default-roles`, unmerged) and 441-444 are taken across `origin/develop`,
   every local and remote branch, and all six worktrees. Open PRs #3129 and #3091 are ADR-only. **Collision risk:**
   worktree `sse-3064` (`fix/3064-health-sweep-log`) is at the same base with no spec yet and could also claim 445 —
   re-check before push.

### What "returned only after the audit row is committed" means here

The owner's comment says the 201 is returned only after the audit row is committed. ADR-0185 *Consequences* fixes what
the mechanism can deliver: *durable before disclosure, not recorded before disclosure.* The **outbox envelope** for
`WebhookIntegrationRegisteredV1` is committed in the same transaction as the integration row, before the 201 and its
token are written. The `audit_events` row in AuditObservability is written when the envelope is delivered — eventually
consistent, and a broker outage delays it but cannot drop it. This spec implements the former and does not claim the
latter. **Marked as interpretation, not a new decision.**

## User story (P1, the only one)

**As** a security officer, **I want** every webhook credential that `POST /webhook-integrations` hands out to leave
exactly one audit record naming the integration, its plant and the operator who created it, and never the credential,
**so that** I can answer "who issued this bearer token, when, for which plant" from the audit trail alone.

### Acceptance scenarios

```gherkin
Scenario: happy — a registration is audited, once, with the operator as actor
  Given operator op-3@munich.test holding fab "munich" and scope sse.webhooks.write
  When they POST /webhook-integrations { name: N, defaultKind: "WebhookAlarm" }
  Then the response is 201 with the identifier, the name and the token
  And exactly one audit_events row exists with event_kind "WebhookIntegrationRegisteredV1",
      resource kind "webhook-integration" and resource identifier N
  And its actor is the operator's subject, its fab is "munich"
  And its payload carries the integration identifier, N, "WebhookAlarm" and the registration time
  And neither the token nor its base64 SHA-256 hash appears anywhere in the row

Scenario: conflict — a taken name issues nothing and audits nothing new
  Given an integration named N already exists (audited once)
  When any operator POSTs /webhook-integrations with name N
  Then the response is 409 WEBHOOK_INTEGRATION_NAME_TAKEN
  And the count of WebhookIntegrationRegisteredV1 rows for N is still exactly one

Scenario: bad request — refused input audits nothing
  Given a multi-fab operator who names no fab, or a body with an invalid name
  When they POST /webhook-integrations
  Then the response is 400 (EVENT_FAB_REQUIRED / WEBHOOK_INVALID_INPUT)
  And no WebhookIntegrationRegisteredV1 row exists for that name

Scenario: auth — a fab the caller does not hold
  Given op-dresden@dresden.test, who does not hold "munich"
  When they POST /webhook-integrations?fabId=munich
  Then the response is 403
  And no WebhookIntegrationRegisteredV1 row exists for that name

Scenario: failure — the publish fails, so nothing is disclosed
  Given the domain-event handler's publish throws inside SaveAsync
  Then the integration row is not committed, no token is returned, and the request fails
```

## Functional requirements

- **FR-001** A successful registration publishes exactly one `WebhookIntegrationRegisteredV1` carrying the integration
  identifier, name, default kind and registration time, with `Metadata.Fab` = the integration's fab and
  `Metadata.Actor` = the registering operator's subject.
- **FR-002** The event carries **no** token and **no** token hash — neither the domain event nor the integration event
  has a field that could hold either (ADR-0185 decision 4).
- **FR-003** The publish is captured in the registration's own transaction and the type is on EventIngestion's durable
  list, so the envelope is committed before the 201 and its token are returned (ADR-0185 decision 3).
- **FR-004** A refused registration (400, 403, 409, 401) publishes nothing.
- **FR-005** AuditObservability records the event as one row against `ResourceKind.WebhookIntegration`, keyed on the
  integration name, alongside the Rotated and Revoked rows for the same integration.
- **FR-006** The HTTP contract of `POST /webhook-integrations` is unchanged (status codes, body, Location).

## Independent end-to-end test procedure

On the Aspire stack (CI; integration tests are CI-only on this machine): register an integration as a Munich operator,
poll `audit_events` for `event_kind = 'WebhookIntegrationRegisteredV1' AND resource_identifier = <name>`, assert one
row, its `actor_identifier`, fab and payload fields, and that neither the returned token nor its hash occurs in the
row's text. Then provoke 409, 403 and 400 for fresh names, register a positive-control integration afterwards, wait
for the control's row, and assert the refused names still have zero rows (409's name: still one). Phase 5 additionally
reads the EventIngestion module's durable registration and the dispatch-before-commit path in `SaveAsync`.

## Locked choices

- New contract `Shared.Contracts/EventIngestion/WebhookIntegrationRegisteredV1` — additive, `V1` (ADR-0073).
- No migration, no new column: the actor rides the domain event and `EventMetadata.Actor`; the aggregate does not
  persist it (not asked for; `RegisteredEventType` does, the webhook aggregate does not — recorded, not changed).
- No new package, AppHost resource, scope, endpoint or Idempotency-Key.

## Latency budget

**N/A** — admin path, off the event→overlay path (constitution §IV; ADR-0185 decision 5 applies to every durable type).
