# Spec 318 — The rotate that forgot the revoke

**Issue:** [#2628](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2628)
— *Rotating an already-revoked webhook integration re-enables it silently, or mints
a misleading 502.* Filed at spec 264's gate (`specs/264-the-revoke-keycloak-never-hears/spec.md`
§3, row "Rotate after revoke").

**Branch:** `fix/2628-rotate-checks-revocation` (cut from `origin/develop` at `ba37ca5a`).
**Lane:** autonomous (ADR-0144). Feature-level issue #2628 already exists. Check that it is on
Project #13 by `content.url`, not by number.

**Spec number.** 318. On 2026-10-08, `origin/develop`'s highest was 315. The local branch
`feat/316-operator-mfe-shell-and-first-remote` claims 316. Three local branches each claim 317
(`the-outage-that-held-the-boot`, `the-same-fifteen-seconds-everywhere`,
`the-unknown-a-source-holds`). **Re-check before opening the PR**, and again after every
parked-PR merge.

**Decision taken before this spec (user, 2026-10-08, issue comment).** Identity gets a
**read-only cross-context query** into EventIngestion's revocation state before it allows a
rotation. It does **not** move the revoke check into EventIngestion's own flow. This spec
implements that decision. It does not reopen it.

**ADRs and constitution sections referenced:**

- **Constitution §III** (bounded-context isolation, ADR-016). Identity may not reference
  EventIngestion. The query is a **runtime HTTP call to EventIngestion's already-published
  API**. No project reference is added, and no type crosses the boundary (plan §2).
- **Spec 017 plan §III**: the precedent this spec follows exactly. That plan recorded a
  bounded exception: a synchronous call on the write path only, carrying the caller's own
  token, to ask a question only the other context can answer. Spec 017 settled the
  mechanism (`CameraCatalogFabGuard` + `CallerTokenForwardingHandler`). This spec reuses it
  in Identity.
- **Constitution §VIII** (safe by default at trust boundaries). If the revocation state
  cannot be determined, the rotation is refused (fail closed, §4).
- **ADR-0047 / ADR-0089** (`Result<T, Error>`, `ApiError` with status). Two new
  `RotateWebhookClientError` variants.
- **ADR-0113** (two-layer optimistic concurrency). The new check runs **before** Layer 1,
  so a revoked integration answers *revoked* whichever precondition the caller sent.
- **ADR-0143** (only idempotent methods are retried). The new call is a `GET`, so the
  standard resilience handler retries it with no opt-in.
- **ADR-0142** (`Idempotency-Key`). Unchanged. A refusal is `IdempotentOutcome.NothingCreated`
  and is not stored for replay.
- **ADR-0040 / ADR-0073** (integration events). No new event. `WebhookIntegrationRevokedV1`
  (spec 264) is unchanged and still disables the client asynchronously.
- **ADR-0103** (integration tests on the Aspire fixture), **ADR-0139 / ADR-0144** (red
  first; phase-4a colour), **ADR-0036** (smallest change), **ADR-0105** (`Ensure.That`),
  **ADR-0109** (`[P]`).

**No new ADR.** See §5. A non-blocking recommendation for the human is recorded there.

**Latency budget (§IV): N/A.** See §6.

---

## 1. The issue's premise, re-checked on this tree (`ba37ca5a`, 2026-10-08)

The issue was filed on 2026-09-26. **One half of its premise is stale, and the defect it
describes has become worse, not better.**

