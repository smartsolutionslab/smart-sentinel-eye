# Spec 380: the EventIngestion Api helpers under five parameters

**Feature Branch**: `refactor/2971-eventingestion-api-s107` (cut from `origin/develop` at `c0f4969d`)

**Created**: 2026-10-16

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. The issue's scope is
**shrunk** from 13 hits to 3 (§5, D-1): the other 10 cannot be cleared by the issue's prescribed
pattern at all, and cannot be cleared by any other pattern without editing a characterisation
baseline, which is a block (ADR-0144). The 10 are reported back for an owner decision (§6).

**Input**: Issue [#2971](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2971).
Refs spec 341 (`IngestManualServices`), spec 365 (the characterisation suite this spec relies on,
and its J4), spec 371 (the split that produced `WebhookDeliveryAuthentication.cs`), #2970 (`Log.cs`).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **backend-engineer**. **Security-sensitive**: `AuthenticateWebhookAsync`,
the bearer check of the anonymous webhook route, changes signature, so Phase 6 runs
`/security-review` (security-reviewer) as well as `/code-review`.

**Spec number.** 380. Checked 2026-10-16 after `git fetch`: highest spec on `origin/develop`
(`c0f4969d`) is 378; 379 is claimed by the unmerged `fix/2989-url-redaction-host-charset` (local
worktree `D:\Github\sse-2989`); no other local, remote or open-PR head carries 379+. Re-check before
the PR is opened.

**ADRs.** **ADR-0084** (S107, at most 4 parameters; advisory `warning`, carved out of
`TreatWarningsAsErrors`), ADR-0144 (lane; characterisation colour; no gate weakened), ADR-0070
(Minimal APIs), ADR-0036 (smallest change), ADR-0087 (each commit builds on its own), ADR-0042/0057
(hand-rolled command handlers), ADR-0105 (`Ensure.That`), ADR-0118 (the one OTLP sink the
revoked-delivery log window protects — the window is untouched).

**No new ADR.** ADR-0084 states the limit; this spec only changes the shape of three private or
internal helpers. The open question in §6 *may* need one; that is for the owner, not this lane.

## 1. Problem — the re-measured hit list

Release, `--no-incremental`, `sort -u`, measured 2026-10-16 on `c0f4969d`
(`dotnet build SmartSentinelEye.slnx -c Release --no-incremental -flp:logfile=…metrics.log;verbosity=normal;NoSummary`,
then `grep -oE '[^ (]+\([0-9]+,[0-9]+\): warning S107' | sort -u`): **113 S107 hits solution-wide,
14 under `src/EventIngestion/Api`**. The issue's 13 plus `Log.cs`, which #2970 owns.

"Counted" is the parameters S107 counts. **Sonar's S107 does not count a `[FromServices]`
parameter.** Proven by counterfactual on `c0f4969d`: `GetEvent` has six parameters, two of them
`[FromServices]`, and is not a hit; removing `[FromServices]` from its `fabGuard` alone produced a
new hit at `EventsEndpoints.Reads.cs(77,48)` (file restored afterwards). The table agrees
throughout: `RegisterAsync` (5 plain) is a hit, `WebhookIntegrationsEndpoints.Register` (6, two of
them `[FromServices]`) is not.

| # | Hit | Method | Kind | Counted parameters (non-`[FromServices]`) | n |
|---|---|---|---|---|---|
| H1 | `EventsEndpoints.Writes.cs(21,52)` | `IngestManual` | route handler | `body`, `services` (`[AsParameters]`), `http`, `user`, `fabId`, `cancellationToken` | 6 |
| H2 | `EventsEndpoints.Writes.cs(172,53)` | `IngestWebhook` | route handler | `integrationName`, `body`, `fabId`, `request`, `cancellationToken` (+7 `[FromServices]`) | 5 |
| H3 | `EventsEndpoints.Reads.cs(16,50)` | `ListEvents` | route handler | `user`, `fabId`, `source`, `deviceId`, `kind`, `occurredAfter`, `occurredBefore`, `ingestedAfter`, `ingestedBefore`, `pageSize`, `cursor`, `cancellationToken` | 12 |
| H4 | `EventsEndpoints.Reads.cs(114,55)` | `ListDeadLetters` | route handler | `user`, `fabId`, `limit`, `reason`, `state`, `cancellationToken` | 6 |
| H5 | `EventSourcesEndpoints.cs(121,47)` | `Declare` | route handler | `body`, `services` (`[AsParameters]`), `request`, `user`, `fabId`, `cancellationToken` | 6 |
| H6 | `EventSourcesEndpoints.cs(179,66)` | `DeclareAsync` | private helper | `handler`, `fab`, `source`, `mode`, `declaredBy`, `cancellationToken` | 6 |
| H7 | `EventSourcesEndpoints.cs(228,46)` | `Change` | route handler | `source`, `request`, `body`, `user`, `fabId`, `cancellationToken` | 6 |
| H8 | `EventSourcesEndpoints.cs(288,49)` | `Undeclare` | route handler | `source`, `request`, `user`, `fabId`, `cancellationToken` | 5 |
| H9 | `EventTypesEndpoints.cs(94,48)` | `Register` | route handler | `body`, `services` (`[AsParameters]`), `request`, `user`, `fabId`, `cancellationToken` | 6 |
| H10 | `EventTypesEndpoints.cs(150,67)` | `RegisterAsync` | private helper | `handler`, `fab`, `kind`, `registeredBy`, `cancellationToken` | 5 |
| H11 | `EventTypesEndpoints.cs(198,46)` | `Retire` | route handler | `kind`, `request`, `user`, `fabId`, `cancellationToken` | 5 |
| H12 | `WebhookIntegrationsEndpoints.cs(155,46)` | `Revoke` | route handler | `name`, `request`, `user`, `fabId`, `cancellationToken` | 5 |
| H13 | `WebhookDeliveryAuthentication.cs(27,77)` | `AuthenticateWebhookAsync` | internal helper | `integrationName`, `request`, `fabId`, `integrations`, `logger`, `cache`, `cancellationToken` | 7 |
| — | `Log.cs(19,72)` | `[LoggerMessage]` partial | generated-log | out of scope (#2970) | — |

## 2. User story

### US1 (P1): the three helper hits are gone and nothing a caller can see moved

**Why P1.** It is the only part of #2971 reachable without editing a characterisation assertion
(§5, D-1). It is small, independent of the §6 decision, and does not foreclose any answer to it.

**Independent test.** The §4 commands pass on `c0f4969d` and pass again, **unmodified**, after the
change (`git diff origin/develop -- tests/` empty); the §1 measurement no longer lists H6, H10 and
H13, lists H1-H5, H7-H9, H11, H12 and `Log.cs` exactly as before (same methods; line numbers may
shift), and no other new warning appears under `src/EventIngestion/Api`.

#### Acceptance scenarios

```gherkin
Scenario: happy path — a declare, a register and a webhook delivery answer as before
  Given the EventIngestion endpoints on the refactored build
  When an operator POSTs a valid source-mode declaration, a valid event type, and a webhook
       integration POSTs a valid delivery with its bearer token
  Then each answers 201 with the same Location, body and stored values as on c0f4969d
  And the declared mode, the registered kind and DeclaredBy/RegisteredBy are the caller's

Scenario: conflict — a repeated declare and register still earn the handler's 409
  Given a pair already declared and a kind already registered in the resolved fab
  When the same declaration and registration are POSTed again without an Idempotency-Key
  Then both answer 409 with the same problem body as on c0f4969d
  And with the same key and body they replay the first answer (ADR-0142), unchanged

Scenario: bad request — the refusal order before any helper runs is unchanged
  Given an unknown source, an unknown mode, an invalid kind or a malformed Idempotency-Key
  When the declare or register is POSTed
  Then the same 400 problem is returned in the same order, before the fab is resolved
  And DeclareAsync / RegisterAsync's handler is never invoked

Scenario: auth — every webhook refusal still collapses to one 401
  Given a delivery with no bearer, a wrong prefix, an unknown or unparseable integration name,
        a revoked integration, a fabId other than the integration's own, a wrong static token,
        or a JWT failing signature, scope, azp or group
  When it is POSTed to /events/webhook/{integrationName}
  Then each answers the identical 401 body, in the same check order, with the same Times.Never
  And a revoked integration is logged once per one-minute window with its own fab, then silent
```

## 3. Requirements

- **FR-001** H6 `DeclareAsync` takes at most 4 counted parameters. Shape: plan §2.1.
- **FR-002** H10 `RegisterAsync` takes at most 4 counted parameters. Shape: plan §2.2.
- **FR-003** H13 `AuthenticateWebhookAsync` takes at most 4 counted parameters. Shape: plan §2.3.
  The order of checks, the single `null` for every refusal, the log text, the log category
  (`typeof(EventsEndpoints)`), the dedup key and window, and the helper signatures it calls
  (`FindIntegrationAsync`, `IsRevokedLoggingOnce`, `IsIntegrationsOwnFab`, `ValidateJwtAsync`) are
  unchanged.
- **FR-004** No route handler's parameter list changes: `IngestManual`, `IngestWebhook`,
  `ListEvents`, `ListDeadLetters`, `Declare`, `Change`, `Undeclare`, `Register`, `Retire`, `Revoke`
  keep their exact signatures (`EventIngestionRouteTableCharacterisationTests` pins them).
- **FR-005** Route templates, verbs, names, summaries, tags, policies, status codes, response
  bodies, `Location` values, the OpenAPI shape and log text are unchanged.
- **FR-006** No file under `tests/` changes. No suppression, threshold, `.editorconfig`,
  `SonarLint.xml`, `Directory.Build.props` or csproj change. No new file whose name ends
  `Endpoints.cs`; no new `EventsEndpoints` partial file.
- **FR-007** Each commit builds on its own and leaves the §4 suite green (ADR-0087).

## 4. Characterisation, the safety net

All of these exist (spec 365 and earlier); **none is written by this spec**. Each method that
changes is covered:

| Changed | Covered by (in-process, `tests/Architecture.Tests`) | Covered by (Aspire, `tests/Integration.Tests/EventIngestion`, CI shards authoritative) |
|---|---|---|
| `DeclareAsync` (+ its call in `Declare`) | `EventSourceAndTypeEndpointCharacterisationTests` — Declare rows: refusal order, keyed/unkeyed, named fab, 409, and the declared `Mode` and `DeclaredBy` asserted on the repository call | `EventSourceModeIntegrationTests`, `EventSourceModeAuthorizationIntegrationTests`, `DiscoverySourceHoldsIntegrationTests`, `StrictSourceIngestIntegrationTests` |
| `RegisterAsync` (+ its call in `Register`) | `EventSourceAndTypeEndpointCharacterisationTests` — Register rows, incl. `RegisteredBy` asserted | `EventTypeRegistryIntegrationTests`, `EventTypeRegistryIdempotencyIntegrationTests`, `EventTypeRegistryAuthorizationIntegrationTests`, `IdempotencyKeyReuseEventIngestionIntegrationTests` |
| `AuthenticateWebhookAsync` (+ its call in `IngestWebhook`) | `WebhookEndpointCharacterisationTests` (32 tests: refusal order with `Times.Never`, byte-identical 401 bodies, revoked log once per window, JWT via `IAuthenticationService`, cross-fab) | `WebhookBearerValidationIntegrationTests`, `WebhookRevocationRefusesDeliveryIntegrationTests`, `RevokedWebhookDeliveryIsLoggedIntegrationTests`, `CrossFabWebhookRotationEffectIntegrationTests`, `AnonymousIngestIsRefusedTests` |
| every handler signature (must not move) | `EventIngestionRouteTableCharacterisationTests` | — |
| manual ingest (unchanged; a regression net for the shared file) | `EventIngestEndpointCharacterisationTests` | `ManualIngestFabScopingIntegrationTests`, `ManualIngestRefusesRevokedWebhookCallerIntegrationTests` |

Commands are in plan §4.

## 5. Guard constraints and decisions

**D-1. Ten hits are out of reach; the scope shrinks to H6, H10, H13.**

1. *The issue's pattern cannot clear any route-handler hit.* Spec 341's shape bundles the
   `[FromServices]` parameters into an `[AsParameters]` record. S107 already does not count those
   (§1, counterfactual), so the bundle removes nothing — and the record parameter itself *is*
   counted, which is why H1, H5 and H9 (all three already bundled) are still hits at 6. Applying the
   pattern to H2, H7, H8, H11 and H12 would leave each count unchanged, or raise it by one.
2. *The only shape that could clear them changes a characterisation baseline.* Every counted
   handler parameter is a request-bound value (route value, `[FromQuery]`, `[FromBody]`,
   `HttpRequest`/`HttpContext`, `ClaimsPrincipal`, `CancellationToken`). Moving those into an
   `[AsParameters]` record changes the handler's `MethodInfo` parameter list, which
   `EventIngestionRouteTableCharacterisationTests.Baseline` renders literally on its `handler …`
   line for all 15 routes. That assertion would have to be edited — evidence the pinned surface
   moved — which is a **block**, not an adjustment (ADR-0144; constitution §Testing). It would also
   put the OpenAPI parameter names (`fabId`, `pageSize`, …) at the mercy of record property names,
   which FR-005 forbids risking in a refactor.
3. *What is left is reachable without any test edit.* H6, H10 and H13 are not route handlers; no
   test reflects on them by name (grep of `tests/`: no reference to `DeclareAsync`, `RegisterAsync`
   or `AuthenticateWebhookAsync`), and the route table does not see them.

**D-2. The guards, checked one by one (all must pass unmodified; any red is a block).**

| Guard (`tests/Architecture.Tests`) | Reads | Reacts to this spec? |
|---|---|---|
| `EventIngestionRouteTableCharacterisationTests` | every handler's name + parameter list, literal baseline | **Would react to any handler-signature change** (D-1.2). Not to this spec: FR-004. |
| `PreconditionDeclarationTests` | `*Endpoints.cs` files (count 16), `EventsEndpoints` at 3 files, `If-Match` helper calls in handler bodies | No: no file added or renamed; `Declare`/`Register` have no `If-Match`. |
| `EndpointScopeDeclarationTests` | `*Endpoints.cs` count 16, mapping count, scope declarations on chains | No: chains untouched. |
| `IdempotentKeyReuseDeclarationTests` | 422 declaration on chains ⇔ `IdempotentRequest.Execute*` in the handler body | No: `ExecuteCreateAsync` stays in `Declare`/`Register`; only its lambda's argument changes. |
| `RouteValueRefusalDeclarationTests` | 400 producers in handler bodies ⇔ declared 400 | No: no producer moves. |
| `StatusProducerDeclarationTests` | status producers in handler bodies ⇔ declared statuses | No: no producer moves (`ToProblem()` stays in the helpers). |
| `ConcurrencyConflictDeclarationTests` | 409 producers / mutating mappings | No. |
| `HandlerDeconstructionTests` | `public … Handle`/`HandleAsync` signatures only | No: none of the three is a `Handle` method. |
| `BoundaryTests` | assembly references | No: no new reference. |

**D-3. `AuthenticateWebhookAsync` is now in scope; spec 365 J4 does not forbid it.** J4 kept the
7-parameter signature because S107 was *not asked for* in #2930 ("in-scope-adjacent but not asked
for"), not for a security reason. #2971 asks. The body stays textually identical below one
deconstruction line (plan §2.3), so the security review compares two signatures and one line.

**D-4. No new characterisation test.** §4 covers every changed method with assertions on the
values the moved parameters carry (mode, kind, declaredBy, registeredBy, the integration name, the
fab, the bearer); a refactor with covering tests is not a rewrite.

## 6. Out of scope, and the decision handed back

- **H1-H5, H7-H9, H11, H12 (ten route handlers): blocked, owner decision needed.** The options, none
  of which this lane may take on its own:
  (a) accept that a minimal-API handler's request-bound parameters are structural and record that
  as an ADR-0084 scope note (an ADR amendment — a human decision, ADR-0144);
  (b) authorise a deliberate re-capture of the route-table baseline's `handler …` lines together
  with request-value `[AsParameters]` records, proven OpenAPI-neutral by diffing the generated
  document — a separately approved contract-surface change, not a refactor;
  (c) leave them as advisory warnings (ADR-0084 is advisory).
  **The same block applies to #2972-#2975**: every context with a route-table baseline renders the
  handler parameter list, and the spec 341 pattern cannot reduce S107 anywhere, since `[FromServices]`
  is never counted.
- `Log.cs(19,72)` (#2970). Hits outside `src/EventIngestion/Api`.
- Unbundling `IngestManualServices` / `DeclareSourceModeServices` / `RegisterEventTypeServices`
  (6 → 5, still a hit, and a route-table change).
- Every other ADR-0084 metric in these files.
