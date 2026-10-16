# Plan 382: Identity's read-fab resolution under five parameters

**Spec**: [spec.md](./spec.md). **Issue**: #2975. **Base**: `38eb836f`.

## 1. Context and layers

Identity, **Api layer only** (`src/Identity/Api/IdentityFabResolution.cs` and the three callers
`DevicesEndpoints.cs`, `KiosksEndpoints.cs`, `WebhookRotationEndpoints.cs`). No Domain,
Application, Infrastructure, Shared.Kernel, Shared.Contracts, ServiceDefaults or AppHost change. No
entity, value object, invariant, domain or integration event, or messaging change. Boundary: the
new record references only `System.Security.Claims.ClaimsPrincipal` and `string`, both already used
by the file. CameraCatalog: **no change** (all four hits blocked, spec D-1).

**Latency budget (constitution §IV).** N/A. The three routes are operator-console reads, off the
event-to-overlay path; the change adds one small record allocation per list request.

**Constitution §II** does not apply: an Api-layer record carrying wire values, like the existing
`*Services` records and `Requests/` DTOs.

## 2. Shape

### 2.1 H8 `IdentityFabResolution.ResolveReadFabsAsync` (5 → 4) - security-sensitive

A record, declared in `IdentityFabResolution.cs` after the class (top-level `internal`, the same
placement spec 380 used for `WebhookDelivery` in `WebhookDeliveryAuthentication.cs`):

```csharp
/// <summary>A read the caller asks for: who they are, and the fab they named (empty for "all of mine").</summary>
internal sealed record FabReadRequest(ClaimsPrincipal User, string Fab);
```

Signature and the first body line:

```csharp
public static async Task<Result<IReadOnlyList<FabIdentifier>, IResult>> ResolveReadFabsAsync(
    FabReadRequest read,
    IFabAuthorizationGuard fabGuard,
    string unusableFabErrorCode,
    CancellationToken cancellationToken)
{
    var (user, fabId) = read;

    // every following line byte-identical to 38eb836f lines 43-69
}
```

The locals take the old parameter names, so the rest of the body (the
`FabResolution.ResolveForReadAsync(user, fabId, fabGuard, cancellationToken)` call, the per-entry
`FabIdentifier.From` with `catch (ArgumentException)`, the `fabs.Count == 0` 400 with
`title: unusableFabErrorCode`, and the success) is **textually unchanged**. That is what the
security review checks: `git diff 38eb836f -- src/Identity/Api/IdentityFabResolution.cs` shows the
signature, one deconstruction line plus its blank line, and the record. The method's `<summary>`
keeps its meaning; its `<paramref name="fabId"/>` becomes
`<see cref="FabReadRequest.Fab"/>` (the only doc edit, because a `paramref` to a parameter that no
longer exists is a dangling reference, CS1734 where documentation generation is on). No `Ensure.That` is added (internal helper, its callers
are the handlers; `FabResolution.ResolveForReadAsync` already guards `user` and `fabGuard`;
ADR-0036, no drive-by guards). The method stays `public` on the `internal static` class.

**Call sites** (three, same shape; one argument per line per the house wrapping rule, because the
call does not fit on one line):

```csharp
var fabsResolution =
    await IdentityFabResolution.ResolveReadFabsAsync(
        new FabReadRequest(user, fabId),
        fabGuard,
        "KIOSK_INVALID_INPUT",
        cancellationToken);
```

