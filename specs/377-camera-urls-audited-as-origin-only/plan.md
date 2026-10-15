# Plan 377: Camera URLs are audited as their origin only

**Spec**: [spec.md](./spec.md) | **Issue**: #2977 | **ADRs**: 0040, 0073, 0101, 0105, 0139, 0144, 0155

## 1. Bounded context and layers

**AuditObservability only**, Application layer for production code. No Domain, Infrastructure, Api,
migration or contract-shape change.

| Layer | Change |
|---|---|
| `Shared.Kernel` | none; reuses `UrlRedaction.ToOrigin` and `UrlRedaction.Unparseable` (spec 373) |
| `Shared.Contracts` | doc-only: `CameraAddressChangedV1` remarks no longer claim the audit records the full previous address |
| AuditObservability.Domain | doc-only: `AuditPayload` remarks name the redaction as part of how the payload is produced |
| AuditObservability.Application | **new** `EventHandlers/AuditPayloadRedaction.cs`; `IntegrationEventAuditHandler.AuditAsync` calls it instead of `JsonSerializer.Serialize`; `DTOs/AuditRowDto.cs` doc ("verbatim V1 JSON" becomes "V1 JSON, camera URLs reduced to origin") |
| AuditObservability.Infrastructure / Api | none (inherit) |

**Boundary rules.** AuditObservability.Application already references `Shared.Contracts` and
`Shared.Kernel` and nothing else cross-context (`BoundaryTests`, `AllowedCrossContext`). The
register names `CameraRegisteredV1` / `CameraAddressChangedV1` from `Shared.Contracts.CameraCatalog`,
which `IntegrationEventAuditHandler` already imports. No new project reference.

## 2. Mechanism: explicit register, guarded by full classification

**Chosen: an explicit per-type register in production plus an architecture test that classifies
every string field of every audited contract.** Rejected: reflection on field names at runtime
(any property whose name contains `Url`). Reasons:

- A name match is bypassed by naming. `StreamSourceUrl` reaching a contract as `Source`, `Address`
  or `Endpoint` would be stored in full, silently. `FabEventIngestedV1.Source` already shows that a
  name says nothing reliable about content.
- A name match also redacts things that are not URLs the moment someone names a field `UrlTemplate`,
  changing audit content nobody decided to change.
- The repo's own precedent for per-V1-type audit knowledge is explicit registers guarded by a
  coverage test (`V1ResourceMap` + `V1ResourceMap_covers_every_IIntegrationEvent`,
  `FabNeutralEvents`, `Every_integration_event_has_an_audit_handler`). This mirrors them.

**Why a new contract with a URL field cannot slip through.** The architecture test does not look for
URLs; it requires a decision for **every** `string`/`Uri` property, so a new field, however named,
fails the build until someone lists it either as a redacted URL field (production register) or as
verbatim (test inventory, which a reviewer sees in the diff). The cost is one line per new string
field on a contract, paid in the PR that adds it. A field nested in a `Shared.Contracts` record
(`LayoutTileV2`, `OverlayElementV3`, `ResolvedOverlayTextV3`, `EventMetadata`) is classified the same
way; the register supports top-level fields only, so a nested field classified as a URL fails the
test with "nested URL fields are not supported by AuditPayloadRedaction; extend it" rather than
passing unredacted.

### 2.1 `AuditPayloadRedaction` (Application/EventHandlers)

Shape (names indicative; the engineer mirrors `FabNeutralEvents` / `V1ResourceMap` style):

- `public static class AuditPayloadRedaction` (public so `Architecture.Tests` can reflect over it,
  like `V1ResourceMap`).
- `public static IReadOnlyDictionary<Type, IReadOnlyList<string>> UrlFields` — frozen:
  `CameraRegisteredV1 → [nameof(Url)]`, `CameraAddressChangedV1 → [nameof(PreviousUrl), nameof(Url)]`.
- `public static string Serialize(IIntegrationEvent message)`:
  1. `Ensure.That(message).IsNotNull()`.
  2. Type not in the register → return `JsonSerializer.Serialize(message, message.GetType())`
     **unchanged** (FR-006; byte-identical, so number/escape formatting of the other 22 types
     cannot drift).
  3. Otherwise apply the registered redactor: a `with` copy of the record whose registered fields are
     replaced by `UrlRedaction.ToOrigin(value)` (null stays null). The register and the redactor are
     one table, so a field cannot be listed without being redacted. Each delivery then checks the
     copy by reflection: a registered field that is absent, not a string, or not equal to the origin
     of the original **throws** `InvalidOperationException` naming the type and field, never the
     value (fail closed: Wolverine retries then dead-letters; no unredacted row is written).
  4. Serialise the copy normally (`JsonSerializer.Serialize(copy, type)`). A `JsonNode`
     round-trip is **not** used: it rewrites every `DateTimeOffset` `+` as `+` and so changes
     the bytes and `PayloadSizeBytes` of a payload that needed no redaction.
  5. For `CameraAddressChangedV1` only, append `,"DetailChanged":<bool>}` to the serialised text per
     spec FR-003, computed from the **typed original's** raw values.
