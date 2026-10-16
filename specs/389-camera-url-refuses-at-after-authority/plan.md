# Plan 389: Camera URLs refuse a raw `@` after the authority on write, and stay readable on read

**Spec**: [spec.md](./spec.md) · **Tasks**: [tasks.md](./tasks.md) · **Issue**: #2999

## 1. Constitution and ADR check

| Gate | Result |
|---|---|
| §II primitives on domain models | Pass. `UrlAuthority` is a static class in `Shared.Kernel` with a `bool`-returning method and no state; the value objects keep their single `Value`. `PrimitiveBoundaryTests` unaffected. |
| §III no cross-context references | Pass. Both contexts already reference `Shared.Kernel`; no project reference added. The two value objects stay separate copies (as `StreamSourceUrl`'s doc comment requires); only the predicate is shared. |
| §IV latency | N/A. Control-plane writes only. |
| ADR-0105 guards | `Ensure.That(...).Satisfies(...)`; message carries no value. `UrlAuthority.AtSignFollowsTheAuthority` guards its argument with `Ensure.That(url).IsNotNull()`, as `ToOrigin` does. |
| ADR-0047/0089 errors | No new code. CameraCatalog 400 `CAMERA_INVALID_REQUEST` (via `BoundaryParse`); StreamDistribution `STREAM_INVALID_RTSP_SOURCE`. |
| ADR-0042/0088 Wolverine | No policy change. Default bounded policy relied on and characterised (§5). |
| ADR-0139/0144 | Red for new behaviour; characterisation for the extraction and for hydration. No ADR, no constitution change, no gate weakened. |
| ADR-0065 coverage | `UrlAuthority` covered through the `UrlRedaction*` suites plus direct rows; `FromStored` covered by direct unit rows (T040) as well as the converter tests. |
| New ADR needed? | **No.** `FromStored` reuses `BearerTokenHash.FromStored` (EventIngestion). |

## 2. Layers touched

| Context / project | Layer | Files |
|---|---|---|
| Shared.Kernel | — | **new** `src/Shared.Kernel/UrlAuthority.cs`; `src/Shared.Kernel/UrlRedaction.cs` (call the shared member; the private copy and its now-unused helpers removed) |
| CameraCatalog | Domain | `src/CameraCatalog/Domain/Camera/RtspUrl.cs` |
| CameraCatalog | Infrastructure | `src/CameraCatalog/Infrastructure/Persistence/Configurations/CameraConfiguration.cs` (line 99, read lambda only) |
| StreamDistribution | Domain | `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` |
| StreamDistribution | Infrastructure | `src/StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs` (line 73, read lambda only) |
| StreamDistribution | Application | `EventHandlers/CameraRegisteredIntegrationEventHandler.cs`, `EventHandlers/CameraAddressChangedIntegrationEventHandler.cs` — **comment only** (FR-006) |

No Api, Contracts, migration, AppHost, frontend or Helm change.

## 3. Shapes

### 3.1 `Shared.Kernel.UrlAuthority`

```csharp
public static class UrlAuthority
{
    // The parser takes an @ as the userinfo delimiter only while it precedes the first / ? # or \ ...
    public static bool AtSignFollowsTheAuthority(string url);
}
```

Move, do not rewrite: the body of `UrlRedaction.AtSignFollowsTheAuthority` (`UrlRedaction.cs:38-57`) and its
`AllSchemeCharacters` helper move verbatim, with the existing *why* comment. `AuthorityTerminators` and
`IsSchemeCharacter` are still needed by `UrlRedaction.AuthorityWasWrittenAsAscii`; expose them from
`UrlAuthority` as `internal` (same assembly) rather than duplicating them. The comment in `UrlRedaction` that
warns "do not unify the two" (strict `://` vs parser-style authority location); that comment now lives at `UrlAuthority.cs:11-12` and still holds.

Name kept from the decision ("`AtSignFollowsTheAuthority` ... a single public member"). The class is named for
what it inspects, not for redaction, because two of its three callers validate rather than redact.

### 3.2 Value objects (both identical in shape)

```csharp
public static RtspUrl From(string value)
{
    EnsureStoredInvariants(value)
        .Satisfies(url => !UrlAuthority.AtSignFollowsTheAuthority(url), AtSignAfterAuthorityMessage);
    return new RtspUrl(value);
}

public static RtspUrl FromStored(string value)
{
    EnsureStoredInvariants(value);
    return new RtspUrl(value);
}

private static EnsuredString EnsureStoredInvariants(string value) =>
    Ensure.That(value, nameof(value))
        .IsNotNullOrWhiteSpace()
        .HasMaxLength(MaximumLength)
        .StartsWith(RequiredScheme, StringComparison.OrdinalIgnoreCase)
        .Satisfies(HasNoUserInfo, "must not contain a user:password@ segment");
```

The exact refactoring shape is the engineer's (the sketch shows the invariant split, not mandated syntax), with
three fixed constraints: `From`'s checks run in today's order with the new one **last**; `FromStored`'s checks
are exactly today's `From`; the message is the FR-003 literal, declared once per value object. `EnsuredString`
is the `readonly struct` `Ensure.That(string, ...)` returns today (`src/Shared.Kernel/Ensure.cs:16,68`).

**Invariants after the change**

| Factory | Not blank | <= 2048 | `rtsp://` (any case) | no userinfo | no `@` after the authority |
|---|---|---|---|---|---|
| `From` (all writes) | yes | yes | yes | yes | **yes** |
| `FromStored` (EF read only) | yes | yes | yes | yes | no |

`FromStored` still throws for a row that today's `From` would reject (e.g. a blank). That is unchanged
behaviour: such a row could not be read today either, and no migration can have produced one.

### 3.3 Persistence

```csharp
.HasConversion(url => url.Value, value => RtspUrl.FromStored(value))          // CameraConfiguration:99
.HasConversion(url => url.Value, value => StreamSourceUrl.FromStored(value))  // StreamConfiguration:73
```

The model characterisation suites (`CameraCatalogModelCharacterisationTests`,
`StreamDistributionModelCharacterisationTests`) render "converted to String" and the committed snapshots do
not record the converter body, so both must pass unmodified, and no migration is generated. The engineer
confirms with `git diff --stat -- '**/Migrations/**'` showing nothing.

## 4. Messaging

No domain event, integration event or contract changes. `CameraRegisteredV1` / `CameraAddressChangedV1`
keep carrying the full URL (by design, issue #2999).

**Replay decision: dead-letter (keep the throw), not ack-and-log.** Grounded in how these handlers treat
invalid input today and why:

1. **It is already bounded.** No StreamDistribution chain has a failure rule, so Wolverine 6.48.3's default
   applies: the first failure goes to `MoveToErrorQueue`, with no retry (spec §1.4, pinned by C7). A misread address cannot loop.
2. **The error queue is the right record.** A legacy `CameraRegisteredV1` refused here means a catalogued
   camera with no stream. Ack-and-log would leave that as one Warning line; the error queue keeps the message
   itself visible. Replaying it fails the same way, moved to the error queue again (it still carries the old address). A
   corrected address arrives as a *new* `CameraAddressChangedV1`, which finds no stream to re-point
   (`StreamToRepoint` returns none), so recovery for such a camera is re-registration; recorded as risk R-2.
3. **Precedent.** Spec 203 kept the default on purpose for "a message that genuinely cannot be processed";
   ack-and-log in this context (`CameraRegisteredWithoutFab`, `IgnoreOlderRepoint`) is reserved for messages
   that are *correctly* not acted on, which a refused address is not.
4. **Scope.** Changing the handlers to ack would rewrite the contract pinned by
   `An_older_change_whose_address_this_context_rejects_fails_rather_than_being_ignored` and
   `On_command_failure_throws_so_Wolverine_redelivers` for every invalid source, not just this class — wider
   than the decision authorises.

Consequence accepted: a `CameraRegisteredV1` redelivered onto an *existing* stream row stored with a misread
address is now refused before the existing-row branch (validation precedes lookup, pinned by
`An_address_that_cannot_be_parsed_is_refused_before_any_lookup_with_the_reason_in_the_message`), so that
redelivery no longer re-asserts the MediaMTX path. The startup reconciler still restores missing paths from
the stored source (read via `FromStored`), so the stream is not lost. Recorded as risk R-3.

The Warning logged on refusal (`RepointAttemptFailed` / `ProvisionAttemptFailed`) carries the camera, the
code and the error message; the message is `RTSP source rejected: value: <FR-003> (Parameter 'value')`, which
holds no part of the address. No log template changes (ADR-0050).

## 5. Testing strategy

**Phase 4a colour: RED** for US1 and US3; **characterisation (observed green first)** for the extraction
(FR-001), the accept/old-reject sets (FR-004) and hydration (US2 / FR-005). Ambiguity resolved to red per
ADR-0144 where both apply (the value-object suites carry both kinds, in separate test classes).

### 5.1 Characterisation — captured green on `2159a482` before the rebase onto `b5a29905` (re-run after it), before any production edit, must pass unmodified

| ID | Test | Guards |
|---|---|---|
| C1 | existing `tests/Shared.Kernel.Tests/UrlRedaction*Tests.cs` (all) | FR-001 extraction preserves `ToOrigin` exactly, including the `?mail=a@b` over-refusal rows at `UrlRedactionUserInfoAsHostTests.cs:127-131` |
| C2 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAcceptSetCharacterisationTests.cs` | §5 accept set accepted; §5 old-reject set rejected with today's exact message |
| C3 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAcceptSetCharacterisationTests.cs` | same for `StreamSourceUrl` |
| C4 | **new** `tests/Architecture.Tests/Persistence/CameraUrlHydrationCharacterisationTests.cs` | the design-time model's `Camera.Url` converter `ConvertFromProvider` returns an `RtspUrl` with the stored value for every §5 hydrate row |
| C5 | **new** `tests/StreamDistribution.Infrastructure.Tests/StreamSourceUrlHydrationCharacterisationTests.cs` | same for `Stream.SourceUrl` |
| C6 | **new** `tests/Integration.Tests/CameraCatalog/StoredMisreadCameraAddressIntegrationTests.cs` (Aspire fixture; raw insert as `StaleIdempotencyReservationIntegrationTests` does) | `GET /cameras/{id}` and `GET /cameras` answer 200 for a row stored with `rtsp://admin:12345#@cam/s` — the running system, not just the converter |
| C7 | **new** `tests/StreamDistribution.Infrastructure.Tests/CameraEventFailurePolicyCharacterisationTests.cs` (mirrors `WallSceneSwitchFailurePolicyTests`: real handler discovery, `StubAllExternalTransports`) | the chains for `CameraRegisteredV1` and `CameraAddressChangedV1` carry no failure rule of their own, so the default bounded policy is in force; if the discovered graph exposes the combined rules, also assert every attempt (1, 2 and 3) yields `MoveToErrorQueue`. Pin: `CameraEventFailurePolicyCharacterisationTests` (`aec00bd7`) reaches into Wolverine internals by reflection, plus a source scan of StreamDistribution, because the module options lambda cannot run in a unit test |
| C8 | existing `RtspUrlTests`, `StreamSourceUrlTests`, `ProvisionStreamCommandHandlerTests`, `RepointStreamCommandHandlerTests`, `RepointStreamOrderCharacterisationTests`, `CameraRegisteredIntegrationEventHandlerTests`, `CameraRegisterEndpointCharacterisationTests`, `CameraPatchEndpointCharacterisationTests`, `CameraCatalogModelCharacterisationTests`, `StreamDistributionModelCharacterisationTests`, NetArchTest boundary suite, `PrimitiveBoundaryTests`, `HandlerDeconstructionTests` | unchanged contracts |

C6 needs a `shard-N.filter` entry in `tests/Integration.Tests/ci-shards/` (a missing entry fails
deterministically). C4/C5 are what turns red if FR-005 is forgotten; that is their counterfactual, run once by
the test-writer (temporarily tighten `From` locally without switching the converter → C4/C5 red; revert).

### 5.2 Red — new behaviour, observed failing before production code

| ID | Test | Red today because |
|---|---|---|
| R1 | **new** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAtSignAfterAuthorityTests.cs`: each §5 new-reject row throws `ArgumentException` whose message is exactly `value: <FR-003> (Parameter 'value')` and contains no part of the input | `From` accepts them |
| R2 | **new** `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAtSignAfterAuthorityTests.cs`: same | same |
| R3 | **new** `tests/Architecture.Tests/CameraUrlAtSignAfterAuthorityEndpointTests.cs` (existing `CameraCatalogEndpointHost`): `POST /cameras` and `PATCH /cameras/{id}` with the five issue forms → 400 `CAMERA_INVALID_REQUEST`, detail = rendered FR-003, body free of the secret, handler never invoked | today 201 / 204 |
| R4 | **new** `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyMisreadSourceReplayTests.cs`: real event handler + real command handler over the in-memory repository / gateway fakes (harness as in `CameraAddressChangedAdversarialOrderingTests`): (a) `CameraRegisteredV1` with a misread address throws `InvalidOperationException`, saves nothing, calls no gateway, one Warning containing `STREAM_INVALID_RTSP_SOURCE` and no secret; (b) a newer `CameraAddressChangedV1` with a misread address throws, stream keeps its address, no gateway call, no secret in any log | today both apply the address |
| R5 | **edited** `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressUserInfoLogRedactionTests.cs` (both facts) | see §6 |
| R6 | **edited** `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedAdversarialOrderingTests.cs` (three rows moved) | see §6 |

### 5.3 Guard — not red, proven by counterfactual

| ID | Test | Counterfactual |
|---|---|---|
| G1 | **new** `tests/Architecture.Tests/FromStoredCallSiteTests.cs` (FR-008): source scan of `src/**/*.cs`, slash-normalised, for `RtspUrl.FromStored` / `StreamSourceUrl.FromStored`; allowed only in the owning context's `Infrastructure/Persistence/Configurations/*Configuration.cs` and the declaring file | a permanent synthetic check (a fixture that must be flagged) replaced the temporary-call counterfactual; the scan also finds exactly the two allowed call sites. `fb7dd0c1` added `FromStoredCallerIlTests` (IL scan) because the text scan is bypassed by `using static` or split lines. |

### 5.4 Run commands

```sh
dotnet test tests/Shared.Kernel.Tests                                                       # C1
dotnet test tests/CameraCatalog.Domain.Tests --filter "FullyQualifiedName~RtspUrl"           # C2, R1, existing
dotnet test tests/StreamDistribution.Domain.Tests --filter "FullyQualifiedName~StreamSourceUrl"  # C3, R2, existing
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CameraUrl|FullyQualifiedName~CameraRegisterEndpoint|FullyQualifiedName~CameraPatchEndpoint|FullyQualifiedName~CameraCatalogModel|FullyQualifiedName~FromStoredCallSite"  # C4, R3, G1, C8
dotnet test tests/CameraCatalog.Application.Tests --filter "FullyQualifiedName~ChangeCameraAddress"   # R5, C8
dotnet test tests/StreamDistribution.Application.Tests --filter "FullyQualifiedName~CameraAddressChanged|FullyQualifiedName~CameraRegistered|FullyQualifiedName~LegacyMisreadSourceReplay|FullyQualifiedName~RepointStream|FullyQualifiedName~ProvisionStream"  # R4, R6, C8
dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~StreamSourceUrlHydration|FullyQualifiedName~CameraEventFailurePolicy|FullyQualifiedName~StreamDistributionModel"  # C5, C7, C8
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~StoredMisreadCameraAddressIntegrationTests"  # C6 (Docker; one Aspire stack per machine)
```

Expected red: R1/R2 `Should.Throw` "did not throw" for every new-reject row; R3 status 201/204 where 400 is
expected; R4 "did not throw" and a gateway call recorded; R5/R6 as in §6. A compile error is not an
acceptable red: R1-R6 reference only types that exist at `2159a482`.

## 6. Existing tests whose assertions change (authorised by the decision for exactly these rows)

Found by `grep -rniE 'rtsp:[^"]*@'` over `tests/`, `src/`, `apps/`, then each hit read to see whether its
URL reaches `RtspUrl.From` / `StreamSourceUrl.From`. Every other hit passes the URL as a raw string to
`UrlRedaction`, the audit handlers, the MediaMTX gateway or the reconciler fakes, none of which this spec
changes.

| File | Rows | Today asserts | After |
|---|---|---|---|
| `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressUserInfoLogRedactionTests.cs` | `Changing_the_address_to_a_cut_short_userinfo_logs_the_placeholder`, `Changing_the_address_to_a_cut_short_userinfo_still_stores_the_full_url` (`CutShortAddress = "rtsp://operator:2468#!@10.0.0.5:554/h264"` through `RtspUrl.From`) | the misread address is **accepted** and stored | the address never reaches the handler: `RtspUrl.From(CutShortAddress)` throws with the FR-003 message, the message does not contain `operator:2468`, and the seeded camera keeps its original address. Class summary updated to say so. |
| `tests/StreamDistribution.Application.Tests/EventHandlers/CameraAddressChangedAdversarialOrderingTests.cs` | `An_ignored_hostile_address_leaves_no_trace...` rows `rtsp://camera-sim:8554/x#@admin:SECRET-MARKER`, `rtsp://camera-sim/SECRET-MARKER@elsewhere`, `rtsp://camera-sim/x?u=admin:SECRET-MARKER@elsewhere` (lines 463-465) | the address is **accepted** (parsed) and then ignored as older | moved, unchanged, to the `InlineData` of `An_older_change_whose_address_this_context_rejects_fails_rather_than_being_ignored`, whose existing assertions (throws, no command-log entry, a Warning, no marker, stream unchanged) they then satisfy |

No other existing assertion changes. If a branch merged after `2159a482` (notably #3015 / #3014) adds tests
that pass such an address through the value objects, T001 finds them before the red run and they are handled
the same way, listed in the PR.

## 7. Risks

- **R-1 — a writer outside the enumerated four.** Mitigation: §1.3 enumeration by grep plus G1 for the
  reverse direction; the security review re-greps.
- **R-2 — a camera refused at provisioning has no recovery short of re-registration.** Pre-existing shape
  (any `STREAM_INVALID_RTSP_SOURCE` at provision does this today); window is the outbox drain at deploy and
  error-queue replays. Visible on the error queue. Not widened beyond this class.
- **R-3 — redelivery onto an existing legacy row no longer re-asserts the MediaMTX path.** Startup
  reconciliation restores it from the stored source. Accepted.
- **R-4 — `FromStored` is a bypass the type system was not used to prevent** (`internal` plus `InternalsVisibleTo` would have; `public` was chosen for the `BearerTokenHash.FromStored` precedent). G1.
- **R-5 — wording drift.** The FR-003 literal is duplicated in two value objects by design; R1/R2 pin the
  exact text in both, so drift fails a test.
- **R-6 — the owner's item-4 rationale vs item-1 instruction** (spec A-1). Surfaced in the PR body.
- **R-7 — stored misread rows remain readable in clear through the API/UI** until the masking issue
  (decision item 5) lands. Not made worse; recorded.
