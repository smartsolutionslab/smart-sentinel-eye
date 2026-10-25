# Spec 459: Models become DTOs through extension methods

**Issue:** #3154 (feature-level, on Project #13) · **Branch:** `refactor/3154-dto-mapping`
**ADRs:** ADR-0093 (Application folder layout — DTOs live in `Application/DTOs/`), ADR-0036 (smallest change; a refactor
changes shape, not behaviour; no speculative generality), ADR-0139 (characterisation colour for a behaviour-preserving
change; a convention that fails the build rather than the review), ADR-0144 (lane), ADR-0087 (each commit builds on its
own), ADR-0091 (no shortcuts in new names), ADR-0109 (task parallelism). House rule: CLAUDE.md "Models become DTOs through
extension methods (#3154)" (merged at `c683329c`) — **this spec implements it; it does not decide it.**
**Lane:** autonomous (ADR-0144) · **Phase-4a colour:** **characterisation** for every mapping move (runtime behaviour
must not change); the new architecture guard is a new rule and is **observed red at the base commit** (it lists today's
sites) before it is committed green last.
**Security-sensitive:** low — three credential DTOs (`DeviceCredentialsDto`, `KioskCredentialsDto`,
`WebhookClientCredentialsDto`) carry a client secret; the mapping moves verbatim, and the exact-JSON endpoint
characterisations pin the bodies. `security-reviewer` recommended for the Identity commit only.
**Latency budget:** N/A. One site is read by the kiosk reconnect path (`GetOverlaySnapshotQueryHandler`, event → overlay
state leg, reconcile read only); the mapping performs the same in-memory construction in a different method — no I/O,
no extra allocation. No leg is affected.
**New ADR needed:** no — the decision is the merged house rule; ADR-0093 already places DTOs. (The issue cites
"ADR-0054 style" for the guard; ADR-0054 is test-data builders. The governing record for a build-failing source scan is
ADR-0139 — corrected here, not a new decision.)
**Written 2026-10-25 against `develop` at `c683329c`.**

---

## Premise, checked at `c683329c`

1. **The four shapes the issue names exist.** Inline `new XDto(` in handlers (incl. two EF `Select`s), private
   `ToDto`/`ToPage` helpers, `internal static Map*` on handlers reused by other handlers, and two mapper classes
   (`AuditRowMapper`, `RuleMapper`). **Holds.**
2. **The issue's count understates.** It names ~27 explicit `new <Name>Dto(` sites in 22 files. Target-typed
   `=> new(` inside `Map`/`ToDto`/`CredentialsOf` helpers add more. Full inventory: plan §"Site inventory" — 35 DTO
   shapes across 9 contexts, with 3 construction sites in `src/Identity/Api` (replays).
3. **Two EF server-side projections, not one.** `ListCamerasQueryHandler.cs:80` (`CameraSummaryDto`) **and**
   `RegisteredClientProjection.cs:31` (`RegisteredClientSummaryDto`, shared by ListDevices/ListKiosks/ListWebhookClients).
   Both need the expression form. Every other site maps after `ToListAsync`/`FirstOrDefaultAsync` — plain extension.
4. **No test detects a projection silently becoming client evaluation.** Unit tests use LINQ-to-Objects fakes; EF
   client-evaluates a non-translatable top-level `Select` without error, so the Aspire tests would stay green while the
   SQL changed from a 7-column read to a full-entity read. A SQL-capture characterisation is required before the change
   (precedent: `tests/Architecture.Tests/Persistence/AuditReadScopeTranslationAdversarialTests.cs` — suppress-open +
   capture interceptors, offline, drives the real handler, no production seam).
5. **Field coverage is partial for 21 of 35 DTO shapes** (audit at `c683329c`, plan §"Coverage"). A mapping moved
   by hand can swap two same-typed fields (`ActorIdentifier`/`EventIdentifier`: both `Guid`; `TriggerSource`/
   `TriggerKind`: both `string`) with nothing turning red. Characterisation tests asserting every field with distinct
   values are written and observed green **before** any move.
6. **No mapping defect found.** Every site reads the matching member; positional constructors follow record order.
   Pinned oddities that are **preserved, not fixed**: device/webhook replays take `DeviceType`/`IntegrationName` from the
   retry body/route (pinned by `DeviceAndKioskEndpointCharacterisationTests.cs:209`,
   `WebhookRotationEndpointCharacterisationTests.cs:316`); `ListLayouts`/`ListOverlays` use `Single(Published)` and
   `PublishedAt!.Value` (throw if the one-published invariant breaks). **No split required** — no site is also a bug fix.

## Out of scope

- `SceneTargetWireMapping` (Automation): maps a value object to primitives for **both** `RuleMapper` and `RuleEvaluator`
  (a fan-out effect, not a DTO). Folding it into `RuleMappings` would make the evaluator depend on a DTO mapping. Kept
  as is; the issue's acceptance names only `AuditRowMapper`/`RuleMapper`.
- `RuleActionDto.For*` static factories in `DTOs/RuleDto.cs:65-72` — already beside the DTO; `RuleMappings` calls them.
- The `with { RtspUrl = UrlRedaction.ToOrigin(...) }` post-step in `ListCamerasQueryHandler` — redaction, not mapping;
  it stays in the handler, in memory, after the expression projection (ADR-0179 keeps `ToOrigin` out of SQL).
- Sorts on DTO fields after mapping (`ListEventTypes`, `ListSourceModes`, `ListWebhookIntegrations`, admin lists by
  `CreatedAt`) — stay in the handlers, after mapping, comparer unchanged.
- Changing any DTO's fields; mapping from `Shared.Contracts` messages; `Shared.Contracts` itself.

## User stories

Each context story is independently shippable (the convention holds for that context; others unchanged). US10 needs all
nine and lands last.

| ID | P | Story |
|---|---|---|
| US1 | P1 | CameraCatalog: `CameraMappings` (incl. `ToSummaryDtoExpression`), SQL byte-identical |
| US2 | P1 | Identity: `RegisteredClientMappings` (incl. `ToSummaryDtoExpression`) + `ReplayedClientMappings` (Api replays) |
| US3 | P2 | AuditObservability: `AuditRowMapper` + both `ToPage` folded into `AuditEventMappings` |
| US4 | P2 | Automation: `RuleMapper` folded into `RuleMappings`; `DryRunResultMappings` |
| US5 | P2 | EventIngestion: Event, RegisteredEventType, SourceMode, WebhookIntegration, DeadLetter mappings |
| US6 | P2 | LayoutComposition: `LayoutMappings`, `WallMappings` (consumers incl. two command handlers) |
| US7 | P2 | OverlayDesigner: `OverlayMappings` |
| US8 | P2 | StreamDistribution: `StreamMappings` |
| US9 | P2 | SystemVariables: `VariableMappings`, `PlaceholderResolutionMappings`, `ResolvedTextPairMappings` |
| US10 | P1 | Guard: a new `new <Name>Dto(` or bare-DTO helper in a handler/Api file fails the build |

As the maintainer of a query handler, I want every model→DTO translation in one `<Model>Mappings` file beside the DTOs,
so that a handler reads as orchestration and a new handler finds the one place its mapping belongs.

**Acceptance (Gherkin)**

```gherkin
Scenario: Happy — a context's mappings move and every observable answer is unchanged
  Given the characterisation net for <context> captured green at c683329c (plan §"Phase 4a")
  When its handlers call <Model>Mappings extensions instead of building DTOs
  Then the same tests pass with the same names and no assertion, fixture or InlineData changed
  And src/<context>/Application/Queries|Commands/Handlers contain no "new <Name>Dto(" and no private ToDto/ToPage/Map

Scenario: Happy — the two EF projections stay server-side
  Given the SQL captured for ListCameras and ListDevices/ListKiosks/ListWebhookClients at c683329c
  When the Select uses CameraMappings.ToSummaryDtoExpression() / RegisteredClientMappings.ToSummaryDtoExpression()
  Then the captured SQL text is byte-identical (same 7 / 9 projected columns, no full-entity read)

Scenario: Conflict — a move swaps two same-typed fields
  Given AuditRowDto.ActorIdentifier and EventIdentifier are both Guid
  When AuditEventMappings.ToAuditRowDto assigns them crosswise
  Then AuditRowDtoMappingCharacterisationTests fails naming the field
  And the step is a regression: stop and report verbatim; never edit the assertion

Scenario: Conflict — a projection becomes client evaluation
  Given the Select is changed to .Select(camera => camera.ToSummaryDto()) (an ordinary method)
  When ListCamerasProjectionSqlCharacterisationTests runs
  Then it fails because the captured SELECT now reads every camera column

Scenario: Bad request — a new handler builds a DTO inline
  Given a file under src/*/Application/**/Handlers/ containing "new CameraDto(" or "private static CameraDto ToDto("
  When DtoMappingConventionTests runs
  Then it fails and names the file and line
  And the same text in src/CameraCatalog/Application/DTOs/CameraMappings.cs is not a violation
  And the same text in a comment or string literal is not a violation

Scenario: Auth — credential DTOs keep their exact bodies
  Given the exact-JSON characterisations for register device, enroll kiosk and rotate webhook (keyed, unkeyed, replayed)
  When credentials are built by RegisteredClientMappings / ReplayedClientMappings
  Then every body, including client secret and the retry-body-sourced deviceType / route-sourced integrationName, is byte-identical
```

## Independent end-to-end test procedure

1. At `c683329c`, before any edit: run the characterisation filter (tasks T001) with `--logger trx`; quote totals;
   save the sorted test-name list. Run the new guard; quote its red listing of today's sites.
2. After each context commit: `dotnet build -c Release` clean (IDE0007/IDE0005/collection-expression rules hold); the
   same filter → same names, all passed. `git diff <base> -- tests/` shows only the permitted call-site rename
   (plan §"The one permitted test edit").
3. After the last commit: guard green; counterfactuals C1-C5 (plan) each red, then reverted; the two SQL captures
   byte-identical; `grep -rnE 'new [A-Z][A-Za-z]*Dto\(' src --include=*.cs | grep -v '/obj/' | grep -v 'Mappings.cs'`
   → empty; full unit + Architecture suites green; Integration suite green in CI (no runtime path changed, but the two
   projections run against real Postgres there).

## Locked choices

- Extension methods named `To<Name>Dto()` (or `ToDto()` where the DTO is the model's only one), in `internal static
  class <Model>Mappings` in `src/<Context>/Application/DTOs/<Model>Mappings.cs`, namespace = the DTOs' namespace.
  `ReplayedClientMappings` is `public` (called from `src/Identity/Api`); every other class stays `internal` — no new
  public surface.
- EF projections: `public static Expression<Func<Model, Dto>> To<Name>DtoExpression()`, lambda body moved **verbatim**
  (implicit conversions, `.Value` accesses and `== null ? null : (DateTimeOffset?)` ternaries kept exactly).
- Mappings are pure: no I/O, no `Result`, no logging, no injected services (the WHEP URL is computed by the handler and
  passed in as a string).
- Values the handler today takes from locals rather than the model (e.g. `clientId`, `fab` in `RegisterDevice`) become
  parameters, not model reads — strict preservation over tidiness. ADR-0084's 4-parameter limit is advisory.
- **One assumption, stated:** `DryRunResultDto` has no source model (it is a computed verdict). It is built by two named
  factories in `DryRunResultMappings` (`NoMatch()`, `Matched(string? evaluatedValue)`), not an extension method. This is
  the only non-extension mapping; the guard still holds (no `new` in the handler).
