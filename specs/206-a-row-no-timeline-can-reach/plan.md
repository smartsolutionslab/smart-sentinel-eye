# Plan — Spec 206, a row no timeline can reach

**Issue:** #2429 · **Branch:** `2429-wrong-guid-audit-timeline` · **Spec:** `specs/206-a-row-no-timeline-can-reach/spec.md`

**Phase-4a colour: RED overall, declared per story** (constitution §Testing, ADR-0139). US1 **red**, US3 **red** (US3's table *is* US1's red), US2 **characterisation observed green + a counterfactual**. US2 is a behaviour-preserving rewrite: the covering test is captured passing *before* the change and must pass **unmodified** after. An assertion that has to be edited is evidence the behaviour moved — **block, do not adjust**. A refactor that is also a bug fix is two issues, which is exactly why US1 (the fix) and US2 (the rewrite) are separate stories with separate commits, in that order.

**No new ADR.** Nothing decided here is new: `V1ResourceMap.Conventions.HandTweaks` is a documented extension point (`Conventions.cs:8-12`), `ResourceIdentifier` is documented to carry either a guid string or a business name (`ResourceIdentifier.cs:8-10`), and `Shared.Contracts` is already this context's sanctioned dependency (constitution §III, ADR-0040). US2 changes *how* an existing mechanism is spelled, inside one file, with no observable effect.

---

## Context and layers

**Bounded context: `AuditObservability`. One context, one layer (`Application`), plus tests.** Nothing in `Domain`, `Infrastructure` or `Api` changes; nothing in any other context changes; `Shared.Contracts` is **read, never written**.

| Layer | File | Change |
|---|---|---|
| `Application` | `EventHandlers/V1ResourceMap.Conventions.cs` | US1: two new hand-tweaks. US2: `BuildHandTweaks` rebound through the compiler; two dead entries removed. |
| `Application` | `EventHandlers/V1ResourceMap.cs` | US1: correct the comment at `:160-162`, which asserts the opposite of the code. **No code change.** |
| `Domain` | `AuditEvent/ResourceKind.cs` | US2: one comment on `Webhook` recording that it is currently unproducible. **No code change.** |
| Tests | `AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` | US3: the table. US1 + US2: it is the net for both. |
| Tests | `Architecture.Tests/BoundaryTests.cs` | US3: one doc-comment line pointing at the stronger guard. **No code change.** |

**Boundary rules, restated and checked rather than assumed:**

- `AuditObservability.Application` → `Shared.Contracts` already exists (`IntegrationEventAuditHandler.cs:4-11` has a `using` for every contracts namespace). US2 adds `using`s to a second file in the same project; **no new project reference**, so `BoundaryTests`' no-cross-context rule is untouched.
- `AuditObservability.Domain` must not depend on any infrastructure framework (`BoundaryTests.cs:195-209`). Untouched — the only `Domain` edit is a comment.
- No context references another context's projects. Unchanged.

**Entities and value objects — all pre-existing, none added, none modified:**

| Type | Kind | Invariant |
|---|---|---|
| `ResourceKind` | closed value object, 11 values | `From(string)` throws for anything outside `All`. **Not extended** — no `chunk`, and `Webhook` is not removed. |
| `ResourceIdentifier : StringValueObject` | value object | `Ensure.That(value).IsNotNullOrWhiteSpace().HasMaxLength(255)` (ADR-0105). A variable `Name` is well inside 255. |
| `V1Mapping` | record | `Option<ResourceKind>` + `Option<ResourceIdentifier>` — absence is modelled, not nulled (ADR-0141). |
| `V1MappingEntry` | `internal` record nested in `V1ResourceMap` | `(Kind, Func<object, ResourceIdentifier?>)`. Shape unchanged by US2; only its construction moves. |
| `AuditEvent` | aggregate | Built by `AuditEvent.From(envelope, mapping, clock, handlerEnteredAt)`. **Not touched** — the fix is upstream of it, which is why there is no schema or migration. |