| Claim in #2628 | On this tree |
|---|---|
| Defect 1: a never-rotated, revoked integration can be rotated, which mints a fresh **enabled** Keycloak client | **True.** `RotateWebhookClientCommandHandler.cs:56-57` looks only in Identity's own `registered_clients`. Nothing on the path asks EventIngestion. With `If-None-Match: *` the create branch (`:104-143`) calls `CreateClientAsync` and registers the row. |
| Defect 2: `GetWithinFabAsync` excludes disabled rows, so a rotated-then-revoked integration takes the create branch | **True.** `RegisteredClientRepository.cs:49-53` filters `DisabledAt == null`. |
| …and Keycloak then refuses with `KeycloakClientAlreadyExistsException`, mapped to **502 `KEYCLOAK_UNAVAILABLE`** | **Stale, twice over.** (a) `34d6f112` (#2749) added an explicit `catch (KeycloakClientAlreadyExistsException)` (`:145-157`) that maps to **409 `RESOURCE_ALREADY_EXISTS`**, so the 502 no longer happens. (b) More importantly, **`def5620b` / `6e6d26cc` (#2728) changed `HttpKeycloakAdminClient.CreateClientAsync`** (`:78-90`, `IsReplaceableDisabledClient` `:228-246`). A **disabled** client with the same `sse.kind` and `sse.fab` is now **deleted and recreated**, not refused. A webhook client that spec 264 disabled meets exactly those conditions. |
| **So today, defect 2 is:** | Rotating a rotated-then-revoked integration with `If-None-Match: *` **deletes the disabled Keycloak client, creates a fresh enabled one, registers a new row and returns 200 with a working secret.** This is the same silent re-enable as defect 1. With `If-Match: "<old version>"` the caller instead gets **412 `WEBHOOK_CLIENT_NOT_FOUND`**, inviting them to "send If-None-Match: * to create it", which leads straight into the re-enable. |
| A third mode the issue does not name | **True, found while reading.** Spec 264's disable is **eventual**. It is asynchronous over RabbitMQ, and spec 264 §3 records that it may dead-letter indefinitely during a Keycloak outage. Until it lands, the row is still enabled. A rotation with `If-Match` then takes the **rotate** branch and rolls a fresh secret for a revoked integration (`:83-102`). |

**Consequence for the design: both bugs have one cause and one fix.** In every mode, the
handler mints or rolls a credential without asking whether the integration is revoked. The
fix is a single check, placed after the integration name is parsed and **before** the
local lookup and both Layer-1 branches. It refuses with a meaningful error. It closes
defect 1, both forms of defect 2 and the third mode. No separate change to
`GetWithinFabAsync` or to the error mapping is needed: the misleading 412, and the
re-enable, become unreachable for a revoked integration. Changing `GetWithinFabAsync`'s
disabled-row filter would be wrong. The filter is shared by `DisableWebhookClient`,
`DisableKiosk`, `DisableDevice` and the kiosk/device endpoints, and disabled rows releasing
the client id is a deliberate rule (spec 005 pattern, `RegisteredClientRepository.cs:28-29`).

**What EventIngestion already exposes.** `GET /webhook-integrations?fabId={fab}&includeRevoked=true`
(`WebhookIntegrationsEndpoints.cs:48-53, 120-146`) returns every integration in the fab,
including revoked ones, each with `name` and `revokedAt`. It requires `sse.webhooks.write`,
which is **the same scope the rotate endpoint requires** (`WebhookRotationEndpoints.cs:37-38`).
It is fab-scoped by the same `IFabAuthorizationGuard` contract. Names are **globally unique,
revoked rows included** (`ux_webhook_integrations_name`, `WebhookIntegrationConfiguration.cs:83-85`;
`RegisterWebhookIntegrationCommandHandler.cs:22-31`), so a fab holds at most one row per name.
**No new EventIngestion endpoint is needed** (plan §2).

## 2. User stories

### US1 (P1): A revoked webhook integration cannot be rotated back to life

Before Identity creates or rolls a webhook client, it asks EventIngestion about the
integration, using the operator's own token and the rotation's fab. If the integration is
revoked, the rotation is refused with **409 `WEBHOOK_INTEGRATION_REVOKED`**. Keycloak is
not called and no row is written. If EventIngestion cannot answer, the rotation is refused
with **502 `WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE`** (fail closed, §4). An active
integration, and a name EventIngestion has never registered in that fab, rotate exactly as
today (§3, "Not registered").

**Why P1:** it is the defect, and it can be observed on its own. Revoke, rotate, then ask
Keycloak whether a usable client exists.

```gherkin
Background:
  Given the running Aspire stack
  And an operator holding /fabs/munich with sse.webhooks.write

Scenario: control — an active integration still rotates (unchanged)
  Given webhook integration "a" registered in munich via POST /webhook-integrations (event-ingestion)
  When the operator sends POST /webhook-integrations/a/rotate {fabId: munich} with If-None-Match: * (identity)
  Then the response is 200 with clientId "webhook-a" and a clientSecret
  And a client_credentials grant for "webhook-a" with that secret succeeds

Scenario: defect 1 — a never-rotated, revoked integration is refused, and nothing is minted
  Given webhook integration "n" registered in munich and never rotated
  And "n" revoked via DELETE /webhook-integrations/n with If-Match (event-ingestion)
  When the operator sends POST /webhook-integrations/n/rotate {fabId: munich} with If-None-Match: *
  Then the response is 409 with code WEBHOOK_INTEGRATION_REVOKED
  And Keycloak has no client "webhook-n"
  And Identity has no registered_clients row for "webhook-n"
  And no WebhookIntegrationRotatedV1 is published

Scenario: defect 2 (create intent) — a rotated, revoked, disabled integration is not recreated
  Given webhook integration "r" registered in munich and rotated (version V, secret S)
  And "r" revoked, and Keycloak reports "webhook-r" enabled=false within 20 s   # spec 264 path, control
  When the operator sends POST /webhook-integrations/r/rotate {fabId: munich} with If-None-Match: *
  Then the response is 409 with code WEBHOOK_INTEGRATION_REVOKED
  And Keycloak still reports "webhook-r" enabled=false, with the same Keycloak id as before   # not deleted and recreated (#2728 path)
  And no new registered_clients row exists for "webhook-r"

Scenario: defect 2 (rotate intent) — the misleading 412 becomes the meaningful 409
  Given "r" as above (rotated at version V, revoked, disabled)
  When the operator sends POST /webhook-integrations/r/rotate {fabId: munich} with If-Match: "V"
  Then the response is 409 with code WEBHOOK_INTEGRATION_REVOKED   # develop: 412 WEBHOOK_CLIENT_NOT_FOUND

Scenario: third mode — revoked, but Identity has not yet disabled the client
  Given a webhook client "webhook-p" whose registered_clients row is still enabled at version V
  And EventIngestion reports integration "p" revoked
  When RotateWebhookClientCommand("p", munich, …, ExpectedVersion: V) is handled
  Then it fails with WEBHOOK_INTEGRATION_REVOKED
  And RotateClientSecretAsync is not called, the row's version stays V and LastRotatedAt does not move
  # unit-level: the window between the revoke and the asynchronous disable is not reliably reproducible end to end

Scenario: conflict — the revoked check precedes the version checks
  Given EventIngestion reports "p" revoked and the local row is at version 4
  When the command carries ExpectedVersion 3 (stale) or None (create intent against an existing row)
  Then it fails with WEBHOOK_INTEGRATION_REVOKED, not WEBHOOK_CLIENT_STALE or WEBHOOK_CLIENT_ALREADY_EXISTS

Scenario: fail closed — EventIngestion cannot answer
  Given the status lookup cannot determine the integration's state
        (EventIngestion unreachable, a non-2xx answer, a timeout after the standard resilience retries,
         or a body missing "name"/"revokedAt")
  When a rotation for "a" is handled
  Then it fails with 502 WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE
  And Keycloak is not called, no row is written or changed, and nothing is published
  And Identity logs a Warning naming the integration and the reason

Scenario: fail closed — a caller cancellation is not reported as an outage
  Given the request's CancellationToken is cancelled while the lookup is in flight
  Then OperationCanceledException propagates (not mapped to 502), as on every other path of this handler

Scenario: not registered — a name EventIngestion does not hold in this fab is unchanged
  Given no webhook integration "u" exists in munich in EventIngestion
  When the operator rotates "u" with If-None-Match: *
  Then the outcome is exactly today's: 200 and a fresh client (see §3, deferred)

Scenario: bad request — an invalid integration name is refused before any outbound call
  Given the name "Bad Name!" that does not form a valid ClientId
  When the rotation is handled
  Then it fails with 400 WEBHOOK_INVALID_INPUT (unchanged)
  And the status lookup is not called

Scenario: auth — the query sees only what the caller can already see
  Given the lookup forwards the incoming request's Authorization header unchanged
  And asks for fabId = the rotation's fab only
  Then EventIngestion applies its own sse.webhooks.write scope and fab guard to the same principal
  And an integration "d" revoked in fab dresden is invisible to a munich rotation of "d"
      (the lookup answers NotRegistered, and the rotation proceeds to today's behaviour —
       including the existing 409 RESOURCE_ALREADY_EXISTS if dresden's disabled client holds the client id)

Scenario: auth — the rotate endpoint's own guards are unchanged
  Given an operator without sse.webhooks.write, or not holding the named fab
  When they POST /webhook-integrations/{name}/rotate
  Then the response is 401/403 as today, and no lookup is made (the guard runs at the endpoint, before the handler)

Scenario: idempotency — a refusal is not stored for replay
  Given a rotation with Idempotency-Key K that was refused with WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE
  When the same request is retried with K after EventIngestion recovers
  Then the handler runs again (NothingCreated is not replayed — ADR-0142, unchanged)
```

## 3. Scope

### In this PR

- Identity Application: a port, `IWebhookIntegrationStatusLookup`, and a
  `WebhookIntegrationStatus` enum (`Active`, `Revoked`, `NotRegistered`, `Unverifiable`).
  `RotateWebhookClientCommandHandler` consults it. Two new `RotateWebhookClientError`
  variants and their `[LoggerMessage]`s.
- Identity Infrastructure: `EventIngestionWebhookIntegrationStatusLookup`, a typed
  `HttpClient` against `http://event-ingestion`, and a copy of
  `CallerTokenForwardingHandler`. Both are registered in `IdentityInfrastructureModule`.
- AppHost: `identity.WithReference(eventIngestion)`. This is a reference only, not a
  `WaitFor` (plan §7).
- Tests per plan §8, and the new integration class added to a CI shard filter.

### Not in this PR, and why

| Item | Where | Why not here |
|---|---|---|
| **Not registered.** Identity will mint a client for a name EventIngestion never registered in that fab. | Follow-up issue, **to be filed at the gate**, if wanted | This gap predates this spec and is not #2628. Refusing it would change behaviour beyond the issue. It would also break at least four integration test classes that rotate names they never register (`CrossFabWebhookRotationIntegrationTests`, `IdempotencyKeyReuseIdentityIntegrationTests`, `RegisteredClientConcurrencyIntegrationTests`, `BruteForceLockoutIntegrationTests`). The lookup already returns `NotRegistered`, so the follow-up is a one-line policy change plus those tests. **Stated assumption, for the gate reviewer.** |
| Hoisting `CallerTokenForwardingHandler` into `ServiceDefaults` | Follow-up refactor, if wanted | Moving it would refactor LayoutComposition inside a bug fix (ADR-0036). A 10-line copy now has two instances; that is the point at which hoisting becomes worth a separate, behaviour-preserving change. |
| A dedicated single-resource `GET /webhook-integrations/{name}` on EventIngestion | Nowhere now | The list endpoint already answers the question with the same scope and fab guard. A fab holds tens of integrations, not thousands. A new endpoint would be new EventIngestion surface for no new capability (plan §2). |
| Moving `WebhookIntegrationDto` into `Shared.Contracts` | Nowhere now | Spec 017's adapter reads CameraCatalog's JSON by field name rather than sharing a type. This spec does the same. The parse fails closed on a missing field (§4), and the integration test exercises the real JSON (plan §2). |
| TOCTOU: a revoke committed between the lookup and the Keycloak call | Accepted residual | The window is one Keycloak round trip. It still ends in a disabled client: spec 264's `WebhookIntegrationRevokedV1` arrives after the revoke and disables whatever the rotation created or rolled. The exception is the create branch's sub-window between `CreateClientAsync` and the row's `SaveAsync`. There, a message arriving first finds no row and settles. Closing that needs a distributed lock across two contexts, which is out of proportion for an operator action that needs `sse.webhooks.write`. |
| Idempotent **replay** of a rotation that succeeded *before* the revoke | Unchanged | A replay returns the stored answer without running the handler (ADR-0142). The secret it returns belongs to a client spec 264 has since disabled, so it mints nothing. |
| Spec 264 §3's dead-letter residual | Unchanged, still accepted there | Out of scope. This spec does narrow its blast radius: a revoked integration whose disable dead-lettered can no longer be rotated. |
| UI | None | No management-web screen calls `/rotate` (`apps/` holds no caller). Problem details carry the new codes as they carry the existing ones. |

## 4. Fail open or fail closed: **closed**, and why this is not a judgement call

If the lookup cannot determine the state, the rotation is **refused** with 502
`WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE`. Causes include unreachable, non-2xx, a timeout after
the standard resilience retries, and malformed JSON. A human does not need to decide this,
for four reasons:

1. **Failing open re-creates the defect.** "Could not check, so proceed" is the exact
   behaviour #2628 exists to remove: an enabled credential minted for a possibly-revoked
   integration. A guard that disappears when its dependency is slow is not a guard.
2. **Failing closed costs almost nothing.** Rotation is a rare, manual admin action. A
   refused rotation leaves the **current credential working**, so the integration keeps
   delivering events. The operator retries when EventIngestion is back. Nothing on the event
   path or the latency path depends on it.
3. **This handler already behaves this way for its other dependency.** A Keycloak outage is
   a 502 `KEYCLOAK_UNAVAILABLE` today. A second upstream with the same failure shape is
   consistency, not a new policy.
4. **The precedent and the constitution both say closed.** Spec 017 FR-015 refuses an
   unresolvable camera rather than accepting it, and its `CallerTokenForwardingHandler`
   documents that a missing token must produce a refusal ("the failure direction is
   closed"). Constitution §VIII is "safe by default at trust boundaries".

The only competing answer would be "fail open because availability matters". It does not
survive point 2: what is unavailable is the *rotation*, never the integration.

## 5. Why no new ADR

The user chose the mechanism. Every element of the shape is spec 017's bounded §III
exception, applied as written:

1. **No new credential.** The call carries the **operator's own token**, forwarded from the
   rotate request. That token already holds `sse.webhooks.write` and the rotation's fab,
   which are exactly what EventIngestion's endpoint demands. No service account, unlike
   ADR-0116.
2. **Write path only.** Only the rotate command calls it. An EventIngestion outage stops
   *rotation* and nothing else (§4). The kiosk/device paths, reads and event ingestion are
   untouched.
3. **It answers a question this context must not answer itself.** Revocation is
   EventIngestion's state. A local Identity copy fed by `WebhookIntegrationRevokedV1` would
   be eventually consistent, which is exactly the window the third mode lives in (§1). It
   would also duplicate a rule EventIngestion owns.
4. **Published HTTP API, no shared types, no project reference.** The same terms spec 017
   recorded as "does not breach ADR-0016".

**Recommendation for the human, non-blocking.** This bounded exception now has **two**
instances: LayoutComposition→CameraCatalog and Identity→EventIngestion. It is recorded only
in spec plans, not in an ADR or in constitution §III, whose rule text still reads
"communication only through `Shared.Contracts`". Codifying "a synchronous read on a write
path, with the caller's token, failing closed" as an ADR would stop a third instance from
having to rediscover it. **The autonomous lane may not write ADRs (ADR-0144), so this is
flagged, not done.**

## 6. Latency budget (§IV)

**N/A.** No leg of event arrival → overlay rendered is touched. The new call is made only
on `POST /webhook-integrations/{name}/rotate`, an operator action. Webhook ingestion
(`POST /events/webhook/{name}`) is unchanged. §VII's dashboard rule: N/A.

## 7. Phase-4a colour: **red**

This change alters behaviour: it adds new refusals and new error codes on an existing
endpoint. The load-bearing reds are the **integration facts against the real stack**
(plan §8.1), which compile on `develop` because they use HTTP only:

- defect 1: `200` on develop, expected `409 WEBHOOK_INTEGRATION_REVOKED`;
- defect 2, create intent: `200` on develop with the client re-enabled through #2728's
  delete-and-recreate, expected `409` with the client still disabled;
- defect 2, rotate intent: `412 WEBHOOK_CLIENT_NOT_FOUND` on develop, expected `409`.

Unit reds on types that do not exist yet are compile failures. They count, but on their own
they would be a weak red.

**Existing tests.** Ten existing construction sites of `RotateWebhookClientCommandHandler`
(three files, plan §8.3) gain one constructor argument, a fake that answers `Active`. **No
assertion in them changes.** If one has to change, that is evidence the behaviour moved, and
the work must stop and escalate rather than adjust the assertion. Every existing rotation
integration test passes unmodified: their names are either registered and active, or
unregistered, and both proceed as today.

## 8. Independent end-to-end test procedure

1. `aspire run` (one stack per machine). Get an operator token for `admin` (`/fabs/munich`,
   `sse.webhooks.write`).
2. **Control.** `POST {event-ingestion}/webhook-integrations {"name":"e2e-2628-a","defaultKind":"WebhookAlarm"}`
   returns 201. `POST {identity}/webhook-integrations/e2e-2628-a/rotate {"fabId":"munich"}`
   with `If-None-Match: *` returns 200 with a secret.
3. **Defect 1.** Register `e2e-2628-n`. Read its `version` from
   `GET {event-ingestion}/webhook-integrations`. Send `DELETE /webhook-integrations/e2e-2628-n`
   with `If-Match` (200). Rotate it with `If-None-Match: *`. Expect **409**
   `WEBHOOK_INTEGRATION_REVOKED`. Keycloak admin
   `GET /admin/realms/smart-sentinel-eye/clients?clientId=webhook-e2e-2628-n` returns `[]`.
   On `develop`: 200 and an enabled client.
4. **Defect 2.** Register `e2e-2628-r` and rotate it (note `version` and the Keycloak client
   `id`). Revoke it, then wait until Keycloak shows `enabled:false`. Rotate with
   `If-None-Match: *`: expect **409**. Keycloak still shows `enabled:false` and the **same
   `id`**. Rotate with `If-Match: "<version>"`: expect **409**. On `develop`: 200 with a new
   `id` and `enabled:true`, and 412 respectively.
5. **Fail closed.** Stop the `event-ingestion` resource from the Aspire dashboard, then
   rotate `e2e-2628-a` with its current `If-Match`. Expect **502**
   `WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE`. Expect a Warning in Identity's structured logs
   naming the integration, and the old secret still mints a token. Start `event-ingestion`
   again. The same rotation then returns 200.
6. *Traces*: the rotate request's trace shows an outbound `GET /webhook-integrations` span
   to `event-ingestion`, carrying `fabId=munich&includeRevoked=true`.

## 9. Success criteria

- **SC1:** a rotation of a revoked integration, never rotated or rotated and disabled, with
  either precondition, answers 409 `WEBHOOK_INTEGRATION_REVOKED`. Keycloak holds no new or
  re-enabled client for it (integration test, red on `develop`).
- **SC2:** when the lookup answers `Unverifiable`, the handler answers 502
  `WEBHOOK_INTEGRATION_STATUS_UNAVAILABLE` and calls neither Keycloak nor the repository's
  write methods (unit). The adapter maps transport, status and parse failures to
  `Unverifiable` and lets caller cancellation propagate (unit, stub handler).
- **SC3:** active and not-registered integrations rotate exactly as before. The existing
  rotation unit and integration facts pass with no assertion changed.
- **SC4:** `Architecture.Tests` are green, including `BoundaryTests`: no Identity→EventIngestion
  project or assembly reference.
- **SC5:** the Release build (`TreatWarningsAsErrors`), `dotnet format --verify-no-changes`
  and the Identity coverage gates (Application ≥ 80%) hold.
