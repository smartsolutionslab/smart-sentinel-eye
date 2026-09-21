# Spec 206 — A row no timeline can reach

**Issue:** #2429 — *`V1ResourceMap`'s reflection picker chooses the wrong Guid for two integration events, so their audit rows are written but unreachable by any timeline query*
**Branch:** `2429-wrong-guid-audit-timeline`
**Phase-4a colour:** **RED overall** (behaviour-changing) — the fix changes what is written to the `resource_kind` / `resource_identifier` columns for two contracts, so a test that arrives green is a phase-4 failure (ADR-0139, constitution §Testing, CLAUDE.md §House rules). Constitution §Testing carries **two** obligations, and this spec splits cleanly across both, so the colour is declared **per story** rather than left to be resolved by the ambiguity rule:

| Story | Colour | Why |
|---|---|---|
| **US1** | **RED** | Two audit rows change their `(kind, identifier)`. New behaviour. |
| **US2** | **CHARACTERISATION, observed green** | A compiler-binding rewrite of `BuildHandTweaks` that deletes dead entries and reproduces the five live ones **identically**. Nothing observable moves. The net is US3's table, captured green *before* the rewrite and required to pass **unmodified** after — an assertion that has to be edited is evidence behaviour moved: block, do not adjust. Its *own* evidence is a **counterfactual**, not a red test (§US2 acceptance). |
| **US3** | **RED** | The table is the instrument that makes US1 red. It is written first and fails on exactly the two defects; that failure is US1's verbatim phase-4a evidence. |