**Constitution §II compliance:** the audit model already exposes only value objects. Nothing this plan adds puts a primitive on a domain model — `PrimitiveBoundaryTests` stays green without attention. The primitives handled here live on `Shared.Contracts` records, which §II exempts because they are the wire boundary (ADR-0040).

---

## Messaging

**No messaging change whatsoever.** Stated because a spec touching integration-event handling invites the question.

- No domain event is raised, renamed or handled.
- No integration event is added, versioned or reshaped. `OverlayHighlightRequestedV1` and `SystemVariableValueRequestedV1` are **read-only inputs** here; a `V<N>` bump (ADR-0073) would be required if their shape changed, and it does not.
- No Wolverine queue, subscription, retry or dead-letter policy changes (ADR-0088). The audit subscriber's per-module queue isolation is why this work is off the §IV hot path.
- No publisher changes. `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:95` — the sole publisher of `SystemVariableValueRequestedV1` — is not opened.

Flow, unchanged end to end: *Wolverine delivers `TV1` → `IntegrationEventAuditHandler.Handle(TV1)` → `AuditAsync` builds the `V1Envelope` from `EventMetadata` (ADR-0102) → `AuditingMessageHandler.HandleAsync` → **`V1ResourceMap.Lookup` (the only thing this spec changes)** → `AuditEvent.From` → repository upsert.*

---

## US1 — the two hand-tweaks

### The change

Two entries in `Conventions.BuildHandTweaks`:

| Contract | Kind | Identifier source |
|---|---|---|
| `LayoutComposition.OverlayHighlightRequestedV1` | `ResourceKind.Overlay` | `OverlayIdentifier` |
| `SystemVariables.SystemVariableValueRequestedV1` | `ResourceKind.Variable` | `Name` |

Plus the comment at `V1ResourceMap.cs:160-162`, which today reads:

