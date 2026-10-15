# Spec 371: `EventsEndpoints.Writes.cs` under 300 lines

**Feature Branch**: `refactor/2962-event-endpoints-writes-split` (cut from `origin/develop` at `8102af6f`)

**Created**: 2026-10-15

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. One deviation from the
issue's proposed shape is decided and recorded (§5, D-1), because the issue's shape fails an
architecture guard.

**Input**: Issue [#2962](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2962).
Refs #2930 (spec 365, which built the characterisation suite this spec relies on) and #2896.

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving**, so Phase 4a is **characterisation,
observed green**. Engineer: **backend-engineer**. **Security-adjacent** (the webhook bearer check
moves): Phase 6 runs `/security-review` as well as `/code-review`, and a diff that is not a pure move
plus the call-site qualifications listed in plan §2 is a **block**.

**Spec number.** 371. Checked 2026-10-15 after `git fetch`: highest spec on `origin/develop`
(`8102af6f`) and on every `refs/remotes/origin/*` head is 370. Open PRs: #2811 (devcontainer), no
spec. Re-check before the PR is opened.

**ADRs.** **ADR-0084** (S104, 300 lines per file; advisory, carved out of `TreatWarningsAsErrors`),
ADR-0144 (lane; characterisation colour; no gate weakened), ADR-0070 (Minimal APIs), ADR-0036
(smallest change), ADR-0087 (each commit builds on its own), ADR-0142 (the idempotent manual create,
untouched), ADR-0118 (the one OTLP sink the revoked-delivery log window protects, untouched).

**No new ADR.** ADR-0084 already states the limit; spec 341 already settled how an Api file sheds
lines when its partial class is pinned at a file count (§5).

## 1. Problem

`src/EventIngestion/Api/EventsEndpoints.Writes.cs` is 519 lines against S104's 300. It is the only
remaining S104 hit in production code (Release `--no-incremental` build, 2026-10-15). It holds two
route handlers and every helper either one calls:

| Lines | Members | Used by |
|---|---|---|
| 22 | `BearerPrefix` | webhook auth |
| 24-176 | `IngestManualEndpoint`, `IngestManual`, `AdmitManualCallerAsync`, `ManualEnvelope`, `ManualScope`, `IngestManualServices`, `IsRevokedWebhookCallerAsync` | manual path |
| 178-242 | `IngestWebhook`, `WebhookEnvelope` | webhook path |
| 244-428 | `AuthenticateWebhookAsync`, `FindIntegrationAsync`, `IsRevokedLoggingOnce`, `RevokedWebhookDeliveryLogWindow`, `IsNewRevokedWebhookDeliveryLog`, `IsIntegrationsOwnFab`, `ValidateJwtAsync` | webhook auth (185 lines) |
| 430-443 | `FabNotProvisioned` | both paths |
| 445-518 | `StoreOrRefuseAsync` | both paths (74 lines) |

## 2. User story

### US1 (P1): the last production S104 hit is gone and nothing a caller can see moved

*As the maintainer holding ADR-0084's shape limits*, I want `EventsEndpoints.Writes.cs` at or under
300 lines, with every route, status, body, header and log line exactly as it is, and every test and
architecture guard that reads this code passing **unmodified**.

**Independent test.** Before and after: the Release `--no-incremental` build's S104 grep, and the
characterisation commands in plan §4. *Before*: one S104 hit, characterisation green. *After*: no
S104 hit, the same tests green, `git diff` on `tests/` empty.

#### Acceptance scenarios

1. **Happy path, the gate clears.** *Given* `origin/develop` at `8102af6f`, *when* the split lands,
   *then* `dotnet build SmartSentinelEye.slnx -c Release --no-incremental` reports no `S104` for any
   file under `src/EventIngestion/Api`, and every touched or new file is at most 300 lines.
2. **Happy path, ingest unchanged.** *Given* a caller holding `sse.events.write` on a provisioned
   fab, *when* it posts `POST /events/manual` with or without an `Idempotency-Key`, *then* it gets
   the same `201` + `Location: /events/{id}` (or the replayed identifier) as before; a valid webhook
   delivery to `POST /events/webhook/{integrationName}` gets the same `201`.
3. **Conflict.** *Given* two concurrent manual posts sharing a key and a caller, *then* the second
   still answers `409 IDEMPOTENT_REQUEST_IN_PROGRESS`; a key reused with a different body still
   answers `422 IDEMPOTENCY_KEY_REUSED`. A saturated limiter still answers `429
   EVENT_INGEST_BACKPRESSURE`.
4. **Bad request.** *Given* an unparseable envelope (missing payload, bad kind), *then* both routes
   still answer `400 EVENT_INVALID_INPUT` with the same `detail`; a malformed `Idempotency-Key`
   still answers `400`; an unprovisioned fab still answers `503 EVENT_FAB_NOT_PROVISIONED` with the
   same detail text.
5. **Auth, the webhook refusal order is unchanged.** *Given* a webhook delivery, *then* the refusal
   order stays: `Bearer ` prefix (ordinal) → name parse → lookup → revoked (logged at most once per
   one-minute window per integration, same message, same `EventsEndpoints` logger category) → the
   integration's own fab → token by mode (static hash, or JWT with `sse.events.write`, matching
   `azp`, and `/fabs/<fabId>` group). Every refusal is the same empty `401`. A manual caller whose
   `azp` names a revoked rotated integration is still refused `401` before fab resolution.

## 3. Requirements

- **FR-001.** S104 is cleared by **moving members by responsibility**, never by line count alone,
  never by reformatting, deleting comments or joining lines.