Ordering follows from that: **US3's table is authored before US1's fix**, and US2's rewrite happens last, behind the green table.
**ADRs:** **ADR-0039 + ADR-0090** (Guid v7 typed IDs with the `Identifier` suffix — the *premise* the reflection picker rests on, and the premise that fails for these two contracts), **ADR-0040 + ADR-0073** (domain vs. integration events, `V<N>` suffix — why these contracts carry primitives and why the map has to reconstruct identity from them), **ADR-0102** (the common `EventMetadata` envelope the audit handler reads), ADR-0088 + ADR-0042 (Wolverine per-module queue isolation — why the audit subscriber is off the event→overlay hot path), ADR-0047 + ADR-0089 (`Result<T, Error>` / `ApiError` — the timeline query's failure surface, unchanged), ADR-0093 (Application folder layout), ADR-0091 + ADR-0094 (naming), ADR-0052 / ADR-0053 / ADR-0054 (xUnit + Shouldly, sentence-style test names, hand-written fixtures), ADR-0065 (coverage gates), ADR-0103 (integration tests via the Aspire fixture, no Testcontainers), ADR-0105 (`Ensure.That` guards), ADR-0109 (`[P]` parallel markers), ADR-0139 (new behaviour starts red), ADR-0141 (`Option<T>` — advisory; the map already uses it), ADR-0144 (autonomous lane), ADR-0037 (phases).
**Constitution:** §III (bounded-context isolation — `Shared.Contracts` is the only cross-context path; unchanged and not widened), §VII (observability — the audit trail *is* the record), §Testing (red for new behaviour).

**No new ADR is required.** Nothing about the layering, the contract shapes, the resource vocabulary, or any context boundary changes. `V1ResourceMap`'s hand-tweak hook already exists and is already documented as *"the only place that knows about per-V1 specifics"* (`V1ResourceMap.Conventions.cs:8-12`); two contracts that the convention cannot serve are exactly what it is for. The compiler-binding change in US2 replaces a string lookup with a typed one inside that same file — an implementation detail of an existing decision, not a new one.

**Severity: a silent, permanent hole in the audit trail.** Not a 500, not a dropped message — the row is written, the write path logs success, the coverage architecture test passes, and the row is then addressable by no query any operator can construct. `AuditObservability` exists to be the thing you consult when something went wrong; a resource pivot that points at the wrong resource is worse than a null one, because a null pivot at least shows up as `—` in the UI (`apps/management-web/src/features/audit/AuditPage.tsx:84`) while a wrong one reads as a filled-in, trustworthy answer.

---

## Problem

Every line number and record shape below was **re-read in the working tree at HEAD `c9018d80`**, not copied from the issue. The full-corpus sweep in §*The complete audit* was performed independently of the issue's claim of "two", and the two corrections in §*Where the issue is wrong* were found by that sweep.

### The mechanism

`src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs` builds one `V1MappingEntry` per concrete `IIntegrationEvent` in `Shared.Contracts` (`:106-131`) in this precedence:

1. `Conventions.HandTweaks` — an explicit `(Type → kind, picker)` entry (`:118-122`).
2. Otherwise, the namespace tail through `Conventions.NamespaceToResource` for the **kind** (`:136-149`), and `BuildConventionPicker` for the **identifier** (`:151-185`).

`BuildConventionPicker` at `:153-173`:

```csharp
PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

// Prefer the first Guid-typed property — every aggregate root in
// the platform identifies itself with a Guid v7 (ADR-0039 / 0090),
// so it's the most reliable signal across the V1 corpus.
PropertyInfo? pick = Array.Find(props, property => property.PropertyType == typeof(Guid));

// Fall back to the small allow-list of canonical property names
// for V1s whose first Guid property is *not* the aggregate id
// (e.g. it's an actor or a parent reference).
if (pick is null)
{
    foreach (string candidate in IdentifierPropertyNames) { ... }
}
```

The `IdentifierPropertyNames` allow-list (`:33-47`) is consulted **only when there is no `Guid` property at all**. The comment above the fallback describes a behaviour the code does not have: it says the fallback covers *"V1s whose first Guid property is not the aggregate id (e.g. it's an actor or a parent reference)"* — which is precisely the case the `if (pick is null)` guard makes unreachable. **The comment describes the fix; the code is the bug.** Correcting that comment is in scope (US1), because a comment that asserts the opposite of the code is how the next reader re-introduces this.

Whatever the entry resolves to, the write path is unconditional: `AuditingMessageHandler.HandleAsync` (`:55-57`) calls `resourceMap.Lookup(...)` and passes the result straight into `AuditEvent.From`. There is no validation step between the pick and the column.

### The reachability failure

`GetResourceTimelineQueryHandler` filters on exactly three columns (`:52-54`):

```csharp
IQueryable<AuditEventEntity> source = events.AuditEvents
    .Where(auditEvent => auditEvent.ResourceKind == resourceKindFilter)
    .Where(auditEvent => auditEvent.ResourceIdentifier == resourceIdentifierFilter)
    .Where(auditEvent => auditEvent.Fab == fabFilter);
```

and the endpoint is `GET /audit/{resourceKind}/{resourceIdentifier}` (`src/AuditObservability/Api/AuditEndpoints.cs:40`). `resourceKind` must be one of the eleven values in `ResourceKind.All` or the query fails 400 `AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND` (`handler :28-31`). So a row is reachable **if and only if** somebody can guess the exact `(kind, identifier)` pair the map chose. A pair that pins the wrong kind, or an identifier from a different aggregate's id-space, is reachable by nothing.

This is the exact failure `tests/Architecture.Tests/BoundaryTests.cs:217-221` names in its own doc comment:

> Catches the case where a new V1 lands without a resource pivot — **the audit row would still be written but the timeline endpoint would never surface it.**

The test asserts only that a type has **an** entry (`:241-246`: `unmapped.ShouldBeEmpty(...)`). A *wrong* entry satisfies it. Both defects below have been passing this guard since spec 009.

### The complete audit

All **20** concrete `IIntegrationEvent` implementations in `Shared.Contracts`, resolved by hand against the code above. (The issue's own sweep, and my first one, missed `LayoutRevisionPublishedV2` — a `find -name '*V1.cs'` does not match it. The corpus is not "the V1s"; it is `IIntegrationEvent`, and it already contains a V2.)

| # | Contract | Namespace → kind | Picked identifier | Verdict |
|---|---|---|---|---|
| 1 | `AuditObservability.AuditChunkArchivedV1` | *tweak* → `event` | *tweak* → `ChunkIdentifier` | deliberate — see §*Blessed* |
| 2 | `CameraCatalog.CameraAddressChangedV1` | `CameraCatalog` → `camera` | `Camera` | correct |
| 3 | `CameraCatalog.CameraRegisteredV1` | → `camera` | `Camera` | correct |
| 4 | `CameraCatalog.CameraRenamedV1` | → `camera` | `Camera` | correct |
| 5 | `CameraCatalog.CameraRetiredV1` | → `camera` | `Camera` | correct |
| 6 | `EventIngestion.FabEventIngestedV1` | `EventIngestion` → `event` | `EventIdentifier` | correct |
| 7 | `Identity.DeviceRegisteredV1` | *tweak* → `device` | *tweak* → `ClientId` | correct |
| 8 | `Identity.KioskEnrolledV1` | *tweak* → `kiosk` | *tweak* → `ClientId` | correct |
| 9 | `Identity.WebhookIntegrationRotatedV1` | *tweak* → `webhook-integration` | *tweak* → `IntegrationName` | correct |
| 10 | `LayoutComposition.LayoutRevisionArchivedV1` | `LayoutComposition` → `layout` | `Layout` | correct |
| 11 | `LayoutComposition.LayoutRevisionPublishedV2` | → `layout` | `Layout` | correct |
| 12 | **`LayoutComposition.OverlayHighlightRequestedV1`** | → **`layout`** | `OverlayIdentifier` | **DEFECT — wrong *kind*** |
| 13 | `OverlayDesigner.OverlayRevisionArchivedV1` | `OverlayDesigner` → `overlay` | `Overlay` | correct |
| 14 | `OverlayDesigner.OverlayRevisionPublishedV1` | → `overlay` | `Overlay` | correct |
| 15 | `StreamDistribution.StreamHealthChangedV1` | `StreamDistribution` → `stream` | `Camera` | deliberate — see §*Blessed* |
| 16 | `SystemVariables.ResolvedOverlayTextChangedV1` | *tweak* → `overlay` | *tweak* → `Overlay` | correct |
| 17 | `SystemVariables.SystemVariableArchivedV1` | `SystemVariables` → `variable` | `Variable` | correct |
| 18 | `SystemVariables.SystemVariableDefinedV1` | → `variable` | `Variable` | correct |
| 19 | `SystemVariables.SystemVariableValueChangedV1` | → `variable` | `Variable` | correct |
| 20 | **`SystemVariables.SystemVariableValueRequestedV1`** | → `variable` | **`CausingEventIdentifier`** | **DEFECT — wrong *identifier*** |

**The issue's count of two is exhaustive. Confirmed, not assumed.** No third contract is wrong, and the two candidates that *look* wrong (#1, #15) were chased to their addressing scheme and found reachable.

### Defect A — `OverlayHighlightRequestedV1` (`src/Shared.Contracts/LayoutComposition/OverlayHighlightRequestedV1.cs:22-27`)

```csharp
public sealed record OverlayHighlightRequestedV1(
    Guid OverlayIdentifier,
    int DurationMs,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier,
    EventMetadata Metadata) : IIntegrationEvent;
```

Published by Automation, consumed by LayoutComposition — hence the `LayoutComposition` namespace, hence `NamespaceToResource["LayoutComposition"] → ResourceKind.Layout`. But nothing about the event identifies a layout. Its subject is an overlay.

Row written: **`kind = layout`, `identifier = <overlay guid>`.**

- `GET /audit/overlay/<overlay guid>` — misses it (wrong kind).
- `GET /audit/layout/<layout guid>` — misses it (the identifier is not a layout guid, and no layout guid appears on the contract at all).
- `GET /audit/layout/<overlay guid>` — would find it, and nobody will ever construct that, because it is a category error.

`ResolvedOverlayTextChangedV1` already carries a hand-tweak for **this exact shape** — published from one context, subject in another (`V1ResourceMap.Conventions.cs:92-97`). This sibling was missed.

### Defect B — `SystemVariableValueRequestedV1` (`src/Shared.Contracts/SystemVariables/SystemVariableValueRequestedV1.cs:18-23`)

```csharp
public sealed record SystemVariableValueRequestedV1(
    string Name,
    string Value,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier,
    EventMetadata Metadata) : IIntegrationEvent;
```

Its **only** `Guid` is `CausingEventIdentifier`, documented on the contract itself (`:12-14`) as *"the `FabEventIngestedV1.EventIdentifier` that triggered the rule"* — an id from the `event` space, deliberately carried for correlation. The reflection picker takes it because it is the only `Guid`; `Name`, which is in the allow-list at `:36`, is never reached because the fallback is gated on `pick is null`.

Row written: **`kind = variable`, `identifier = <ingested event guid>`.**

- `GET /audit/variable/<variable guid>` — misses it.
- `GET /audit/variable/<variable name>` — misses it.
- `GET /audit/event/<event guid>` — misses it (wrong kind).

The row is written into a coordinate that exists in no addressing scheme the system has.

### Secondary — two hand-tweaks that resolve to nothing

`V1ResourceMap.Conventions.cs:76-89` registers tweaks for `SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRegisteredV1` and `...RevokedV1` by string, guarded with `if (… is not null)`. **Neither type exists.** `src/Shared.Contracts/EventIngestion/` contains exactly one file, `FabEventIngestedV1.cs`; a repo-wide grep for either name returns only these two lines. `Type.GetType` returns `null`, the guard swallows it, and the entries are never added.

This matters twice over:

1. It is dead configuration that **reads as coverage** — ten lines of code and a comment (*"Spec 006 webhook contracts are emitted from EventIngestion but pivot on a webhook integration name"*) describing a mapping that does not exist.
2. It makes `ResourceKind.Webhook` (`ResourceKind.cs:27`) **the only entry in the eleven-value vocabulary that nothing can ever produce.** Verified: the only references to `DomainResourceKind.Webhook` in the whole repository are these two dead lines and the property's own definition. The live webhook-integration mapping is `ResourceKind.WebhookIntegration` via `Identity.WebhookIntegrationRotatedV1`, which is correct.

The **class** of the defect is that `Type.GetType(string)` plus `GetProperty(string)` makes both the type name and the property name unverifiable at build time, and both failure modes degrade to a silent no-op (`PickByProperty` returns `_ => null` when the property is missing, `:117-121`). US2 closes the class; the two dead entries then cease to exist by construction.

### Where the issue is wrong, and it matters

**1. The title's diagnosis does not fit defect A.** "The reflection picker chooses the wrong Guid" is true of `SystemVariableValueRequestedV1` and **false** of `OverlayHighlightRequestedV1`: there the picker chooses the *right* Guid (`OverlayIdentifier`, the first positional parameter) and the **namespace convention** assigns the wrong kind. Two defects, one symptom, two different mechanisms. Recorded because it changes what a fix has to cover — and because it disposes of the fix that the title invites:

**2. Inverting the precedence — allow-list first, reflection second — would be a regression, not a fix.** `IdentifierPropertyNames` lists `"Name"` second (`:36`). Run it first and:

| Contract | Picks today (correct) | Would pick, allow-list first |
|---|---|---|
| `CameraRegisteredV1` | camera guid | `"north-gate"` |
| `OverlayRevisionPublishedV1` | overlay guid | the overlay's name |
| `LayoutRevisionPublishedV2` | layout guid | the layout's name |
| `SystemVariableDefinedV1` | variable guid | the variable's name |
| `SystemVariableArchivedV1` | variable guid | the variable's name |
| `SystemVariableValueChangedV1` | variable guid | the variable's name |

Six correct mappings broken to fix two. **The precedence order stays exactly as it is.** The issue's own *fix direction* (hand-tweak both) is right; only its title is misleading. This is stated at spec level so that phase 4 does not rediscover it by breaking the build.

**3. `Array.Find` over `Type.GetProperties` depends on an ordering the CLR does not guarantee.** The reflection contract is explicit that `GetProperties` returns members in no particular order. Defect A's pick is correct *today* only because the runtime happens to return metadata order and `OverlayIdentifier` is declared first; a runtime that returned `CausingEventIdentifier` first would turn defect A from "wrong kind" into "wrong kind *and* wrong identifier" with no source change. After US1 both affected contracts are hand-tweaked and no longer depend on the order, and US3's table pins the picked *value* for all twenty so a reordering anywhere in the corpus fails the build rather than silently rewriting the audit trail.

### Blessed — checked, and deliberately left alone

Both are recorded in US3's table with the reasoning, so the next reader meets a decision rather than a suspicion.

- **`StreamHealthChangedV1` → `stream` / `<camera guid>`.** The stream aggregate has its own `StreamIdentifier` (`src/StreamDistribution/Domain/Stream/StreamIdentifier.cs:11`), so `stream`/`<camera guid>` looks like the same mismatch. It is not: **the stream's public handle in the API is the camera id** — `GET /streams/{cameraIdentifier:guid}` (`src/StreamDistribution/Api/StreamEndpoints.cs:34`), and `Stream` holds `Camera` as a value-copied cross-context reference (`Stream.cs:44`). An operator looking at a stream has the camera id in hand, and `StreamIdentifier` appears in no route. The pivot matches the addressing scheme. Reachable; left as is.
- **`AuditChunkArchivedV1` → `event` / `<chunk guid>`.** There is no `chunk` kind in the vocabulary and adding one is a public-API change to a closed value object. The pairing is an explicit spec-009 hand-tweak with a stated reason (`Conventions.cs:99-104`). Anybody holding a chunk id can reach it. Left as is, now pinned.

---

## Fix direction — the decision

Three changes, in priority order. Each is independently shippable.

| | What | Why not deferred |
|---|---|---|
| **US1** | Two hand-tweaks + the backwards comment | The filed defect. Smallest possible change (CLAUDE.md §Karpathy): two dictionary entries, no signature, no contract, no schema, no precedence change. |
| **US2** | `BuildHandTweaks` binds types and properties through the **compiler**, not through strings | Deletes the two dead webhook entries *by construction* and makes the whole silent-no-op class impossible. Fixing the two instances by hand would leave the mechanism that produced them. |
| **US3** | The coverage guard becomes an asserted `(type → kind, identifier value)` table over all 20 | Without it US1 is two entries a future contract can drift past exactly as these two did. This is the change that makes the fix stick. |

### US1 — what the two entries say

```
OverlayHighlightRequestedV1   → ResourceKind.Overlay  / OverlayIdentifier
SystemVariableValueRequestedV1 → ResourceKind.Variable / Name
```

**Defect A's target is unambiguous.** `overlay`/`<overlay guid>` is the same `(kind, id-space)` pair that `OverlayRevisionPublishedV1`, `OverlayRevisionArchivedV1` and `ResolvedOverlayTextChangedV1` already write. The highlight event joins an existing timeline rather than founding a new one — which is the point: an operator asking "what happened to this overlay?" gets the publish, the resolved-text change **and** the highlight, in one query.

**Defect B's target needed a decision, and here it is, with the evidence.** The contract carries no variable guid, so `Name` is the only identity available short of changing a `Shared.Contracts` record — which would ripple across the Automation publisher (`src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:95`) and the SystemVariables consumer for a fix that does not need it. `Name` is also the **right** answer independently of that constraint: **SystemVariables addresses variables by name, not by guid, on every route it has** —

```
GET  /system-variables/{name}
PUT  /system-variables/{name}/value
POST /system-variables/{name}/archive
```
(`src/SystemVariables/Api/SystemVariableEndpoints.cs:97, 121, 133`)

— and `ResourceIdentifier` is a `StringValueObject` explicitly documented to carry *"a business name where the V1 uses one"* (`ResourceIdentifier.cs:8-10`), already doing so for three Identity contracts. A variable's name is its handle.

**The honest cost, stated rather than buried.** This puts `SystemVariableValueRequestedV1` under `variable`/`<name>` while the other three variable contracts sit under `variable`/`<guid>`. One kind, two identifier spaces, so `GET /audit/variable/oeeLine1` and `GET /audit/variable/<guid>` each return half the story. That split is **pre-existing** (it is what `ResourceIdentifier`'s "guid string or business name" design permits corpus-wide) and it is strictly better than today, where the row is in *neither* half and reachable from nothing. Unifying the `variable` id-space is a real question about four contracts and the UI that queries them, and it is **out of scope** — filed as a follow-up in §*Out of scope* (1) rather than smuggled in. US3's table makes the split visible in source, which is the difference between a known trade-off and an accident.

### US2 — the mechanism, not the instances

Today `BuildHandTweaks` resolves seven entries through `Type.GetType("…, SmartSentinelEye.Shared.Contracts")` and `type.GetProperty("PropertyName")`. Two of the seven silently resolve to nothing; a third would silently degrade to `_ => null` if anyone renamed a property. `AuditObservability.Application` **already references `Shared.Contracts` directly** — `IntegrationEventAuditHandler.cs:4-11` has a `using` for every one of its namespaces — so there is no boundary reason for the strings, and `Shared.Contracts` is the sanctioned cross-context path (constitution §III, ADR-0040). Binding through `typeof(...)` and a property lambda makes a deleted type, a renamed type and a renamed property all **build failures**. The two dead webhook entries are removed as part of it, because they cannot be written any other way.

`ResourceKind.Webhook` is left in the vocabulary — see §*Out of scope* (2).

### US3 — the guard asserts the answer, not the presence

`V1ResourceMap_covers_every_IIntegrationEvent` stays (it guards a real, different thing: that a new contract gets *some* entry). Alongside it, a hand-written table asserts, for **every** concrete `IIntegrationEvent`, the expected `ResourceKind` **and** which property the identifier came from — proven by constructing each contract with a **distinct sentinel per property** and asserting the mapping returns the sentinel belonging to the expected property. Asserting the *value* rather than a property name means the test cannot be satisfied by a map that merely records an intention; it has to pick the right field. Completeness is asserted in both directions, so a new contract with no table row fails.

**It lives in `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`, not in `Architecture.Tests`** — a deviation from the issue's literal wording, with a reason. `Architecture.Tests` deliberately reaches everything through `Assembly.Load` + reflection and holds no project reference to `Shared.Contracts`; a twenty-row table written there would be strings, which is precisely the failure mode US2 is removing. In the Application test project the types are `typeof(...)` and the expectations are compiler-checked. `BoundaryTests.cs`'s doc comment gains one line pointing at the stronger guard so a reader of the weaker one is not misled.

---

## Locked technical choices

| Concern | Choice | Source |
|---|---|---|
| Resource vocabulary | `ResourceKind` closed value object, eleven values; **not extended** | `ResourceKind.cs`, constitution §II |
| Resource identifier | `ResourceIdentifier : StringValueObject`, ≤ 255 chars, guid-string **or** business name | `ResourceIdentifier.cs:8-14` |
| Mapping registry | `V1ResourceMap` + `V1ResourceMap.Conventions` — the single place that knows per-V1 specifics | `Conventions.cs:8-12` |
| Precedence | Hand-tweak → namespace kind + Guid-first picker → allow-list. **Unchanged** | `V1ResourceMap.cs:118-173`, §*Where the issue is wrong* (2) |
| Cross-context path | `Shared.Contracts` only; no new project reference | constitution §III, ADR-0040, NetArchTest `BoundaryTests` |
| Absence | `Option<DomainResourceKind>` / `Option<ResourceIdentifier>` in `V1Mapping` — already in place | ADR-0141 |
| Guards | `Ensure.That(...)` — existing guards unchanged, none added | ADR-0105 |
| Errors | `Result<T, GetResourceTimelineError>` on the query — untouched | ADR-0047, ADR-0089 |
| Messaging | Wolverine, per-module queues; no queue, retry or dead-letter policy changes | ADR-0042, ADR-0088 |
| Tests | xUnit + Shouldly, sentence-style names, hand-written fixtures (no AutoFixture) | ADR-0052, ADR-0053, ADR-0054 |
| Integration tests | Aspire fixture if any are added; no Testcontainers | ADR-0103 |
| Coverage | Application ≥ 80 % — every touched production file is Application-layer | ADR-0065 |

---

## Latency-budget impact

**N/A — no leg of constitution §IV is touched.**

Stated rather than asserted:

1. The changed code runs in `AuditObservability`'s Wolverine subscriber, which has its **own** queue under per-module isolation (ADR-0088). It is a fan-out consumer of `OverlayHighlightRequestedV1`; the kiosk-facing SignalR push that *is* on the *Event → overlay state ≤ 200 ms* leg is LayoutComposition's separate consumer and is not modified, referenced or reordered.
2. `V1ResourceMap.Default` is a `static` built **once per process** (`:62`) into a `FrozenDictionary`. US1 adds two dictionary entries at startup; US2 changes how those entries are constructed at startup. Per-message cost is one frozen-dictionary probe before and after.
3. No contract shape, no message size, no serialisation, no queue topology changes.

**Evidence at phase 5:** the existing `NFR001_AuditIngestLatencyTests` and `NFR002_AuditSearchLatencyTests` (`tests/Integration.Tests/AuditObservability/`) must stay green and their figures quoted. Per the standing lesson *"measurement runs need repeating"*, run them twice and quote both.

---

## Assumptions

Marked explicitly, as unavoidable guesses must be (CLAUDE.md §Karpathy).

1. **There is no already-written bad data to repair, and none will be migrated.** This repository has no production deployment — the Aspire k8s publisher has never been run and no k8s package is referenced (CLAUDE.md §What lives where; `specs/047-the-decisions-we-made/audit.md:373`, issue #1015). Audit rows written by dev and CI stacks are disposable. **No backfill, no data migration, no EF migration of any kind is in scope**, and the decision is deliberately *not* being made here for a hypothetical future deployment. Should this system ever run in production before the fix ships, repairing the rows already written becomes a separate issue, and the repair is mechanical: the payload column carries the full serialised event (`AuditingMessageHandler` → `V1Envelope.Payload`), so the correct `(kind, identifier)` is recoverable per row from data already stored.
2. **`GetProperties` returns metadata order on the runtimes this ships to.** Assumed only for the *description* of today's behaviour in §*The complete audit*; the fix does not rely on it, and US3 pins the outcome so a change in ordering fails the build (§*Where the issue is wrong* (3)).
3. **`EventIngestion.WebhookIntegrationRegisteredV1` / `…RevokedV1` are not planned work.** No such types, no references outside the two dead lines, and the live webhook-integration path (`Identity.WebhookIntegrationRotatedV1` → `webhook-integration`) is complete. If they are in fact planned, US2 costs nothing: the entries would be written typed and compiler-checked when the contracts land.
4. **The `variable` timeline is queried by operators typing a handle into the audit UI's free-text field** (`AuditPage.tsx:123-124`) rather than by a deep link from a variable page. Checked: the management-web audit feature offers free-text `resourceKind` / `resourceIdentifier` inputs and no variable-page deep link exists. This is what makes `Name` the useful pivot for defect B; if a deep-link-by-guid ever lands, follow-up (1) becomes the resolution.

---

## User stories

### US1 (P1) — Two events land on the timeline of the thing they are about

**As** an operator investigating why an overlay flashed, or why a variable changed,
**I want** the highlight request and the value request to appear on that overlay's / that variable's timeline,
**so that** the audit trail answers the question it exists to answer instead of holding a row nothing can find.

Independently shippable: US1 alone fixes #2429 and merges without US2 or US3.

**Acceptance:** `OverlayHighlightRequestedV1` maps to `(overlay, <OverlayIdentifier>)` and appears on the same timeline as that overlay's publish and resolved-text rows; `SystemVariableValueRequestedV1` maps to `(variable, <Name>)`; neither maps to a `CausingEventIdentifier`; the other eighteen contracts are unchanged; the misleading fallback comment states what the code does.

### US2 (P2) — A hand-tweak that names a type that does not exist fails the build

**As** the maintainer who will one day rename a contract or a property,
**I want** the mapping registry to stop compiling,
**so that** the registry cannot go on describing a mapping it no longer performs.

Independently shippable: US2 alone removes the dead configuration and merges without US1 or US3.

**Acceptance:** `BuildHandTweaks` contains no type-name or property-name string literals; the two `EventIngestion` webhook entries are gone; the five live tweaks produce byte-identical mappings to today, proven by test; deleting or renaming any tweaked contract or its picked property is a compile error.

### US3 (P3) — The guard asserts which field was picked, not merely that something was

**As** the author of the twenty-first integration event,
**I want** the build to tell me which resource my event pivots on and refuse a guess,
**so that** a wrong entry stops being as green as a right one.

Independently shippable: US3 alone is valuable and can merge without US1 or US2 — though **landed before US1 it is red on the two defects, which is precisely the phase-4a demonstration**, and the tasks therefore order it inside US1's red step.

**Acceptance:** a table pins `(kind, source property)` for all 20 contracts, verified by distinct per-property sentinels; a contract absent from the table fails; a table row naming a type that is not mapped fails; changing any hand-tweak or the picker's precedence fails with a message naming the contract.

---

## Acceptance scenarios

Gherkin. `Given a <Contract> whose <field> is <value>` means an instance constructed with that field set and every other field set to a **distinct** value, so no assertion can pass by coincidence.

### US1 — the filed defect

```gherkin
Scenario: An overlay highlight is audited against its overlay, not against a layout
  Given an OverlayHighlightRequestedV1 whose OverlayIdentifier is O
    and whose CausingEventIdentifier is E, with O != E
  When V1ResourceMap.Default.Lookup is called for it
  Then the Kind is ResourceKind.Overlay
  And  the ResourceIdentifier is O
  And  the ResourceIdentifier is not E

Scenario: A variable value request is audited against the variable, not the triggering event
  Given a SystemVariableValueRequestedV1 whose Name is "oeeLine1"
    and whose CausingEventIdentifier is E
  When V1ResourceMap.Default.Lookup is called for it
  Then the Kind is ResourceKind.Variable
  And  the ResourceIdentifier is "oeeLine1"
  And  the ResourceIdentifier is not E.ToString()
```

### US1 — the timeline actually surfaces it (the failure the issue names)

```gherkin
Scenario: The highlight row joins the overlay's existing timeline
  Given an audit row from OverlayRevisionPublishedV1 for overlay O in fab "munich"
  And   an audit row from OverlayHighlightRequestedV1 for the same overlay O in fab "munich"
  When GET /audit/overlay/{O} is called with a token carrying sse.audit.read and fab munich
  Then both rows are returned, ascending by occurred_at
  And  on develop today only the first is returned

Scenario: The variable-value-request row is reachable by the variable's handle
  Given an audit row from SystemVariableValueRequestedV1 for name "oeeLine1" in fab "munich"
  When GET /audit/variable/oeeLine1 is called with a token carrying sse.audit.read
  Then the row is returned
  And  on develop today the response is an empty page
```

### US1 — conflict: the eighteen correct mappings do not move

```gherkin
Scenario Outline: Contracts that were already right stay right
  Given a <contract> constructed with a distinct value in every property
  When V1ResourceMap.Default.Lookup is called for it
  Then the Kind is <kind> and the ResourceIdentifier is the value of <property>
  Examples:
    | contract                     | kind                | property        |
    | CameraRegisteredV1           | camera              | Camera          |
    | LayoutRevisionPublishedV2    | layout              | Layout          |
    | LayoutRevisionArchivedV1     | layout              | Layout          |
    | OverlayRevisionPublishedV1   | overlay             | Overlay         |
    | ResolvedOverlayTextChangedV1 | overlay             | Overlay         |
    | SystemVariableValueChangedV1 | variable            | Variable        |
    | StreamHealthChangedV1        | stream              | Camera          |
    | FabEventIngestedV1           | event               | EventIdentifier |
    | AuditChunkArchivedV1         | event               | ChunkIdentifier |
    | DeviceRegisteredV1           | device              | ClientId        |
    | KioskEnrolledV1              | kiosk               | ClientId        |
    | WebhookIntegrationRotatedV1  | webhook-integration | IntegrationName |

Scenario: A camera event is not re-pivoted onto its name
  Given a CameraRegisteredV1 whose Camera is C and whose Name is "north-gate"
  When V1ResourceMap.Default.Lookup is called for it
  Then the ResourceIdentifier is C, not "north-gate"
  # Pins the precedence order the issue's title invites reversing.
```

### US1 — bad request and auth on the timeline are unchanged

```gherkin
Scenario Outline: The timeline's existing failure surface is untouched
  Given the audit timeline endpoint
  When it is called <call>
  Then the response is <status>
  Examples:
    | call                                            | status                                     |
    | with no bearer token                            | 401                                        |
    | with a token lacking sse.audit.read             | 403                                        |
    | with a token for another fab                    | 403                                        |
    | GET /audit/banana/anything                      | 400 AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND   |
    | GET /audit/overlay/{O}?cursor=not-a-cursor      | 400 AUDIT_TIMELINE_INVALID_CURSOR          |
    | GET /audit/overlay/{O}?pageSize=5000            | 400 AUDIT_TIMELINE_PAGE_SIZE_OUT_OF_RANGE  |
```

### US2 — the dead entries, and the class they belong to

US2 is **behaviour-preserving**, so its obligation is characterisation-green plus a counterfactual — not a red test. `Conventions` and `V1MappingEntry` are `internal`, and no `InternalsVisibleTo` exists or is added, so "enumerate the tweaks" is not assertable through the public surface and is not attempted; the table asserts the *outcome* instead, which is the thing that matters.

```gherkin
Scenario: The five live hand-tweaks are unchanged by the rewrite
  Given DeviceRegisteredV1, KioskEnrolledV1, WebhookIntegrationRotatedV1,
        ResolvedOverlayTextChangedV1 and AuditChunkArchivedV1
  When each is looked up
  Then each yields exactly the (kind, identifier) it yielded before the rewrite
  # US3's table, captured green beforehand and passing UNMODIFIED afterwards.
  # An assertion that has to be edited means behaviour moved: block, do not adjust.

Scenario: No hand-tweak can name a type that does not exist
  Given BuildHandTweaks after the rewrite
  When the source is read
  Then it contains no type-name or property-name string literal
  And  the two EventIngestion webhook entries are absent,
       because neither type exists to be named

Scenario: A renamed property is a compile error, not a null pivot
  Given the hand-tweak for WebhookIntegrationRotatedV1
  When IntegrationName is renamed on the contract and the solution is built
  Then the build fails
  And  the same counterfactual on develop today builds clean
       and the pivot silently becomes null
  # This IS US2's evidence: a construct-the-failure counterfactual, run twice
  # (once on develop, once after), both transcripts quoted in the PR.
  # A guard that reads the source proves the design was written down, not that
  # it holds — so the build is asked, not the file.
```

### US3 — the guard

```gherkin
Scenario: Every concrete IIntegrationEvent has an asserted kind and source property
  Given the 20 concrete IIntegrationEvent types in Shared.Contracts
  When the mapping table test runs
  Then every type has a row, and every row's type is mapped
  And  each assertion compares against a sentinel unique to the expected property

Scenario: A new contract without a table row fails the build
  Given a new IIntegrationEvent added to Shared.Contracts
  When the table test runs
  Then it fails naming the new type
  # Proven by counterfactual at phase 4.

Scenario: Reverting either US1 hand-tweak fails the table test
  Given the table test
  When the OverlayHighlightRequestedV1 tweak is removed
  Then the test fails reporting kind 'layout' where 'overlay' was expected
  # This is the phase-4a red, quoted verbatim in the PR.
```

---

## Independent end-to-end test procedure

Observable by a person against the real stack — not a test run (phase 5, ADR-0103).

1. Stop any running AppHost (one Aspire stack per machine), then boot it.
2. Mint a token from Aspire's **proxied** Keycloak endpoint, not the container's mapped port, with `sse.audit.read` and fab `munich`.
3. Create and publish an overlay; note its guid **O**. Define a system variable named `spec206Var`.
4. `GET /audit/overlay/{O}` → note the rows present (the publish row). **Record the count.**
5. Drive an `OverlayHighlightRequestedV1` and a `SystemVariableValueRequestedV1` by ingesting a fab event that fires an Automation rule with both effects — `POST /events`. **Create the event and then look; do not search trace history** (`list_traces` ignores `search`).
6. Observe, in order:
   - Aspire dashboard structured logs: two `Audited` lines from `AuditingMessageHandler` (`:62-67`), one reading `OverlayHighlightRequestedV1 … overlay … {O}` and one reading `SystemVariableValueRequestedV1 … variable … spec206Var`. On `develop` today the first reads `layout … {O}` and the second reads `variable … <a guid>`.
   - `GET /audit/overlay/{O}` → **one more row than step 4**, the highlight, on the same timeline as the publish. On `develop` today the count is unchanged.
   - `GET /audit/variable/spec206Var` → the value-request row. On `develop` today this is an empty page.
   - `GET /audit/layout/{O}` → **empty**. The row has left the layout timeline it never belonged on.
7. Confirm nothing else moved: `GET /audit/camera/{some camera guid}` still returns that camera's rows.
8. Run `NFR001_AuditIngestLatencyTests` and `NFR002_AuditSearchLatencyTests` twice in Release; quote all four figures.

**The before/after is the point of step 6.** The verification note must state what the same procedure produces on `develop` — and the note must be written down, not merely reported to the orchestrator (standing lesson: *"self-review catches contradictions, never omissions"*).

---

## Out of scope

Each was found while investigating; each is recorded so it is a decision, not an omission.

1. **Unifying the `variable` identifier space.** After US1, `variable` rows carry a guid from three contracts and a name from one. Resolving it means either changing three contracts to pivot on `Name` (which loses the guid a deep link would use) or changing `SystemVariableValueRequestedV1`'s shape in `Shared.Contracts` to carry the variable guid (an Automation → SystemVariables ripple). Both are larger than this fix and neither is needed to make the row reachable. **Recommend filing**, with §*Assumptions* (4) as the input. The same question exists, smaller, for `device`/`kiosk`, which pivot on `ClientId` while their contracts also carry `RegisteredClientIdentifier`.
2. **`ResourceKind.Webhook` becomes unproducible.** After US2 deletes the two dead entries, nothing in the system can write kind `webhook`; `webhook-integration` is the live one. `ResourceKind.All` is a **closed public vocabulary** — `GET /audit/webhook/x` answers 200-empty today and would answer 400 if the value were removed, which is an API behaviour change for an unrelated reason. Left in place, with a source comment recording that it is currently unproducible. **Recommend filing** to decide remove-vs-keep on its own merits.
3. **The `Guid`-first precedence in `BuildConventionPicker`.** Correct for 18 of 20 and actively load-bearing (§*Where the issue is wrong* (2)). Unchanged. Only its comment is corrected.
4. **`IdentifierPropertyNames` contains six entries that match no property in the corpus** — `Identifier`, `CameraIdentifier`, `LayoutIdentifier`, `RuleIdentifier`, `VariableIdentifier`, `WebhookIntegrationIdentifier`, `DeadLetterIdentifier`. (`RuleIdentifier` in particular: `NamespaceToResource` maps `"Automation" → rule`, but `src/Shared.Contracts/` has **no `Automation` folder** — Automation publishes into other contexts' namespaces and owns no integration event of its own. That dictionary entry is dead too.) Harmless — the list is a fallback that, after US1, runs for no contract at all. Pruning it is cosmetic and would churn a file this fix must keep reviewable. Noted, not touched.
5. **Backfilling or migrating already-written rows.** See §*Assumptions* (1). No EF migration, no data fix, no repair endpoint.
6. **Widening `ResourceKind` with a `chunk` value** for `AuditChunkArchivedV1`. See §*Blessed*.
7. **`V1ResourceMapTests` uses a leading-underscore field** (`_map`, `:19`), against CLAUDE.md §House rules. Pre-existing, in a file US3 extends. Renaming it is a behaviour-preserving churn that would obscure the diff carrying the new guard. Left; **recommend a sweep issue** rather than a drive-by here.
8. **`Option<T>` migration** of any signature touched (ADR-0141 is advisory; existing signatures are not a defect).
9. **The audit UI.** `AuditPage.tsx` renders `resourceKind / resourceIdentifier` verbatim and needs no change; no frontend file is touched by this spec.

---

## File contention

All inside `AuditObservability` and its tests. No project reference is added — `AuditObservability.Application` already references `Shared.Contracts`. NetArchTest boundary rules are unaffected; `Shared.Contracts` itself is **not modified**.

| File | Story |
|---|---|
| `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs` | **US1 + US2 — shared, serialise** |
| `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs` (comment at `:160-162` only) | US1 |
| `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs` (one comment on `Webhook`) | US2 |
| `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` | **US1 + US2 + US3 — shared, serialise** |
| `tests/Architecture.Tests/BoundaryTests.cs` (doc comment at `:217-221` only) | US3 |

`V1ResourceMap.Conventions.cs` and `V1ResourceMapTests.cs` are each touched by more than one story. **That is why all three stories live on one branch and why their phase-4 tasks are not `[P]` against each other** (ADR-0109). The `[P]` opportunities are named in `tasks.md`.