- Property names in the payload stay PascalCase (default `JsonSerializer` options; existing queries
  use `payload->>'Camera'`, `payload->>'Url'`). `DetailChanged` follows the same casing.

`IntegrationEventAuditHandler.AuditAsync` line 75 becomes `Payload: AuditPayloadRedaction.Serialize(message)`.
Nothing else in the handler changes.

### 2.2 Payload JSON shape

`CameraRegisteredV1` (unchanged keys, value reduced):

```json
{"Camera":"…","Name":"north-gate","Url":"rtsp://cam","RegisteredAt":"…","RegisteredBy":"…","Metadata":{…}}
```

`CameraAddressChangedV1` (one key added, at the end):

```json
{"Camera":"…","Fab":"munich","PreviousUrl":"rtsp://cam","Url":"rtsp://cam","ChangedAt":"…","ChangedBy":"…","Metadata":{…},"DetailChanged":true}
```

**Backward compatibility for readers.** Additive only: no key is renamed or removed, value types are
unchanged (string, possibly the `<unparseable url>` placeholder). A reader of an old row sees the
full URL and no `DetailChanged`; a reader of a new row sees an origin and `DetailChanged`. Absence of
`DetailChanged` therefore means "written before spec 377", never "false". `PayloadSizeBytes` is
derived from the redacted content by `StoredPayload.From`, so it stays consistent. Old rows and blobs
are untouched (spec §7).

## 3. Entities, value objects, invariants

No new domain type. Invariants this plan relies on, all existing:

- `StoredPayload.From(content)` derives size from content: the size of a redacted payload is the
  redacted size, by construction.
- `AuditPayload` refuses empty: the serialised text of an object is never empty.
- `UrlRedaction.ToOrigin` is total over non-null input (placeholder for anything unparseable) and
  idempotent on its own output.

## 4. Messaging

No new domain event, integration event, queue or handler. The integration messages are consumed
exactly as today; the redaction is applied to the audit copy inside the existing audit handler. The
StreamDistribution consumer of `CameraRegisteredV1` / `CameraAddressChangedV1` still receives the full
URL (FR-002), which is asserted by leaving its tests untouched.

## 5. Tests (phase 4a: red, then adversarial)

Docker is down locally; CI is authoritative for `Integration.Tests`. Everything except §5.4 runs
without Docker.

### 5.1 Application unit tests (new class, red)

`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadRedactionTests.cs`, driving
the **real** `IntegrationEventAuditHandler` over `InMemoryAuditEventRepository` (the
`IntegrationEventAuditHandlerTests.Build` pattern), asserting on `repo.Committed[0].Payload.Content.Value`
parsed with `JsonDocument` **and** on the raw string (`ShouldNotContain("SECRET")`; use ASCII markers
so default JSON escaping cannot hide a leak):

- registration with `?token=SECRET` → `Url == "rtsp://cam"`, other fields unchanged, size matches.
- same-origin change → both `rtsp://cam`, `DetailChanged == true`, neither marker present.
- cross-origin change → two origins, `DetailChanged == false`.
- identical raw URLs → `DetailChanged == false`.
- unparseable / empty → placeholder; null → JSON null and a row is written.
- **API DTO sink:** `AuditRowMapper.Map(row).Payload` (internal; `InternalsVisibleTo` already grants
  this project) has no marker.
- **Characterisation, expected green before and after:** `CameraRenamedV1`, `FabEventIngestedV1`,
  `OverlayRevisionPublishedV3` (decimals) payloads equal `JsonSerializer.Serialize(message, type)`.
  Captured green on unmodified develop first, then must pass unmodified.

### 5.2 Architecture tests (new class, red)

`tests/Architecture.Tests/AuditPayloadRedactionCoverageTests.cs` (Architecture.Tests is not sharded):

- `Every_string_field_of_every_integration_event_is_classified_for_audit` — reflection over
  `Shared.Contracts` as in `BoundaryTests.Every_integration_event_has_an_audit_handler`; register read
  by reflection from `AuditPayloadRedaction.UrlFields`; verbatim inventory is a literal set of
  `"Type.Property"` strings in the test (about 46 today; **measured by the test's own enumeration,
  not by this plan**). Fails on unclassified, on doubly-classified, and on an inventory entry that no
  longer exists.
