# Plan 459: Models become DTOs through extension methods

**Spec:** [spec.md](spec.md) · **Issue:** #3154 · **Base:** `c683329c`
**Bounded contexts touched:** all nine that serve queries — Application layer only (`DTOs/`, `Queries/Handlers/`,
`Commands/Handlers/`), plus `src/Identity/Api` (three replay sites). No Domain, Infrastructure, migration, contract,
AppHost or frontend change. No cross-context reference is introduced (each `*Mappings` file references only its own
context's Domain + DTOs).

## Why no ADR

The rule is the merged CLAUDE.md house rule (#3154, `c683329c`); placement is ADR-0093's `Application/DTOs/`; the guard
is an ADR-0139 build-failing rule over a convention that already exists. The single judgement this plan makes that the
house rule is silent on — a DTO with no source model (`DryRunResultDto`) gets named factories — is recorded in spec
§"Locked choices" as an explicit assumption, not a new pattern for other contexts (it is the only such DTO).

## Shape

```csharp
// src/CameraCatalog/Application/DTOs/CameraMappings.cs
internal static class CameraMappings
{
    public static CameraDto ToDto(this Camera camera) => ...;                       // moved from GetCameraQueryHandler
    public static CameraAddressDto ToAddressDto(this Camera camera) => ...;         // GetCameraAddressQueryHandler:64
    public static Expression<Func<Camera, CameraSummaryDto>> ToSummaryDtoExpression() =>
        camera => new CameraSummaryDto(...);                                         // ListCamerasQueryHandler:80, verbatim
    public static CameraListPageDto ToCameraListPageDto(
        this IReadOnlyList<CameraSummaryDto> items, int count, int offset, int limit) => ...;
}
```

Signatures illustrate shape only; the engineer moves each body **verbatim** (argument order, named/positional form,
implicit conversions, `.Value` accesses, `ToString()` on enums, `!` operators, `UrlRedaction.ToOrigin` where it is today).

Rules for every move:
- **One file per aggregate** (child entities — `Revision`, `Tile`, `OverlayElement` — map in the aggregate's file).
  A DTO→DTO mapping (`ReplayedClientDto` → credentials) gets a file named for its source DTO.
- **Page/envelope DTOs** are an extension on the rows they carry (`rows.ToEventPageDto(nextCursor)`); the cursor and
  trim logic **stays in the handler**, inlined in `HandleAsync` where a private `ToPage` held it (the helper is removed;
  its lines move unchanged). Cursor computation is paging, not mapping.
- **Locals stay parameters.** Where the handler passes a local (`clientId.Value`, `fab.Value`, `integrationName`,
  `deviceType`, the Keycloak secret, `query.OverlayIdentifier`, `version`, `total/offset/limit`), the extension takes it
  as a parameter rather than re-reading the model.
- **Services stay in the handler.** `StreamHealthDto.WhepUrl` is computed by the handler (`whepUrls.For(stream.Path)`) and
  passed in: `stream.ToHealthDto(whepUrl)`.
- **Order of operations unchanged.** Post-mapping sorts (`OrderBy(dto => dto.Kind, StringComparer.Ordinal)`, etc.),
  the camera URL redaction `with`, and `Single(Published)` evaluation order are kept exactly.
- `internal static` everywhere except `ReplayedClientMappings` (`public`, Api caller). `var` / collection-expression /
  no-underscore / `Ensure.That` house rules apply to new code; extension `this` parameters on internal pure mappings
  take no guard (matching today's `Map` helpers, which have none — adding one is a behaviour change for a null input).

## Site inventory (all at `c683329c`)

| Ctx | DTO | Today | Becomes (in `Application/DTOs/`) | Callers to repoint |
|---|---|---|---|---|
| Audit | AuditRowDto | `AuditRowMapper.Map` | `AuditEventMappings.ToAuditRowDto(this AuditEvent)` | GetAuditEvent, SearchAudit, GetResourceTimeline; 10 test files (see below) |
| Audit | AuditPageDto | `ToPage` ×2, `SearchAudit:45` empty | `ToAuditPageDto(this IEnumerable<AuditEvent>, string? nextCursor)` | SearchAudit (+ empty page via `List<AuditEvent> none = []`), GetResourceTimeline |
| Automation | RuleDto / RuleActionDto | `RuleMapper.Map` / `MapAction` | `RuleMappings.ToDto(this Rule)` (calls existing `RuleActionDto.For*` + `SceneTargetWireMapping`) | GetRule, ListRules; delete `RuleMapper.cs` |
| Automation | DryRunResultDto | `DryRunRule:175,185` | `DryRunResultMappings.NoMatch()`, `.Matched(string?)` | DryRunRule |
| Camera | CameraDto | `GetCamera:51` | `CameraMappings.ToDto` | GetCamera |
| Camera | CameraAddressDto | `GetCameraAddress:64` | `ToAddressDto` | GetCameraAddress |
| Camera | CameraSummaryDto | EF `Select` `ListCameras:80` | `ToSummaryDtoExpression()` | ListCameras (`PageAsync`) |
| Camera | CameraListPageDto | `ListCameras:67` | `ToCameraListPageDto(count, offset, limit)` on summaries | ListCameras |
| Events | EventDto | `GetEventQueryHandler.Map` | `EventMappings.ToDto(this Event)` | GetEvent, ListEvents |
| Events | EventPageDto | `ListEvents:56` | `ToEventPageDto(this IEnumerable<Event>, string? nextCursor)` | ListEvents |
| Events | RegisteredEventTypeDto | `ListEventTypes:23` | `RegisteredEventTypeMappings.ToDto` | ListEventTypes |
| Events | SourceModeDto | `ListSourceModes:21` | `SourceModeMappings.ToDto` | ListSourceModes |
| Events | WebhookIntegrationDto | `ListWebhookIntegrations:29` | `WebhookIntegrationMappings.ToDto` | ListWebhookIntegrations |
| Events | DeadLetterDto | private `ToDto` `ListDeadLetters:73` | `DeadLetterMappings.ToDto` | ListDeadLetters |
| Identity | RegisteredClientSummaryDto | EF `Select` `RegisteredClientProjection:31` | `RegisteredClientMappings.ToSummaryDtoExpression()` | RegisteredClientProjection |
| Identity | DeviceCredentialsDto | `RegisterDevice:53` | `ToDeviceCredentialsDto(this RegisteredClient, ClientId, string deviceType, string deviceIdentifier, FabIdentifier, string clientSecret)` | RegisterDevice |
| Identity | KioskCredentialsDto | `EnrollKiosk:53` | `ToKioskCredentialsDto(this RegisteredClient, ClientId, FabIdentifier, string clientSecret)` | EnrollKiosk |
| Identity | WebhookClientCredentialsDto | private `CredentialsOf` `RotateWebhookClient:354` | `ToWebhookClientCredentialsDto(this RegisteredClient, ClientId, string integrationName, FabIdentifier, string clientSecret)` | RotateWebhookClient (keep the "read after SaveAsync" comment at the call site) |
| Identity | ReplayedClientDto | `ReplayRegisteredClient:85` | `RegisteredClientMappings.ToReplayedClientDto(...)` (locals as parameters) | ReplayRegisteredClient |
| Identity Api | Device/Kiosk/WebhookClientCredentialsDto | `DevicesEndpoints:198`, `KiosksEndpoints:194`, `WebhookRotationEndpoints:228` | `ReplayedClientMappings.ToDeviceCredentialsDto(deviceType, deviceIdentifier)`, `.ToKioskCredentialsDto()`, `.ToWebhookClientCredentialsDto(integrationName)` (public) | the three replay methods |
| Layout | LayoutDto / LayoutRevisionDto / TileDto | `GetLayoutQueryHandler.Map`, `MapRevision`, `MapTiles` | `LayoutMappings.ToDto`, `ToRevisionDto`, `ToTileDtos` (tile sort kept inside, verbatim) | GetLayout, ListLayouts |
| Layout | PublishedLayoutDto | `ListLayouts:45` lambda | `LayoutMappings.ToPublishedLayoutDto(this Layout)` (keeps `Single(Published)` + `PublishedAt!.Value`) | ListLayouts |
| Layout | WallDto | `GetWallQueryHandler.Map` | `WallMappings.ToDto(this Wall)` | GetWall, ListWalls, EditWallScenes, SwitchWallScene (×2) |
| Overlay | OverlayDto / OverlayRevisionDto / OverlayElementDto | `GetOverlayQueryHandler.Map`, `MapRevision`, `MapElement` | `OverlayMappings.ToDto`, `ToRevisionDto`, `ToElementDto` (no `OrderBy` added to elements) | GetOverlay, ListOverlays |
| Overlay | PublishedOverlayDto | `ListOverlays:24` lambda | `OverlayMappings.ToPublishedOverlayDto(this Overlay)` | ListOverlays |
| Stream | StreamHealthDto | `GetStreamQueryHandler.Map(stream, whepUrls)` | `StreamMappings.ToHealthDto(this Stream, string whepUrl)` | GetStream, ListStreams |
| SysVar | VariableDto | `GetVariableQueryHandler.Map` | `VariableMappings.ToDto` | GetVariable, ListVariables |
| SysVar | PlaceholderResolutionDto / ResolvedTextPreviewDto | private `ToDto`, `ResolveOverlayText:44` | `PlaceholderResolutionMappings.ToDto`, `ToResolvedTextPreviewDto(this IEnumerable<PlaceholderResolution>, string resolvedText)` | ResolveOverlayText |
| SysVar | ResolvedOverlayTextDto / ResolvedOverlaySnapshotDto | `GetOverlaySnapshot:59,61` | `ResolvedTextPairMappings.ToDto(this (string Template, string Resolved))`, `ToResolvedOverlaySnapshotDto(this IReadOnlyList<(string Template, string Resolved)>, <overlay id type>, <version type>)` | GetOverlaySnapshot |

Model type names are the aggregates in `src/<Context>/Domain/<Aggregate>/`; the engineer uses the existing alias where a
handler aliases one (`AuditEventEntity`, `EventAggregate`, `RegisteredClientAggregate`).

Deleted files: `src/AuditObservability/Application/Queries/Handlers/AuditRowMapper.cs`,
`src/Automation/Application/Queries/Handlers/RuleMapper.cs`.

## Coverage — what exists, what Phase 4a adds

Audited at `c683329c` per DTO field (tests asserting the field through the handler or endpoint; whole-record equality
counted only for scalar fields). **Fully covered, no new test:** AuditPageDto, DryRunResultDto, CameraDto,
CameraSummaryDto (values), CameraListPageDto, CameraAddressDto, EventPageDto, DeadLetterDto
(`ListDeadLettersMappingCharacterisationTests`), Device/Kiosk/WebhookClientCredentialsDto (handler + all three Api
replay sites, exact JSON), ReplayedClientDto, ResolvedTextPreviewDto, ResolvedOverlaySnapshotDto (scalars).

**Partial or none — characterisation added first, drive the existing handler, every field with a distinct value:**

| New test file | Handler driven | Fields/branches currently unasserted |
|---|---|---|
| `tests/AuditObservability.Application.Tests/Queries/Handlers/AuditRowDtoMappingCharacterisationTests.cs` | GetAuditEvent | ReceivedAt, ResourceKind, ActorIdentifier, ActorIsSystem, EventIdentifier, PayloadSizeBytes, SchemaVersion |
| `tests/Automation.Application.Tests/Queries/RuleDtoMappingCharacterisationTests.cs` | GetRule, ListRules | TriggerSource, TriggerKind, CreatedAt, CreatedBy, PublishedAt (non-null), ArchivedAt; null "other variant" action fields for SetVariableValue, HighlightOverlay, SwitchWallScene/Layout |
| `tests/EventIngestion.Application.Tests/Queries/EventIngestionDtoMappingCharacterisationTests.cs` | GetEvent, ListEventTypes, ListSourceModes, ListWebhookIntegrations | EventDto: OccurredAt, IngestedAt, Payload · RegisteredEventTypeDto: EventTypeId, RegisteredAt, RegisteredBy · SourceModeDto: SourceModeId, DeclaredAt, DeclaredBy · WebhookIntegrationDto: Identifier, DefaultKind, RegisteredAt, exact RevokedAt |
| `tests/Identity.Application.Tests/Queries/RegisteredClientSummaryMappingCharacterisationTests.cs` | ListDevices (projection is shared) | RegisteredClientIdentifier, RegisteredBy, LastRotatedAt, exact DisabledAt |
| `tests/LayoutComposition.Application.Tests/Queries/LayoutDtoMappingCharacterisationTests.cs` | GetLayout, ListLayouts | LayoutDto: CreatedAt, CreatedBy · LayoutRevisionDto: RevisionIdentifier, State, CreatedAt, CreatedBy, PublishedAt, ArchivedAt · TileDto: row-then-col order, null OverlayIdentifier · PublishedLayoutDto: tile spans |
| `tests/LayoutComposition.Application.Tests/Walls/WallDtoMappingCharacterisationTests.cs` | GetWall | Fab, ShowingSince |
| `tests/OverlayDesigner.Application.Tests/Queries/OverlayDtoMappingCharacterisationTests.cs` | GetOverlay, ListOverlays | OverlayDto: Name, CreatedAt, CreatedBy, revision order; admin list CreatedAt-desc · OverlayRevisionDto: RevisionIdentifier, RevisionNumber, CreatedAt, CreatedBy, PublishedAt, ArchivedAt · OverlayElementDto: Kind, Color, null Text/FontSizePx for a shape · PublishedOverlayDto: OverlayIdentifier, RevisionNumber, Elements, PublishedAt, case-insensitive name order |
| `tests/StreamDistribution.Application.Tests/Queries/StreamHealthDtoMappingCharacterisationTests.cs` | GetStream | LastSuccessAt; exact WhepUrl (not suffix) |
| `tests/SystemVariables.Application.Tests/Queries/SystemVariablesDtoMappingCharacterisationTests.cs` | GetVariable, GetOverlaySnapshot, ResolveOverlayText | VariableDto: TruthyLabel, FalsyLabel, CreatedAt, CreatedBy · ResolvedOverlayTextDto: Template · PlaceholderResolutionDto: null RenderedValue for non-Resolved, `BooleanLabels.Default` fallback |

**SQL-shape characterisation (nothing detects client evaluation today):**

| New test file | Drives | Asserts |
|---|---|---|
| `tests/Architecture.Tests/Persistence/ListCamerasProjectionSqlCharacterisationTests.cs` | real `ListCamerasQueryHandler` over `CameraQuerySource(CameraCatalogDbContext)` with `UseNpgsql(unused)` + suppress-open + capture interceptors | the captured page `SELECT` text equals the literal captured at `c683329c` (7 projected columns) |
| `tests/Architecture.Tests/Persistence/RegisteredClientProjectionSqlCharacterisationTests.cs` | real `ListDevicesQueryHandler` (shared projection) over the Identity query source, same interceptors | the captured `SELECT` text equals the literal captured at `c683329c` (9 projected columns) |

Pattern: `tests/Architecture.Tests/Persistence/AuditReadScopeTranslationAdversarialTests.cs` (`SuppressOpenInterceptor`,
`CaptureInterceptor` returning an empty `DataTable` reader). Offline, no Docker, no production seam, Architecture.Tests
already references both contexts' Infrastructure. Architecture.Tests has no shard filter; **no Integration.Tests class is
added, so no `ci-shards/*.filter` entry is needed** (confirm in T-verify).

Every new characterisation class must pass at `c683329c` **before** any `src/` edit, and pass **unmodified** after every
context commit. A field asserted with a value that is not distinct from a same-typed sibling does not count — the
test-writer picks distinct Guids/timestamps/strings per field so a crosswise assignment is red.

### The one permitted test edit

Ten existing test files call `AuditRowMapper.Map(x)` directly (as an accessor for the stored payload in redaction
tests), all in `tests/AuditObservability.Application.Tests/EventHandlers/`:
`AuditPayloadDetailChanged{Adversarial,BracketContent,Unknown,ZoneAdversarial,Zone}Tests`,
`AuditPayloadHostCharsetRedactionTests`, `AuditPayloadRedactionAdversarialTests`,
`AuditPayloadRedactionPostFixAdversarialTests`, `AuditPayloadRedactionTests`, `AuditPayloadUserInfoRedactionTests`.
Folding the mapper in removes that symbol, so the invocation `AuditRowMapper.Map(<expr>)` becomes
`<expr>.ToAuditRowDto()`. **Nothing else in those files may change** — no assertion, literal, fixture or member. They
reach the `internal` mapping through the existing `InternalsVisibleTo` (AuditObservability.Application csproj).
An eleventh file, `tests/Integration.Tests/AuditObservability/UnresolvedFabAuditRowIntegrationTests.cs:441`, names
`AuditRowMapper` in a doc comment only; that comment may be reworded, code hash unchanged.
Proved in T-verify: the comment-stripped diff of those files, with `AuditRowMapper.Map(X)` → `X.ToAuditRowDto()`
normalised, is empty. Any other test diff is a block.

## The guard — `tests/Architecture.Tests/DtoMappingConventionTests.cs`

Source scan in the style of `FromStoredCallSiteTests` / `HandlerDeconstructionTests` (ADR-0139), reusing
`RepositorySource.Root()` / `RelativePath` and `SourceMask.Apply(text, MaskStrictness.CommentsAndLiteralInteriors)`
(stated at the call site, per spec 190).

- **Corpus:** every `*.cs` under `src/*/Application/` and `src/*/Api/`, excluding `obj/`, `bin/`. Paths
  forward-slash-normalised (memory: a backslash literal is green on Windows, red on Linux CI).
- **Allowed:** a file whose path matches `src/<Context>/Application/DTOs/<Name>Mappings.cs`.
- **V1 — explicit construction:** `\bnew\s+[A-Z]\w*Dto\s*[({]` outside an allowed file.
- **V2 — target-typed local:** `\b[A-Z]\w*Dto\s+\w+\s*=\s*new\s*\(` outside an allowed file.
- **V3 — mapping helper in a handler:** in a file under `/Handlers/`, a member declaration whose return type is a bare
  DTO: `\b(private|internal|public)\s+static\s+[A-Z]\w*Dto\s+\w+\s*\(` (catches `ToDto`, `ToPage`, `Map*`,
  `CredentialsOf`, any `=> new(` helper). `Task<…Dto>` / `Option<…Dto>` / `Result<…>` returns are not matched.
- **V4 — a `*Mappings.cs` outside `Application/DTOs/`** is a violation (closes the rename loophole).
- **Facts:**
  1. `No_handler_or_endpoint_builds_a_dto` — real tree, violations empty; message lists `path:line`.
  2. `Every_context_that_serves_dtos_has_its_mappings_in_DTOs` — the set of `*Mappings.cs` paths equals the exact list
     from §"Site inventory" (non-vacuity: a scan that reads nothing fails here).
  3. `The_scan_reads_every_context_handler_folder` — at least one file under `Queries/Handlers/` for each of the nine
     contexts was read.
  4. Synthetic counterfactual cases (in-test sources, like `A_reference_from_a_command_handler_is_a_violation`):
     C1 `new CameraDto(` in a `Queries/Handlers/` file → violation; C2 `private static CameraDto ToDto(Camera camera) =>
     new(` in a handler → violation (V3); C3 the same text in `src/CameraCatalog/Application/DTOs/CameraMappings.cs` →
     none; C4 `// new CameraDto(` and `"new CameraDto("` → none; C5 `src/X/Application/Queries/Handlers/FooMappings.cs`
     with `new CameraDto(` → violation (V4).
- **Stated limit (in the class doc):** a DTO built by target-typed `new(` as an argument or collection element
  (`[new(a, b)]`) is not seen; V3 covers the helper-method form that every current site used.
- **Red first:** the test-writer runs fact 1 at `c683329c` and quotes the red list (expected: every site in
  §"Site inventory" that uses `new XDto(`, plus the V3 helpers). The class is committed **last**, after US1-US9, so no
  commit on `develop` is red (ADR-0087).

## Messaging / domain events

None. No domain or integration event, handler signature, route, status code or wire shape changes.

## Commit plan (ADR-0087: each builds and passes on its own)

0. `test(3154): characterise every DTO field and both EF projections before the mapping moves` — the 11 new test files.
1. `refactor(3154): camera DTOs map through CameraMappings` (US1)
2. `refactor(3154): registered-client DTOs map through RegisteredClientMappings and ReplayedClientMappings` (US2)
3. … one `refactor(3154): …` per context, US3-US9 (Audit commit includes the permitted test-call rename)
10. `test(3154): handlers and endpoints build no DTOs (architecture guard)` (US10)

## Risks

- **Expression move changes translation** — mitigated by the byte-identical SQL captures; the lambda is cut/pasted,
  including `client.Registration.At` (implicit conversion) vs `client.Registration.By.Value` asymmetry.
- **Swap during a positional move** — mitigated by distinct-value characterisation of every field.
- **Order drift** — post-mapping sorts and the tile sort stay where they are; element order gets no `OrderBy`.
- **Parallel engineers on one branch** — one index per branch (memory: worktrees isolate files, not commits). `[P]`
  marks file-disjointness; the orchestrator may still run US1-US9 sequentially in one worktree.
