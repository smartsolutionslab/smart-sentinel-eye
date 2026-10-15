# Plan 371: `EventsEndpoints.Writes.cs` under 300 lines

**Spec**: [spec.md](./spec.md). **Issue**: #2962. **Base**: `origin/develop` at `8102af6f`.

## 1. Context and layers

Bounded context **EventIngestion**, **Api layer only** (`src/EventIngestion/Api`). No Domain,
Application, Infrastructure, Shared.Kernel, Shared.Contracts or AppHost change; no entity, value
object, invariant, domain event or integration event is touched; no cross-context reference is
added. Both new types are `internal`, in namespace `SmartSentinelEye.EventIngestion.Api`, next to
the precedent `EventIngestionFabResolution.cs`.

## 2. The move

### 2.1 `WebhookDeliveryAuthentication.cs` (new, `internal static class WebhookDeliveryAuthentication`)

Moves, in this order and byte-identical: `BearerPrefix` (line 22), then lines 244-428 —
`AuthenticateWebhookAsync` (with its `<summary>`), `FindIntegrationAsync`, `IsRevokedLoggingOnce`,
`RevokedWebhookDeliveryLogWindow`, `IsNewRevokedWebhookDeliveryLog`, `IsIntegrationsOwnFab`,
`ValidateJwtAsync`.

- `AuthenticateWebhookAsync`: `private` → `internal`. Everything else stays `private`.
- Class doc: one `<summary>` line saying it holds the webhook route's bearer authentication for
  `EventsEndpoints.IngestWebhook`.
- The logger is still created in `IngestWebhook` as `loggerFactory.CreateLogger(typeof(EventsEndpoints))`
  and passed in, so the log **category stays `EventsEndpoints`**. Do not create a logger in the new
  class.
- `<see cref="RevokedWebhookDeliveryLogWindow"/>` still resolves (same class).
- `Log.RevokedWebhookIntegrationDeliveryRefused` is an `ILogger` extension in `Log.cs`; no change.

### 2.2 `EventIngestStore.cs` (new, `internal static class EventIngestStore`)

Moves lines 445-518 byte-identical: `StoreOrRefuseAsync` with **both** existing `<summary>` blocks
(spec D-3.1). `private` → `internal`. Class doc: one `<summary>` line.

### 2.3 `EventsEndpoints.Writes.cs` (remains, `public static partial class EventsEndpoints`)

Keeps lines 1-21, 24-243 and 430-443: `IngestManualEndpoint`, `IngestManual`,
`AdmitManualCallerAsync`, `ManualEnvelope`, `ManualScope`, `IngestManualServices`,
`IsRevokedWebhookCallerAsync`, `IngestWebhook`, `WebhookEnvelope`, `FabNotProvisioned`.

Three call-site edits, nothing else:

| Line (before) | Before | After |
|---|---|---|
| 71 | `token => StoreOrRefuseAsync(` | `token => EventIngestStore.StoreOrRefuseAsync(` |
| 197 | `await AuthenticateWebhookAsync(` | `await WebhookDeliveryAuthentication.AuthenticateWebhookAsync(` |
| 225 | `(await StoreOrRefuseAsync(` | `(await EventIngestStore.StoreOrRefuseAsync(` |

If a qualified call exceeds the line, wrap per house style (one argument per line) — the argument
text itself does not change. `using` directives: drop what IDE0005 reports unused
(`Microsoft.AspNetCore.Authentication`, `...JwtBearer`, `Application.Commands`,
`Application.Commands.Handlers` expected); each new file carries only what it uses.

### 2.4 Comment pointers (comment-only)

- `src/EventIngestion/Api/Program.cs:14` — `(EventsEndpoints.Writes.cs, spec 302 ...)` →
  `(WebhookDeliveryAuthentication.cs, spec 302 ...)`.