- `The_redacted_url_fields_are_exactly_the_camera_addresses` — pins the register to the three fields.
- **Counterfactual required** (prove-a-guard-by-counterfactual): the classification is a function of
  a type list, so a test-local `ProbeV1(string StreamEndpoint, LayoutTileProbe Tile)` fed to it must
  be reported unclassified; and temporarily deleting `PreviousUrl` from the register must turn the pin
  red. Quote both in the PR.

### 5.3 Archive sink (add one Fact to the existing harness)

`tests/Architecture.Tests/Persistence/AuditChunkArchiverCharacterisationTests.cs`: add
`An_archived_chunk_carries_the_payload_as_redacted_when_written`. Build the payload with the real
`IntegrationEventAuditHandler` (Architecture.Tests references AuditObservability.Application; a
minimal capturing `IAuditEventRepository` is local to the test), put the stored payload in the
harness's `DataTable` `payload` column, run the real archiver, gunzip, assert no marker and
`Url == "rtsp://cam"` inside the NDJSON line's `Payload`. Existing literals in this class do not
change. (This is green by inheritance once 5.1 passes; it is red before the change because the
handler wrote the full URL.)

### 5.4 Integration (CI, shard-4 — no new class, so no shard-filter edit)

`tests/Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.cs`:

- **Existing assertion that changes:** line 40 `audited.Url.ShouldBe(CorrectedUrl)` becomes
  `ShouldBe("rtsp://10.0.5.44")`, and `PreviousUrl` is asserted as `"rtsp://10.0.5.12"`. It changes
  because the stored value is now the origin by design (FR-001); this is the behaviour moving, so
  editing it is correct here (it is a red test of the new behaviour, not a characterisation).
- **New Fact** `A_query_string_credential_reaches_neither_the_audit_row_nor_the_audit_api`: register
  `rtsp://10.0.5.12/h264?token=SECRET-<guid>`, change to `rtsp://10.0.5.12/h264?token=SECRET2-<guid>`;
  poll `audit_events` for both kinds; assert `payload::text` has no marker, `payload->>'DetailChanged'
  = 'true'`; then `GET /audit/{auditIdentifier}` with an `sse.audit.read` client and assert the
  response body has no marker.

No other existing assertion is expected to change: `IntegrationEventAuditHandlerTests:56` asserts
the camera *name*; `EndToEndIngestion`, `UnresolvedFab`, `CrossFabReadGuard` search payloads for the
camera name or id; `V1ResourceMapTests` reads the object, not the payload.

### 5.5 Adversarial pass (test-adversary, after 5.1 is green)

Userinfo `rtsp://user:SECRET@cam/x`; path `rtsp://cam/SECRET/x`; query; fragment `#SECRET`;
query without path `rtsp://cam?SECRET`; percent-encoded marker; backslash `rtsp://cam\SECRET`;
`@` in path `rtsp://cam/x@SECRET`; uppercase scheme; IPv6 `rtsp://[fd00::5]:554/SECRET`; unicode host
`rtsp://kämera.example/SECRET` (assert no marker, whatever the host form); 2048- and 70000-character
URLs (on .NET 10 `Uri` parses the latter, so `ToOrigin` returns `rtsp://cam`, not the placeholder; no throw); whitespace-padded URL; null on either
field of the address change; both fields unparseable but different (`DetailChanged == true` by the
rule; confirm it leaks nothing but the bit).

### 5.6 Commands

```sh
dotnet test tests/AuditObservability.Application.Tests --filter "FullyQualifiedName~AuditPayloadRedaction|FullyQualifiedName~IntegrationEventAuditHandler"
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~AuditPayloadRedactionCoverage|FullyQualifiedName~AuditChunkArchiverCharacterisation|FullyQualifiedName~BoundaryTests"
dotnet test tests/Shared.Kernel.Tests --filter "FullyQualifiedName~UrlRedaction"
dotnet build -c Release   # analyzers as errors
# CI only (Docker): Integration shard-4, FullyQualifiedName~CameraAddressAuditIntegrationTests
```

## 6. Risks

- **Fail closed vs fail open.** Throwing on a missing registered field means an audit row is not
  written for that delivery (retried, then dead-lettered with its full body). Chosen deliberately: a
  missing row is visible in the dead-letter queue; an unredacted row is the defect being fixed. With
  `nameof` and default serializer options the branch is unreachable today.
- **Payload drift for other types** is prevented by not touching their serialisation (§2.1 step 2)
  and pinned by the characterisation tests.
- **Code metrics** (ADR-0084, advisory): `Serialize` stays under 30 LOC by splitting the
  `DetailChanged` computation into its own method.