> Fall back to the small allow-list of canonical property names for V1s whose first Guid property is *not* the aggregate id (e.g. it's an actor or a parent reference).

That case is exactly what `if (pick is null)` makes unreachable — the comment describes the *hand-tweak* hook, not the fallback. It must say what the code does: the allow-list runs only when the contract has **no `Guid` property at all**, and a contract whose first `Guid` is the wrong one needs a hand-tweak. Comments say *why*, and this one currently says something false, which is how the next reader re-introduces the bug.

### Why hand-tweaks and not a smarter picker

Three alternatives were considered and each breaks something that works.

| Alternative | Why rejected |
|---|---|
| Run `IdentifierPropertyNames` **before** the Guid scan | Breaks six correct mappings — `Name` is second in the list, so `CameraRegisteredV1`, `OverlayRevisionPublishedV1`, `LayoutRevisionPublishedV2` and the three guid-carrying `SystemVariable*` contracts would all re-pivot onto a business name (spec §*Where the issue is wrong* (2)). Six broken to fix two. |
| Exclude `CausingEventIdentifier` by name from the Guid scan | Fixes defect B only, by adding a *second* implicit name-based rule to a picker whose implicit rule is already the problem. Does nothing for defect A, whose Guid pick is correct. Trades a visible tweak for an invisible one. |
| Derive the kind from the identifier property's **name** (`OverlayIdentifier` → `overlay`) | Plausible for defect A and wrong everywhere else: `CameraRegisteredV1.Camera`, `LayoutRevisionArchivedV1.Layout` and `StreamHealthChangedV1.Camera` are named after the noun per ADR-0094, so `stream` would become `camera`. Replaces one brittle convention with another. |

The hand-tweak hook exists for precisely this — a contract whose resource shape the convention cannot see — and it is already used five times, once for the *identical* cross-context shape (`ResolvedOverlayTextChangedV1`). Using it is reuse, not new machinery. Smallest possible change (CLAUDE.md §Karpathy).

### Why `Name` for defect B, in one line

The contract carries no variable guid; SystemVariables addresses every route by `{name}` (`SystemVariableEndpoints.cs:97, 121, 133`); `ResourceIdentifier` is documented to carry a business name and already does for three Identity contracts. The resulting guid/name split within kind `variable` is pre-existing, strictly better than unreachable, and filed as follow-up — spec §*Out of scope* (1).

### Hot path

None. `BuildDefault` runs **once per process** into a `FrozenDictionary` (`V1ResourceMap.cs:62`). Two more startup entries; per-message cost is one frozen-dictionary probe, before and after.

---

## US2 — the registry binds through the compiler

### The change

`BuildHandTweaks` today resolves seven entries like this (`Conventions.cs:57-105`):

```csharp
Type? webhookRotated = Type.GetType("SmartSentinelEye.Shared.Contracts.Identity.WebhookIntegrationRotatedV1, SmartSentinelEye.Shared.Contracts");
if (webhookRotated is not null)
{
    map[webhookRotated] = new V1MappingEntry(DomainResourceKind.WebhookIntegration, PickByProperty(webhookRotated, "IntegrationName"));
}
```

Two of the seven `Type.GetType` calls return `null` and the guard eats it. `PickByProperty` has the same shape one level down: a missing property returns `_ => null` (`:117-121`), so a rename degrades to a silent null pivot rather than a failure.

After: a single generic helper, and one line per tweak.

```csharp
private static void Add<TEvent>(
    Dictionary<Type, V1MappingEntry> map,
    DomainResourceKind kind,
    Func<TEvent, object?> pick)
    where TEvent : IIntegrationEvent
    => map[typeof(TEvent)] = new V1MappingEntry(
        kind,
        instance => Identify(pick((TEvent)instance)));
```

```csharp
Add<DeviceRegisteredV1>(map, DomainResourceKind.Device, registered => registered.ClientId);
Add<KioskEnrolledV1>(map, DomainResourceKind.Kiosk, enrolled => enrolled.ClientId);
Add<WebhookIntegrationRotatedV1>(map, DomainResourceKind.WebhookIntegration, rotated => rotated.IntegrationName);
Add<ResolvedOverlayTextChangedV1>(map, DomainResourceKind.Overlay, changed => changed.Overlay);
Add<AuditChunkArchivedV1>(map, DomainResourceKind.Event, archived => archived.ChunkIdentifier);
// + US1's two
```

`Identify` is the *unchanged* tail of today's `PickByProperty`:

```csharp
private static ResourceIdentifier? Identify(object? raw) =>
    raw is null ? null : ResourceIdentifier.From(raw.ToString()!);
```

**`Identify` is deliberately byte-identical in behaviour to the existing tail, including its exposure**: a picked value whose `ToString()` is empty or whitespace makes `ResourceIdentifier.From` throw through `Ensure.That(...).IsNotNullOrWhiteSpace()`. That is true on `develop` today and stays true. Changing it would be drive-by error handling at a non-boundary (CLAUDE.md §Karpathy), and it would also make US2 stop being behaviour-preserving, which would cost it its characterisation colour. Not touched.

Naming follows ADR-0091 (no shortcuts) and the repo's convention of naming a lambda parameter after its subject rather than a single letter.

### What this buys, precisely

| Failure | Today | After |
|---|---|---|
| Tweaked type deleted or renamed | silent no-op; the map quietly loses a pivot | compile error |
| Tweaked type moved to another namespace | silent no-op | compiles (the `typeof` follows it) — correct, the name was never the point |
| Picked property renamed | `PickByProperty` → `_ => null`; the pivot becomes null and the row is written with no resource | compile error |
| Picked property retyped | unchanged behaviour via `ToString()` | unchanged — `object?` is deliberate, `ToString()` is the contract |
| A tweak for a type that never existed | ten lines that read as coverage | **cannot be written** |

The two `EventIngestion` webhook entries disappear because `Add<WebhookIntegrationRegisteredV1>` does not compile. That is the whole deletion — no separate removal step, no judgement call about whether they are "planned". If they are ever written, they get typed entries on the day the contracts land.

### `PickByProperty` is removed, not kept

After the rewrite nothing calls it. A private helper with no callers is dead code, and dead code that reads as capability is the defect this story is about. Removed.

### `ResourceKind.Webhook`

Becomes unproducible (the only two writers were the dead entries — verified by repo-wide grep: `ResourceKind.Webhook` appears on exactly three lines, its definition plus those two). It **stays in the vocabulary**: `ResourceKind.All` is a public closed list and the timeline endpoint 400s on anything outside it, so removal changes `GET /audit/webhook/x` from 200-empty to 400 for a reason unrelated to this fix. One comment on the property records the state; the remove-vs-keep decision is filed (spec §*Out of scope* (2)).

### Evidence: a counterfactual, run twice

US2 has no red test, and pretending otherwise would be the "wrong red" failure this repo has already recorded. Its evidence is constructed:

1. On `develop`: rename `WebhookIntegrationRotatedV1.IntegrationName` → `IntegrationLabel`, build, observe **the solution builds** and `Lookup` returns `ResourceIdentifier: None` for that contract. Revert. Quote the transcript.
2. After the rewrite: the same rename, build, observe **CS1061 naming the property**. Revert. Quote the transcript.

Both transcripts go in the PR body. **Ask the build, not the file** — a test that greps `Conventions.cs` for the absence of string literals would prove the design was written down, not that it holds.

---

## US3 — the table

### Shape

In `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`, one hand-written table plus two completeness assertions:

```csharp
// (contract type, expected kind, a factory that stamps a DISTINCT sentinel in
//  every property, the sentinel the mapping must return)
```

Each row builds its contract with **a different value in every field** and asserts the returned `ResourceIdentifier` equals the sentinel of the expected property. Concretely: every `Guid` field gets its own `Guid.CreateVersion7()`, every `string` field gets `"<PropertyName>-sentinel"`, every `DateTimeOffset` a distinct instant. So `OverlayHighlightRequestedV1` is built with `OverlayIdentifier = O`, `CausingEventIdentifier = E`, `O != E`, and the assertion is `ShouldBe(O.ToString())` **and** `ShouldNotBe(E.ToString())`.

**This is the property that matters: the assertion cannot pass by reading its own input.** The expected value is chosen by the table's author from the contract's shape; the actual value comes from the production map. Change the map and the assertion text does not change — it fails. That is the test this repo's standing lesson asks for, and it is the reason the table asserts a *value* and not a property name: a property name could be copied out of the same `Conventions.cs` the test is meant to police.

### Completeness, in both directions

```csharp
// 1. Every concrete IIntegrationEvent in Shared.Contracts has a table row.
//    A 21st contract fails here, naming itself.
// 2. Every table row names a type that V1ResourceMap.Default actually maps.
//    A row for a deleted contract fails here.
```

Direction 1 is what turns the table into a guard rather than a snapshot. It is the thing `V1ResourceMap_covers_every_IIntegrationEvent` was reaching for and could not express, because "has an entry" is satisfied by a wrong entry.

### The existing tests

- `V1ResourceMap_covers_every_IIntegrationEvent` (`BoundaryTests.cs:224-247`) **stays.** It guards a genuinely different thing — that the coverage hole is visible from the architecture suite, and it still runs when the Application test project does not. One line is added to its doc comment pointing at the table, so a reader of the weaker guard is not left believing it checks more than it does.
- The five existing facts in `V1ResourceMapTests` (`Camera_V1_maps_to_camera_resource_kind`, `Device_V1_picks_clientId_not_the_RegisteredClientIdentifier`, `Kiosk_V1_maps_to_kiosk_via_a_hand_tweak`, `AuditChunkArchivedV1_pivots_on_the_chunk_identifier`, `Lookup_of_a_non_IIntegrationEvent_type_returns_unmapped`) **stay unmodified.** They are the characterisation net for US2 alongside the table; deleting a test to reach green is one of the three things the autonomous lane may not do.
- `MappedTypes_covers_a_meaningful_subset_of_Shared_Contracts_V1s` (`:83-95`) asserts a floor of 10. The table subsumes it. **Left in place** — removing a weaker green test to tidy up is churn inside the same diff that adds the guard, and it does no harm.

### Why not in `Architecture.Tests`

`Architecture.Tests` reaches `Shared.Contracts` and `AuditObservability.Application` through `Assembly.Load` + `GetType(string)` and holds no project reference to either. A twenty-row table written there would be twenty type-name strings and twenty property-name strings — reintroducing, in the guard, the exact unverifiable-string failure US2 removes from production. The Application test project already references both, so the table is `typeof(...)` and constructor calls, checked by the compiler. This is a deliberate deviation from the issue's literal wording and is recorded in `spec.md` §US3.

### Coverage

Every production file touched is `Application`-layer, gate ≥ 80 % (ADR-0065). The table raises `V1ResourceMap` coverage substantially — it exercises every mapped branch. `Domain` (≥ 90 %) and `Shared` (≥ 90 %) are unaffected: the only `Domain` edit is a comment.

---

## Testing strategy

| Level | What | Where |
|---|---|---|
| Unit | The 20-row table + both completeness directions | `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` |
| Unit | The two defects specifically, with `ShouldNotBe(<the wrong guid>)` | same file — the rows carry it, no duplicate facts |
| Architecture | Existing coverage guard, doc comment corrected | `tests/Architecture.Tests/BoundaryTests.cs` |
| Integration | **None added.** | — |
| e2e | **None added.** | — |
| Manual | The phase-5 procedure in `spec.md` | `verification.md` |

**Why no integration test, stated rather than skipped.** The defect is entirely inside a pure, synchronous, dependency-free mapping function; an Aspire-fixture test (ADR-0103) would boot Postgres, RabbitMQ and Keycloak to assert a dictionary lookup, and would be slower, flakier and *weaker* than the table (it could cover two contracts, not twenty). The end-to-end claim — that the row reaches the timeline — is discharged by the phase-5 procedure against the real stack, observed by a person, which is what phase 5 is for. `GetResourceTimelineQueryHandlerTests` already covers the query side and needs no change: the query was never the bug.

**ADR-0054:** no AutoFixture. The sentinel factories are hand-written, one per contract, in the test file.
**ADR-0053:** sentence-style names with underscores, e.g. `Every_integration_event_pivots_on_the_resource_it_is_about`.
**ADR-0052:** xUnit + Shouldly. Moq is not needed — `V1ResourceMap.Default` has no dependencies.

---

## Review focus for phase 6

1. **Did the precedence order in `BuildConventionPicker` survive untouched?** The issue's title invites reversing it and reversing it breaks six mappings. If the diff touches `:158` or `:165-172`, that is a blocker.
2. **Is US2 genuinely behaviour-preserving?** Any edit to an existing assertion in `V1ResourceMapTests` between the US1 commit and the US2 commit means behaviour moved. Block, do not adjust.
3. **Do the table's assertions compare against sentinels the production code cannot have supplied?** If a row's expected value is read back from `V1ResourceMap` rather than written by hand, the row proves nothing.
4. **Is `Shared.Contracts` unmodified?** `git diff --stat src/Shared.Contracts/` must be empty. Changing a contract to carry a variable guid is the tempting fix for defect B and is explicitly out of scope.
5. **No new project reference.** `git diff` on any `.csproj` must be empty.
6. **No migration, no schema change, no backfill.** `git diff --stat` must show nothing under any `Migrations/` folder.
7. **ADR-0105:** no `ArgumentNullException.ThrowIfNull`, no bare `throw new ArgumentException`. No new guards are expected at all.
8. **Deconstruction rule:** no handler in this diff reads two or more fields of a record, so the destructure-first rule has nothing to bind to. Confirm rather than assume.
9. **Collection expressions** (`Dictionary<Type, V1MappingEntry> map = [];`) and **no leading underscore** on any new private field. The pre-existing `_map` in the test file is deliberately left — see `spec.md` §*Out of scope* (7) — so it should appear in no diff hunk.