- `src/ServiceDefaults/Authorization/FabClaims.cs:19` — `EventsEndpoints.Writes` →
  `WebhookDeliveryAuthentication` (it refers to `ValidateJwtAsync`'s ad-hoc groups check).
- `EventsEndpoints.cs:9-11` summary stays true (handlers are still in `.Writes.cs`/`.Reads.cs`); no
  edit.

Proof: strip comments and hash both files before and after; hashes equal.

### 2.5 Projected sizes (physical lines, the count S104 reported as 519)

| File | Before | After (projected) |
|---|---|---|
| `EventsEndpoints.Writes.cs` | 519 | ~252 |
| `WebhookDeliveryAuthentication.cs` | — | ~205 |
| `EventIngestStore.cs` | — | ~92 |
| `EventsEndpoints.cs` / `EventsEndpoints.Reads.cs` | 136 / 163 | unchanged |

Moving only the webhook-auth cluster leaves `.Writes.cs` at ~330, so both moves are needed. The
engineer re-measures (`wc -l`) after each commit.

### 2.6 Why these two and not the manual admission helpers

`AdmitManualCallerAsync` takes `IngestManualServices`, a private nested record the route-table
snapshot pins as `EventsEndpoints+IngestManualServices` (spec G-d); moving the helper out would
force that record's accessibility or the helper's signature to change. The webhook-auth cluster and
`StoreOrRefuseAsync` take only public types and carry none of the tokens the one-hop guards read
(spec G-c).

## 3. Boundary and guard check

| Guard | Why it stays green unmodified |
|---|---|
| `PreconditionDeclarationTests` — `EventsEndpoints` in 3 files; `EndpointFileCount = 16`; masker-form scan of every `src/*/Api/*.cs` | No new partial file; new names do not end in `Endpoints.cs`; moved text adds no raw string or new literal form |
| `EndpointScopeDeclarationTests` — `EndpointFileCount = 16`, mapping count | Same |
| `IdempotentKeyReuseDeclarationTests` — `IdempotentRequest.ExecuteCreateAsync(` in the mapped body | Stays in `IngestManual` |
| `RouteValueRefusalDeclarationTests`, `StatusProducerDeclarationTests` — handler-body tokens | Handler bodies keep every token; only call targets are qualified |
| `ConcurrencyConflictDeclarationTests` — register prose naming `StoreOrRefuseAsync` | Name kept (spec D-2) |
| `EventIngestionRouteTableCharacterisationTests` — handler signatures incl. `EventsEndpoints+IngestManualServices` | Handlers and record untouched |
| `BoundaryTests` (NetArchTest) | No new project reference |

## 4. Covering tests and commands (Phase 4a characterisation, observed green before; unmodified after)

All commands from `D:\Github\sse-2962`. Stop any running AppHost first (it holds the binaries).

**In-process (no Aspire; the primary safety net — spec 365 built it for exactly this file):**

```sh
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~EventIngestEndpointCharacterisationTests|FullyQualifiedName~WebhookEndpointCharacterisationTests|FullyQualifiedName~EventIngestionRouteTableCharacterisationTests"
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~BoundaryTests"
dotnet test tests/Architecture.Tests -c Release
dotnet test tests/EventIngestion.Application.Tests -c Release --filter "FullyQualifiedName~IngestWriteLimiterTests"
```

Each `--filter` run must report a non-zero test count (a filter matching nothing exits 0, #2289).

**Aspire fixture (CI's integration shards are authoritative; run locally if the stack is free —
one machine, one stack):**

```sh
dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~SmartSentinelEye.Integration.Tests.EventIngestion.WebhookBearerValidationIntegrationTests|FullyQualifiedName~WebhookRevocationRefusesDeliveryIntegrationTests|FullyQualifiedName~RevokedWebhookDeliveryIsLoggedIntegrationTests|FullyQualifiedName~ManualIngestRefusesRevokedWebhookCallerIntegrationTests|FullyQualifiedName~ManualIngestFabScopingIntegrationTests|FullyQualifiedName~AnonymousIngestIsRefusedTests|FullyQualifiedName~MissingPayloadIsRefusedIntegrationTests|FullyQualifiedName~FabStorageRefusalIntegrationTests|FullyQualifiedName~IngestBackpressureIntegrationTests|FullyQualifiedName~IdempotencyKeyReuseEventIngestionIntegrationTests|FullyQualifiedName~DiscoverySourceHoldsIntegrationTests|FullyQualifiedName~StrictSourceIngestIntegrationTests"
```

**Gate measurements:**

```sh
dotnet build SmartSentinelEye.slnx -c Release --no-incremental 2>&1 | grep -E "S104|warning|error" | grep "EventIngestion[\\/]Api"
wc -l src/EventIngestion/Api/EventsEndpoints*.cs src/EventIngestion/Api/WebhookDeliveryAuthentication.cs src/EventIngestion/Api/EventIngestStore.cs
git diff --stat origin/develop -- tests/        # must be empty
```

**Pure-move proof** (attach to the PR): for each moved block, extract it from the old file
(`git show 8102af6f:src/EventIngestion/Api/EventsEndpoints.Writes.cs | sed -n 244,428p`, `445,518p`,
`22p`) and diff against the new file's body; the only differences allowed are the two
`private` → `internal` tokens. For `.Writes.cs`, `git diff -w` shows only the three qualifications
and removed `using`s / moved blocks.

## 5. Risks

- **A guard reading a moved token.** Checked: none of the one-hop guards reads helper bodies (§3).
  If any guard turns red, that is a block, not an edit.
- **Release analyzers on the new files** (e.g. a Sonar rule newly applicable to an `internal`
  class). Fix only by changing the new file's *shape* (class doc, modifiers), never by suppression;
  if the only fix is a body change, block.
- **Spec-number race.** Re-check `specs/` on `origin/develop` and open PR heads before opening the PR.
