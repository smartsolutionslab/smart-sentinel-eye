# Spec 365: The event-ingestion endpoints under thirty lines

**Feature Branch**: `refactor/2930-eventingestion-s138` (cut from `origin/develop` at `c5cde739`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Four judgement calls
are marked explicitly (§6).

**Input**: Issue [#2930](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2930),
*ADR-0084 S138: split the long methods in EventIngestion.Api*.

**Lane.** Autonomous (ADR-0144). Behaviour-preserving, so Phase 4a is **characterisation, observed
green** (constitution §Testing; CLAUDE.md "Refactors stay green"). Engineer: **backend-engineer**.
**Security-sensitive**: the webhook ingest path authenticates an anonymous caller in the handler
(§2 US1, FR-004). Phase 6 runs `/security-review` as well as `/code-review`.

**Spec number.** 365. Checked 2026-10-15 after `git fetch`: `origin/develop` highest 362; highest
across every remote head (`git ls-tree`) 364 (`#2933`, scenario simulator). 363 is spec 363 for
#2929 on the unpushed `refactor/2929-layout-s138`, which this number avoids. Re-check before the PR
is opened.

**ADRs.** ADR-0084 (code metrics: S138 <= 30 lines, S107 <= 4 parameters, advisory `warning`),
ADR-0144 (autonomous lane; phase 4a colours; the lane may not weaken a gate), ADR-0070 (Minimal
APIs), ADR-0036 (smallest change), ADR-0105 (`Ensure.That`), ADR-0113 (`If-Match` read before the
lookup), ADR-0142 (idempotency scope = route, caller, fab, fingerprint), ADR-0089 (`ApiError` →
problem), ADR-0052/ADR-0103 (xUnit + Shouldly + Moq; no Testcontainers). No new ADR: this changes
shape, not a decision.

**Sibling.** Spec 363 (#2929, LayoutComposition.Api) solves the same problem with the same pattern.
It is not merged, so this branch does not depend on it (§6 J2).

---

## 1. What was measured (premise verified, not assumed)

```sh
dotnet build src/EventIngestion/Api -c Release --no-incremental
```

Result on `c5cde739`: **12 S138 hits in `src/EventIngestion/Api`, exactly the issue's 12, same
methods, same line counts.** Nothing is stale.

| # | File:line | Method | Lines |
|---|---|---|---|
| 1 | `EventSourcesEndpoints.cs:37` | `MapEventSourcesEndpoints` | 54 |
| 2 | `EventSourcesEndpoints.cs:104` | `Declare` | 40 |
| 3 | `EventSourcesEndpoints.cs:208` | `Change` | 32 |
| 4 | `EventTypesEndpoints.cs:27` | `MapEventTypesEndpoints` | 42 |
| 5 | `EventTypesEndpoints.cs:81` | `Register` | 38 |
| 6 | `EventsEndpoints.cs:15` | `MapEventsEndpoints` | 70 |
| 7 | `EventsEndpoints.Reads.cs:16` | `ListEvents` | 40 |
| 8 | `EventsEndpoints.Writes.cs:27` | `IngestManual` | 57 |
| 9 | `EventsEndpoints.Writes.cs:158` | `IngestWebhook` | 34 |
| 10 | `EventsEndpoints.Writes.cs:229` | `AuthenticateWebhookAsync` | 39 |
| 11 | `WebhookIntegrationsEndpoints.cs:23` | `MapWebhookIntegrationsEndpoints` | 32 |
| 12 | `WebhookIntegrationsEndpoints.cs:67` | `Register` | 31 |

The same build prints **14 S107 hits** in this project's hand-written files (13 endpoint files + one
in `Log.cs`). They are out of scope (the issue: the `[AsParameters]` question) and **must not grow**.
This includes `AuthenticateWebhookAsync` (7 parameters), whose signature stays as it is. Generated
migrations are excluded by the issue.

## 2. User story

### US1 (P1): EventIngestion.Api's endpoint methods fit ADR-0084's 30-line limit with no observable change

Operators, webhook senders, the gateway and the generated OpenAPI client see exactly the same API
before and after. That means the same 15 routes, verbs, names, summaries, tags, authorization
policies, `AllowAnonymous` and declared statuses. Every request gets the same status, problem body
and `Location`, in the same refusal order. The repositories, the fab guard, the idempotency store
and the authentication service are called with the same arguments, or not at all, exactly as before.

**Why P1 / why one slice.** It is the whole issue: 12 methods across 6 files of one project, one
layer. That is above the ~10-method mark, so it ships as **one PR in five commit groups, one per
file family** (plan §6). Each group builds and passes on its own. The security-sensitive webhook
group comes last and is separately reviewable.

**Independent test.** Characterisation tests (§4) are observed green on `c5cde739` before any
production edit. They then stay green **unmodified** after each refactor commit. Afterwards the §1
build prints zero S138 hits in `EventIngestion.Api` and still exactly the 14 S107 hits.

#### Acceptance scenarios

Route table (for a refactor, the conflict case is "the shape moved"):

```gherkin
Scenario: The route table is identical after the refactor
  Given the four EventIngestion Map*Endpoints methods are mapped in process on develop c5cde739
  And their canonical route-table rendering is captured as a literal baseline
  When the same rendering is taken after the refactor
  Then it equals the baseline line for line
    # pattern, verbs, name, summary, tags, authorization data, AllowAnonymous, produces metadata
    # in order, accepts metadata, handler method name and parameter list (names, types, attributes)
```

Happy path (the handler's outcome reaches the wire unchanged):

```gherkin
Scenario: A valid manual event is stored as sent, in the resolved fab
  Given an operator holding only fab "dresden" with storage ready
  When they POST /events/manual with deviceId, kind, occurredAt and payload and no Idempotency-Key
  Then the response is 201 with Location "/events/{id}"
  And the stored envelope carries fab "dresden", Source manual, and that deviceId, kind, occurredAt and payload

Scenario: A delivery with the integration's own static token is stored under the integration's name
  Given a StaticHash integration "line-7" owned by fab "dresden"
  When its token is presented as "Bearer <token>" on POST /events/webhook/line-7?fabId=dresden with no kind
  Then the response is 201
  And the envelope carries Source webhook, device "line-7", the integration's default kind and the clock's time
```

Bad request and refusal order:

```gherkin
Scenario: The manual write reads the Idempotency-Key before anything else
  When a caller POSTs /events/manual with a malformed Idempotency-Key and an invalid body
  Then the response is the key refusal
  And neither the revocation lookup nor the fab guard was consulted

Scenario: Declare and Register parse the body before the Idempotency-Key
  When a caller POSTs /event-sources (or /event-types) with an invalid body and a malformed key
  Then the response is 400 SOURCE_MODE_INVALID_INPUT (or EVENT_TYPE_INVALID_INPUT), not the key refusal

Scenario: Change reads If-Match before parsing the route value
  When a caller PUTs /event-sources/{source}/mode with an invalid source and no If-Match
  Then the response is 428, not 400

Scenario: A list filter is parsed before the fab is resolved
  When a caller with no usable fab GETs /events?source=<invalid>
  Then the response is 400 EVENT_LIST_INVALID_INPUT
```

Conflict / precondition:

```gherkin
Scenario: Changing an undeclared pair is the handler's 404
  Given a valid If-Match, source and mode in the caller's fab, and no declared row
  When the caller PUTs /event-sources/{source}/mode
  Then the response is the handler's 404 problem, and the repository was asked for that fab and source
```

Auth (the security-relevant core):

```gherkin
Scenario Outline: Every webhook refusal is the same 401
  When a delivery arrives with <condition>
  Then the response is 401 with the same body as for an unknown integration
  And <not-called> was never called
  Examples:
    | condition                                       | not-called                              |
    | no Authorization header                         | the integration repository              |
    | "bearer <token>" (wrong case)                   | the integration repository              |
    | a malformed integration name                    | the integration repository              |
    | an unknown integration                          | the token check                         |
    | a revoked integration                           | the token check (and it is logged once) |
    | another plant's fabId with the right token      | the token check / JWT authentication    |
    | a wrong static token                            | storage readiness                       |
    | a JWT without sse.events.write / wrong azp / no /fabs/{fabId} group | storage readiness |

Scenario: A revoked integration's refusal is logged at most once per window
  Given a revoked integration
  When two deliveries for it arrive within one minute, the first naming a mismatched fabId
  Then exactly one log record is written, carrying the integration's own fab

Scenario: A manual write by a revoked webhook integration's own JWT is refused first
  Given a caller whose azp is a revoked integration's Keycloak client
  When they POST /events/manual with a valid body
  Then the response is 401 and the fab guard was never consulted

Scenario: An idempotent create is scoped to route, caller, fab and body
  Given an operator in "dresden" sending POST /events/manual (or /event-sources, /event-types) with Idempotency-Key "k1"
  Then the idempotency store is consulted with ("POST /events/manual" | "POST /event-sources" | "POST /event-types", that operator, "dresden", fingerprint of the body)

Scenario: Registering a webhook integration never consults the idempotency store
  When an admin POSTs /webhook-integrations with an Idempotency-Key header
  Then the response is 201 with the plaintext token and the idempotency store is never called
```

Scope policies (the 401/403 that `RequireAuthorization` produces) are middleware, not handler code.
They are pinned by the route-table rendering and by the existing CI integration tests (§4.3), not
driven in process.

## 3. Functional requirements

- **FR-001** Every method in §1 is at most 30 S138-lines. The §1 build prints no S138 hit in
  `src/EventIngestion/Api` (migrations excepted) and exactly the same 14 S107 hits, by file and
  method (line numbers may move). No other new warning appears anywhere in the build. Every new
  helper therefore takes **at most 4 parameters**.
- **FR-002** No route, verb, name, summary, tag, authorization policy, `AllowAnonymous`,
  `Produces*`/`Accepts` metadata, handler method name or handler parameter list changes, and
  registration order stays the same (§4.1 baseline equal).
- **FR-003** Every request gets the same status, problem body and `Location` as before, in the same
  refusal order:
  - `IngestManual`: key → revoked-webhook caller → fab → storage readiness → envelope parse → idempotent store.
  - `IngestWebhook`: authenticate → storage readiness (the integration's fab) → envelope parse → store.
  - `AuthenticateWebhookAsync`: `Bearer ` prefix (ordinal) → name parse → lookup → revoked (logged once per window) → own-fab check → token by mode (SHA-256 hash match or JWT).
  - `Declare`, `Register` (event types): body parse → key → fab → idempotent dispatch.
  - `Change`: `If-Match` → parse → fab → handler.
  - `ListEvents`: filters (source, device, kind, in that order) → read fabs → handler.
  - `Register` (webhook): parse (name, then default kind) → fab → handler. No idempotency.
- **FR-004** (security) No webhook refusal becomes distinguishable from another: same status, body
  and headers. No refusal moves after a lookup it currently precedes, nor before one it currently
  follows. The bearer token and the plaintext registration token are never logged, stored or echoed
  anywhere they are not today.
- **FR-005** The tokens the source-scanning guards read in a handler body stay in that body (plan
  §3.6): `ConcurrencyHeaders.TryReadExpectedVersion` in `Change`, `IdempotentRequest.ExecuteCreateAsync`
  and `IdempotencyHeaders.TryRead` in `IngestManual`/`Declare`/`Register` (event types), and
  `StatusCodes.Status400BadRequest` in the two route-value handlers `Change` and `IngestWebhook`.
  Mappings stay in the four `*Endpoints.cs` files, on receivers named exactly as their `MapGroup`
  local (`group`, `writes`, `reads`). No new `*Endpoints.cs` file and no fourth `EventsEndpoints`
  partial file are added.
- **FR-006** No guard, existing test, suppression, threshold, `.editorconfig` or `SonarLint.xml` is
  edited. No migration, `Shared.Contracts`, Application/Domain/Infrastructure or AppHost change.

## 4. Characterisation (phase 4a, observed green before any production edit)

All new tests live in `tests/Architecture.Tests` (J2). Each problem body is asserted **exactly**
(string equality of the JSON). Each collaborator call is verified with its exact arguments, or
verified `Times.Never` where the order is the claim.

### 4.1 Route table (in process): proves FR-002

`EventIngestionRouteTableCharacterisationTests.cs` maps `MapEventsEndpoints`,
`MapEventSourcesEndpoints`, `MapEventTypesEndpoints` and `MapWebhookIntegrationsEndpoints` on an
in-process `WebApplication`. It renders all 15 `RouteEndpoint`s in data-source order and asserts
equality with a raw-string literal captured on `c5cde739`. It covers methods 1, 4, 6 and 11
completely, and the handler signatures of the other eight.

### 4.2 Endpoint behaviour (in process): proves FR-003 and FR-004

The mapped `RequestDelegate` is driven directly (no Docker). The real command handlers are used,
with Moq doubles behind them, so the extracted code and its dispatch tail are both exercised.

| Method | Cases (exact status + exact body; verified calls) |
|---|---|
| IngestManual | malformed key → key refusal, revocation repo and guard never called. Malformed key + invalid body → key refusal. Revoked azp → 401, guard never called. Unrevoked azp → proceeds. Azp longer than `KeycloakClientIdentifier.MaximumLength` → proceeds, repo never called. Multi-fab no fabId → fab refusal, storage never asked. Storage not ready → 503 `EVENT_FAB_NOT_PROVISIONED`. Invalid body + storage not ready → 503 (storage before parse). Invalid body → 400 `EVENT_INVALID_INPUT`. Keyed → scope (`POST /events/manual`, caller, fab, fingerprint) field by field. Unkeyed → 201, `Location: /events/{id}`, envelope captured field by field. |
| IngestWebhook + AuthenticateWebhookAsync | every row of the §2 outline, each with its `Times.Never`. StaticHash right token → 201 and the envelope (fab = query fabId, Source webhook, device = integration name, default kind when the body has none, the clock's time when occurredAt is absent). Body kind/occurredAt given → used. JWT mode valid → 201. JWT invalid in each of 3 ways → 401. Fab mismatch in JWT mode → `AuthenticateAsync` never called. Revoked → logged once in a window, with the integration's own fab, even when fabId mismatches; a second delivery is not logged. Unauthenticated + storage not ready → 401. Authenticated + storage not ready → 503 for the integration's fab. Invalid payload → 400 `EVENT_INVALID_INPUT`. |
| Declare | invalid source → 400. Invalid mode → 400. Invalid body + malformed key → 400 (parse first). Malformed key → key refusal. No fab → refusal. Keyed → scope (`POST /event-sources`, caller, fab, fingerprint). Unkeyed → 201, `Location: /event-sources/{source}`, command captured. |
| Change | no If-Match + invalid source → 428. Invalid source → 400. Invalid mode → 400. No fab → refusal, repo never called. Valid, no row → handler's 404, repo asked for (fab, source). |
| Register (event types) | invalid kind → 400. Invalid kind + malformed key → 400. Malformed key → key refusal. No fab → refusal. Keyed → scope (`POST /event-types`, …). Unkeyed → 201, `Location: /event-types/{kind}`. |
| ListEvents | invalid source / deviceId / kind → 400 `EVENT_LIST_INVALID_INPUT` with each VO's message. Invalid source + invalid kind → the source's message. Invalid filter + no usable fab → filter 400. No usable fab → `EVENT_FAB_REQUIRED`. `pageSize=1001` → the handler's 400 (proves pageSize reaches it). Malformed cursor → the handler's 400 naming the cursor. |
| Register (webhook) | invalid name → 400 `WEBHOOK_INVALID_INPUT`. Invalid kind → 400. Both invalid → the name's message. Multi-fab no fabId → refusal. Valid → 201, `Location: /webhook-integrations/{name}`, body `{identifier, name, token}`, the token matches the stored hash, registered into the resolved fab with that kind. With an `Idempotency-Key` header → the store is never called. |

### 4.3 Existing coverage (CI integration, Docker; the second observer)

These tests are not runnable in the lane's local loop. CI's integration shards observe them on the
PR:

- **Webhook ingest:** `WebhookBearerValidationIntegrationTests` (8: both modes, scope/azp/group,
  cross-plant), `WebhookRevocationRefusesDeliveryIntegrationTests`,
  `RevokedWebhookDeliveryIsLoggedIntegrationTests`, `CrossFabWebhookRotationEffectIntegrationTests`.
- **Manual ingest:** `ManualIngestFabScopingIntegrationTests`,
  `ManualIngestRefusesRevokedWebhookCallerIntegrationTests`, `FabStorageRefusalIntegrationTests`,
  `MissingPayloadIsRefusedIntegrationTests`, `IngestBackpressureIntegrationTests`,
  `DirectWriteHonestyIntegrationTests`, `IdempotencyKeyReuseEventIngestionIntegrationTests`.
- **Reads:** `ListEventsFilteringTests`, `EventFabScopingIntegrationTests`.
- **Sources and types:** `EventSourceModeIntegrationTests` (16),
  `EventSourceModeAuthorizationIntegrationTests`, `EventTypeRegistryIntegrationTests` (13),
  `EventTypeRegistryIdempotencyIntegrationTests` (7),
  `EventTypeRegistryAuthorizationIntegrationTests`, `EventTypeRegistryConcurrencyIntegrationTests`.
- **Webhook registry:** `WebhookRegistryFabScopingIntegrationTests`,
  `WebhookIntegrationConcurrencyIntegrationTests`.
- **Anonymous callers:** `AnonymousIngestIsRefusedTests`.

**Gaps they leave, which 4.2 closes:**

- refusal order between steps that both answer the same status (every webhook 401, key-vs-parse, If-Match-vs-parse, filter-vs-fab, storage-vs-parse);
- the `Times.Never` facts (no lookup without a bearer prefix, no token check across plants);
- the log-once window in process;
- the idempotency scope's argument order;
- the envelope's field mapping on both write paths;
- the webhook registry's "no idempotency".

## 5. Success criteria

- **SC-001** §1 build: 0 S138 in `EventIngestion.Api`'s hand-written files. The S107 set is
  unchanged (14). No new warning.
- **SC-002** §4.1 and §4.2 are observed green on `c5cde739` (output quoted in the PR), and stay
  green and byte-unmodified after each refactor commit.
- **SC-003** All of `tests/Architecture.Tests` is green, and CI's integration shards are green on the PR.
- **SC-004** `git diff --stat origin/develop` touches only:
  - the 6 Api files of §1;
  - the new test files and the `Architecture.Tests` csproj (Moq reference);
  - this spec directory.

## 6. Judgement calls (marked)

- **J1: reuse `BoundaryParse.TryParse` for parses in handlers without a route value** (`Declare`,
  `Register` ×2, `ListEvents`). Its refusal is
  byte-identical to the inline catch (title = code, detail = message, 400). Consequence: those
  bodies no longer spell `StatusCodes.Status400BadRequest`, so `RouteValueRefusalDeclarationTests`
  stops *demanding* their chains' 400. §4.1 pins every one of those declarations exactly, which is
  strictly stronger. This is the trade spec 363 J1 makes. The two route-value handlers (`Change`,
  `IngestWebhook`) keep their inline catch so the guard's subject is untouched. `IngestManual` also
  keeps its inline envelope catch, because its reduction comes from elsewhere (plan §3.4).
- **J2: an independent `EventIngestionEndpointHost`, not a stack on #2929.** Spec 363's
  `LayoutCompositionEndpointHost` is unmerged. CLAUDE.md permits a stack only when a branch
  genuinely cannot build on `develop`, and this one can. So this branch adds its own small host
  with the same shape, named `EventIngestion*`, plus its own copy of the route-table renderer.
  Both branches add the identical `<PackageReference Include="Moq" />` line to the csproj, and git
  merges an identical change on both sides cleanly. Unifying the two hosts and renderers is a
  follow-up once both have merged, not part of this PR.
- **J3: per-route mapping helpers** (as spec 363 J3), named `Map` + the route's `WithName` where it
  has one, else `Map` + verb-noun (plan §3.5). Each chain stays byte-identical.
- **J4: `AuthenticateWebhookAsync` keeps its 7-parameter signature.** Shrinking it would remove an
  S107 hit, which is in-scope-adjacent but not asked for. The smallest change is to extract the
  inside of the method and leave its contract alone.

## 7. Latency budget impact (constitution §IV)

**Leg "Event → overlay state" (≤ 200 ms):** the two ingest writes start that leg. Their runtime
work does not change: the same awaits, the same single store, the same order. The cost of the
refactor is at most a few extra synchronous calls and one closure per request, which is
nanoseconds, so the budget is not measurably touched and no re-measurement is owed. The reads,
the registry and the source/type admin endpoints: N/A.

## 8. Out of scope

- S107 (the `[AsParameters]` / DI-parameter question).
- Migrations.
- Every method under the limit today (`Undeclare`, `Retire`, `List`, `GetEvent`,
  `ListDeadLetters`, `Revoke`, `StoreOrRefuseAsync`, `ValidateJwtAsync`), even where the new helpers
  would fit them.
- Every other context's Api (#2929, #2931, #2932).
- Any behaviour fix found along the way, which goes in a separate issue. For example, `IngestWebhook`
  builds the envelope's fab from the raw `fabId` rather than `integration.Fab`. The two are equal
  after the own-fab check, and the code stays as written.

## 9. Locked tech choices

.NET 10 Minimal APIs (ADR-0070), `Result<T,E>` + `ApiError.ToProblem` (ADR-0047/0089), `Ensure.That`
(ADR-0105), xUnit + Shouldly + Moq (ADR-0052), no Testcontainers (ADR-0103).