with `"DEVICE_INVALID_INPUT"` in `DevicesEndpoints.List` and `"WEBHOOK_INVALID_INPUT"` in
`WebhookRotationEndpoints.List`. The literal stays in the handler body where it is today (spec
FR-003). Each `List` grows by three lines (about 20 → 23 counted, under S138's 30). The
`[FromServices]`/`[FromQuery]` signature of each `List` is **not** touched (FR-004).

### 2.2 What is not touched

H1-H7 and every other handler signature; the `*Services` records; `FabResolution` in
ServiceDefaults; every `Map*` method and chain; every CameraCatalog file; `RevocationEndpoints.cs`.

## 3. Boundary and guard check

See spec §5 D-2. Summary: the only guards that read signatures literally are the two route-table
baselines, which read route handlers only, none of which changes. The declaration guards resolve
handler bodies by method-group name; in the three `List` bodies the only change is the helper
call's argument list, which contains no status producer, no `If-Match` helper, no
`IdempotentRequest` call. No file is added or renamed under `src/*/Api`.

## 4. Covering tests and commands (Phase 4a characterisation, observed green before; unmodified after)

All commands from `D:\Github\sse-2975`. Stop any running AppHost first (MSB3027).

**T000a adds (test-writer, observed green on unchanged code, commit `C0`):**

- `IdentityEndpointHost`: register `ListDevicesQueryHandler`, `ListKiosksQueryHandler` and
  `ListWebhookClientsQueryHandler` over one in-memory `IRegisteredClientQuerySource` the tests can
  seed. `RegisteredClientProjection.ListAsync` ends in EF's `ToListAsync`, so the source must be
  async-capable; port the existing pattern
  (`tests/Identity.Application.Tests/Fakes/InMemoryRegisteredClientQuerySource.cs` +
  `TestAsyncEnumerable.cs`) into `tests/Architecture.Tests` rather than referencing that test
  project. Expose a `Mock<IRegisteredClientQuerySource>` or a counting wrapper so "never read" is
  assertable.
- `DeviceAndKioskEndpointCharacterisationTests` (for `/devices/` and `/kiosks/`) and
  `WebhookRotationEndpointCharacterisationTests` (for `/webhook-integrations/`): the six spec §2
  scenarios per route, 18 cases in all (a `[Theory]` over the route + code is fine). The 400 body is
  asserted as the exact JSON **observed** on unchanged code (via `IdentityEndpointHost.Problem`,
  with or without the `type` member as observed). The 200 cases assert the returned client
  identifiers, not only the count, so a wrong fab set fails.
- Counterfactuals, run once each and reported, not committed: (1) change `"KIOSK_INVALID_INPUT"`
  to `"DEVICE_INVALID_INPUT"` in `KiosksEndpoints.List`: the kiosk 400 case must go red; (2) pass
  `string.Empty` instead of `fabId` into `FabResolution.ResolveForReadAsync`: the narrowing and the
  guard-refusal cases must go red; (3) drop the `catch (ArgumentException)` skip: the
  unusable-group-skipped case must go red. Restore after each (and mind a restored file's old
  timestamp: rebuild with `--no-incremental`).

**In-process (no Aspire; the primary safety net):**

```sh
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~DeviceAndKioskEndpointCharacterisationTests|FullyQualifiedName~WebhookRotationEndpointCharacterisationTests|FullyQualifiedName~IdentityRouteTableCharacterisationTests|FullyQualifiedName~CameraCatalogRouteTableCharacterisationTests"
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~PreconditionDeclarationTests|FullyQualifiedName~EndpointScopeDeclarationTests|FullyQualifiedName~IdempotentKeyReuseDeclarationTests|FullyQualifiedName~RouteValueRefusalDeclarationTests|FullyQualifiedName~StatusProducerDeclarationTests|FullyQualifiedName~ConcurrencyConflictDeclarationTests|FullyQualifiedName~HandlerDeconstructionTests|FullyQualifiedName~BoundaryTests"
dotnet test tests/Architecture.Tests -c Release
dotnet test tests/Identity.Application.Tests -c Release
```

Each `--filter` run must report a non-zero test count (a filter matching nothing exits 0, #2289).
Before T000a the first run counts 19 + 15 + 2 + 1 test methods; after it, those plus the new ones.

**Aspire fixture (CI's shard 2 is authoritative; run locally only if the stack is free, one
machine, one stack):**

```sh
dotnet test tests/Integration.Tests -c Release --filter "FullyQualifiedName~SmartSentinelEye.Integration.Tests.Identity.CrossFabListIntegrationTests"
```

No new test class is added, so no shard-filter entry is owed.

**Gate measurement** (spec §1 command, then):

```sh
grep -oE '[A-Za-z]:[^ (>]+\([0-9]+,[0-9]+\): warning S[0-9]+' metrics.log | sort -u | grep -E 'src.(CameraCatalog|Identity).Api'
git diff --stat C0 -- tests/                                              # must be empty
git diff 38eb836f -- src/Identity/Api/IdentityFabResolution.cs            # signature + 1 line + record + 1 doc ref
```

Before: 8 S107 in scope (spec §1), no other Sonar rule. After: 7 (H1-H7), **no S138, S104, S1541 or
S134** in either Api project, no new warning of any rule; solution-wide 109.

## 5. Risks

- **A tenancy regression hidden in a shape change.** Mitigated by the textual-identity rule (§2.1),
  the T000a suite covering every outcome per route, and `/security-review`.
- **The wrong code at a call site.** The three literals stay where they are; the per-route 400 test
  fails on a swap (T000a counterfactual).
- **A host change that disturbs existing tests.** T000a must show the existing 34 behaviour tests
  and both route-table tests green with the extended host before any production change.
- **A new S138 on a `List`.** +3 lines from ~20; if one appears, the call is not restructured or
  suppressed, the slice is reported back.
- **Spec-number race.** Re-check `specs/` on `origin/develop` and open PR heads before the PR (381 is
  in flight).
