# Plan 464 — A disabled client replays no secret

Spec: [spec.md](spec.md) · Issue #3123 · ADR-0142, ADR-0047/0089 · Latency: N/A.

## Context and layers

Bounded context: **Identity** only. Application layer (one query handler, one error record).
No Domain, Infrastructure, Api, Shared.Contracts, migration or AppHost change. No cross-context reference.

## Replay path (as it stands)

| Step | Location |
|---|---|
| Rotate first call ends `disabledDuringRace`, still returns the secret | `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:69-85, 116-121` |
| Key completed as created with the answer's version | `src/Identity/Api/WebhookRotationEndpoints.cs:174-180` (`RotationOutcome` → `IdempotentOutcome.CreatedAt`) |
| Rotate replay, bound to `answeredAt` | `src/Identity/Api/WebhookRotationEndpoints.cs:205-230` |
| Device / kiosk replay, unbound | `src/Identity/Api/DevicesEndpoints.cs:183-198`, `src/Identity/Api/KiosksEndpoints.cs:180-195` |
| Row read, no `DisabledAt` check | `src/Identity/Application/Queries/Handlers/ReplayRegisteredClientQueryHandler.cs:50-58` |
| Secret read and returned | same file `:65-85` |
| Refusal family | `src/Identity/Application/Queries/ReplayRegistrationErrors.cs:10-47` |

All three endpoints map any `ReplayRegistrationError` through `error.ToProblem()`, so a new
member of the family needs no endpoint change.

## Design

1. **`ReplayRegistrationErrors.cs`** — add
   `public sealed record ReplayedClientDisabled(Guid Client) : ReplayRegistrationError("REPLAYED_CLIENT_DISABLED", "<message naming no client, version, fab or secret>", HttpStatusCode.Conflict);`
   Sibling of `ReplayedRegistrationMissing` / `ReplayedClientMissingInKeycloak` (same `REPLAYED_*`
   prefix, same 409). `IDEMPOTENCY_KEY_SUPERSEDED` is not reused: its text says the client "has changed
   since" the answer, which is false for the race case (the client was disabled *before* the answer
   was returned), and its advice ("send a new key") is the wrong remedy for a disabled client.
2. **`ReplayRegisteredClientQueryHandler.HandleAsync`** — immediately after `var registered = found.Value;`
   (line 58) and before the `answeredAt` version check (line 60):
   `if (registered.DisabledAt is not null) { return Failure(new ReplayedClientDisabled(client.Value)); }`
   Order rationale: a disabled client reads no secret, whatever its version.
   Update the class `<summary>` with one paragraph on why (the first call's FR-005 answer is not
   re-issuable once the client is off; issue reference goes in the PR, not the code).
3. `IRegisteredClientRepository.GetByIdentifierAsync` unchanged — "missing" and "disabled" remain
   separate codes (FR-005).

## Invariant

*A replay returns a secret only for a client whose `DisabledAt` is null at the moment it is read.*
`RegisteredClient.DisabledAt` (`src/Identity/Domain/RegisteredClient/RegisteredClient.cs:29`) is
already the authority; no new domain state.

## Messaging

None. No domain or integration event added or changed.

## Boundary rules

Identity-internal. NetArchTest unaffected.

## Tests (phase 4a, red)

- `tests/Identity.Application.Tests/Queries/ReplayRegisteredClientQueryHandlerTests.cs` (extend; uses
  `InMemoryRegisteredClientRepository`, whose `GetByIdentifierAsync` returns disabled rows like production):
  - unbound replay of a disabled client → `REPLAYED_CLIENT_DISABLED`, Keycloak call count unchanged after arrange;
  - bound replay (`AnsweredAt` = current version) of a disabled client → `REPLAYED_CLIENT_DISABLED`, no Keycloak read;
  - the refusal's message contains neither the client id, the fab nor the secret.
- `tests/Architecture.Tests/ReplayOfDisabledClientEndpointTests.cs` (new; `IdentityEndpointHost`,
  `Held(...)` then `.Disable(clock)` on the returned aggregate, `StoreReplaysAs`):
  - webhook rotate replay → 409, title `REPLAYED_CLIENT_DISABLED`, `Keycloak.ReadClientSecretAsync` never called;
  - device register replay → 409, same title.
  Not an Integration.Tests class, so no `ci-shards/shard-N.filter` entry is needed.
- Unmodified and green: `ReplayRegisteredClientQueryHandlerSupersededTests`,
  `WebhookRotationReplaySupersededEndpointTests`, `DeviceAndKioskEndpointCharacterisationTests`,
  `RotateWebhookClientCommandHandler*Tests` (first call unchanged).

## Constitution check

§II: no primitive enters the domain (the error record lives in Application, takes `Guid` like its
siblings). §Testing: red first. ADR-0105: no new guards needed beyond the existing `Ensure.That(query)`.
No ADR required (owner decision on #3123; additive member of an ADR-0142 refusal family).