- **FR-002.** Member bodies and their doc comments move **byte-identical** (modulo the indentation
  they already have). The only edits allowed in moved or remaining code are those listed in plan §2:
  a class qualifier on three call sites, `private` → `internal` on the two methods that become
  cross-class entry points, and `using` directives redistributed (IDE0005 clean).
- **FR-003.** No `Map*` call or fluent chain changes. No route, verb, status, scope, header,
  OpenAPI, DTO, problem title/detail, or log message/category/level change.
- **FR-004.** The guard constraints in §5 hold. **No file under `tests/` is edited.**
- **FR-005.** No suppression, threshold, `#pragma`, `.editorconfig`, `SonarLint.xml` or csproj
  change.
- **FR-006.** Each commit builds on its own and keeps the characterisation suite green (ADR-0087).
- **FR-007.** Two production comments that name the moved code's file are re-pointed (plan §2.4);
  comment-only, proven by a comment-stripped hash.

**Latency-budget impact: N/A in effect.** Ingest sits at the start of the *event → overlay state*
leg (constitution §IV), but a static method moving to another static class compiles to the same
direct call. No runtime behaviour changes; nothing is measured.

## 4. Characterisation, the safety net

Spec 365 (#2930) already pins both write paths in-process, refusal order included. **No gap; Phase
4a writes no new test** — it runs the existing suite green and quotes it. Plan §4 lists the tests
and commands.

| Moved unit | Covering test |
|---|---|
| `AuthenticateWebhookAsync` and its six helpers | `WebhookEndpointCharacterisationTests` (refusal order, revoked log once per window, JWT via `IAuthenticationService`, cross-fab); `WebhookBearerValidationIntegrationTests`, `WebhookRevocationRefusesDeliveryIntegrationTests`, `RevokedWebhookDeliveryIsLoggedIntegrationTests` (CI) |
| `StoreOrRefuseAsync` | `EventIngestEndpointCharacterisationTests`, `WebhookEndpointCharacterisationTests` (`429`); `IngestBackpressureIntegrationTests`, `FabStorageRefusalIntegrationTests`, `DiscoverySourceHoldsIntegrationTests` (`202` held), `IdempotencyKeyReuseEventIngestionIntegrationTests` (CI) |
| Handlers (stay in place) | `EventIngestionRouteTableCharacterisationTests` plus the above |

## 5. Guard constraints and decisions

- **G-a. New files must not end in `Endpoints.cs`.** `EndpointFileCount = 16` is pinned in
  `EndpointScopeDeclarationTests` and `PreconditionDeclarationTests` (suffix match).
- **G-b. `EventsEndpoints` stays at exactly three partial files.**
  `PreconditionDeclarationTests.A_partial_endpoint_class_is_indexed_across_every_file_that_declares_it`
  pins `[InlineData("EventsEndpoints", 3)]`.
- **G-c. Both handlers stay whole in `EventsEndpoints.Writes.cs`.** `IdempotentKeyReuseDeclarationTests`
  reads `IdempotentRequest.ExecuteCreateAsync(` in `IngestManual`'s body;
  `RouteValueRefusalDeclarationTests` and `StatusProducerDeclarationTests` (M9, M10, M12, M16, all
  `Visibility.HandlerBody`) read `StatusCodes.Status400BadRequest`, `IdempotencyHeaders.TryRead` and
  the `IdempotentRequest` call in the mapped handler's body (one hop, no helper followed). Moving a handler or hoisting those tokens would shrink a
  guard's population silently — weakening a gate, forbidden.
- **G-d. The route-table snapshot pins `EventsEndpoints+IngestManualServices`**
  (`EventIngestionRouteTableCharacterisationTests`), so that record stays nested in `EventsEndpoints`.

**D-1. The issue's "sibling partial files" shape is not taken.** It would make `EventsEndpoints` a
four- or five-file partial and fail G-b, and editing that `InlineData` is editing a covering test,
which this lane forbids. Spec 341 met the identical constraint for `LayoutEndpoints` and
`OverlayEndpoints` and chose **non-handler helpers into a separate, non-partial `internal static
class`** (precedent `EventIngestionFabResolution`). This spec does the same. The seam is the one the
issue names; only the container differs.

**D-2. Method names are kept** (`AuthenticateWebhookAsync`, `StoreOrRefuseAsync`). Prose in
`IWebhookIntegrationRepository.cs`, `ConcurrencyConflictDeclarationTests` and five integration
tests names them; keeping the names keeps those pointers true without touching `tests/`.

**D-3. Not a bug fix.** Investigation found nothing behaviour-changing to fix. Out-of-scope findings,
reported in the PR and not touched here (ADR-0036):
1. `StoreOrRefuseAsync` carries two consecutive `<summary>` blocks (doc defect; moves verbatim).
2. `IsNewRevokedWebhookDeliveryLog` is check-then-set on `IMemoryCache`, so two concurrent refusals
   can both log inside one window. At worst a second log line; the window still bounds the rate.
3. Test doc comments already cite stale locations (`IngestBackpressureIntegrationTests` cites
   `EventsEndpoints.Writes.cs:344-351`; `MissingPayloadIsRefusedIntegrationTests` cites `:85`) and
   three more name `EventsEndpoints.Writes.AuthenticateWebhookAsync`. Left, because `tests/` is
   frozen for this change.

## 6. Out of scope

S107/S138/S1541 in this file or elsewhere; the `IngestManual` signature; the findings in D-3; any
other context.
