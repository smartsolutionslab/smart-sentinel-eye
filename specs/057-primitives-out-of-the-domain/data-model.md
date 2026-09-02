# Phase 1 Data Model: Primitives out of the domain

**Feature**: 057 | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

No aggregate gains or loses a field. Every entry below is a **retyping** of an
existing property, and every one maps to the same column with the same type
and nullability. The schema after this feature is byte-identical to the schema
before it (SC-004).

---

## Correction to the spec's count

The spec says **13** domain string properties. The accurate figure for
*aggregate* properties is **9**.

The original survey counted 12 `public string` declarations under `Domain/`,
three of which are a value object's own backing value —
`CameraName.NormalizedValue`, `Payload.Value`, `BearerTokenHash.Value`. Those
are correct as they stand: a string-backed value object must expose its
string somewhere, and that is the boundary the rule protects, not one it
violates. The remaining count differences came from `string?` being tallied
separately.

The timestamp figure was exact: **26**, confirmed property by property.

This is recorded rather than quietly fixed because a count that shrinks
without explanation is indistinguishable from work being dropped.

---

## Convention: how nullability is expressed

**Nullable persisted properties keep a nullable reference; they do not become
`Option<T>`.**

ADR-0048 mandates `Option<T>` "everywhere", which would suggest
`Option<StreamError>`. That reading is wrong for persisted state, and the
repository already says so: `StreamConfiguration.cs:10-11` documents "an
ADR-0048 carve-out documented in `Stream.cs`" for exactly these properties,
and `Option<T>` appears in **zero** EF configurations across all eleven
mappings.

The established shape is a nullable value-object reference —
`Stream.Fab` is already `FabIdentifier?` on a persisted aggregate. New
nullable types follow it.

`Option<T>` remains correct where it is already used: 32 sites in the domain,
all of them in-memory mappings and return values rather than mapped columns.

---

## Text types (Story 2)

Nine properties. Each becomes a `record` deriving from the existing
`Shared.Kernel.Primitives.StringValueObject`, validating in its `From(...)`
factory via `Ensure`.

| Context | Property today | New type | Null? | Invariant | Column |
|---|---|---|---|---|---|
| Automation | `Rule.TriggerSource` | `TriggerSource` | no | non-empty, ≤ 16 | `varchar(16)` |
| Automation | `Rule.TriggerKind` | `TriggerKind` | no | non-empty, ≤ 128 | `varchar(128)` |
| EventIngestion | `DeadLetter.Topic` | `DeliveryTopic` | no | non-empty, ≤ 256 | `varchar(256)` |
| EventIngestion | `DeadLetter.Error` | `RejectionReason` | no | non-empty, ≤ 512 | `varchar(512)` |
| EventIngestion | `DeadLetter.RawPayload` | `RawPayload` | no | non-empty only | `text` |
| EventIngestion | `WebhookIntegration.KeycloakClientId` | `KeycloakClientIdentifier` | yes | non-empty when present, ≤ 255 | `varchar(255)` |
| AuditObservability | `AuditEvent.ActorUsername` | `ActorUsername` | yes | non-empty when present, ≤ 255 | `varchar(255)` |
| AuditObservability | `AuditEvent.Payload` | `AuditPayload` | no | non-empty only | `jsonb` |
| StreamDistribution | `Stream.LastError` | `StreamError` | yes | non-empty when present, ≤ 1024 | `varchar(1024)` |

### The length limits move, and that is the point

Every length above is currently enforced **only in the EF configuration** —
that is, only at the moment of writing to the database, as a
`DbUpdateException` from Postgres rather than a refusal from the domain. A
17-character trigger source is constructible today and fails at the far end
of the request.

Moving the bound into the value object means the domain refuses it at
construction. The EF `HasMaxLength` stays exactly as it is, so the column is
unchanged; it simply stops being the only thing that knows the rule.

### The two opaque payloads

`RawPayload` and `AuditPayload` are the exemptions from the spec's
Assumptions. They are exempt from being **parsed or interpreted** — the
content is captured verbatim for post-mortem and the system must not care
what is in it. They are not exempt from *having a type*, so both get one that
enforces non-emptiness. Neither gets a length bound: their columns are `text`
and `jsonb`, deliberately unbounded, and inventing a limit here would change
behaviour rather than preserve it.

---

## Timestamp types (Story 3)

Twenty-six properties across nine contexts. Each becomes a `record` following
`EventIngestion.Domain.Event.IngestedAt` exactly: `From(...)` normalizes with
`ToUniversalTime()`, an implicit `DateTimeOffset` unwrap is exposed, and
`ToString()` renders round-trip `"O"` format.

| Context | Aggregate | Properties → types | Null? |
|---|---|---|---|
| AuditObservability | `AuditEvent` | `OccurredAt`, `ReceivedAt` | no |
| AuditObservability | `AuditEvent` | `HandlerEnteredAt`, `WrittenAt` | yes |
| Automation | `Rule` | `CreatedAt` | no |
| Automation | `Rule` | `PublishedAt`, `ArchivedAt` | yes |
| CameraCatalog | `Camera` | `RegisteredAt` | no |
| EventIngestion | `DeadLetter` | `RejectedAt` | no |
| EventIngestion | `WebhookIntegration` | `RegisteredAt` | no |
| EventIngestion | `WebhookIntegration` | `RevokedAt`, `RotatedAt` | yes |
| Identity | `RegisteredClient` | `RegisteredAt` | no |
| Identity | `RegisteredClient` | `DisabledAt`, `LastRotatedAt` | yes |
| LayoutComposition | `Layout` | `CreatedAt` | no |
| LayoutComposition | `Revision` | `CreatedAt` | no |
| LayoutComposition | `Revision` | `PublishedAt`, `ArchivedAt` | yes |
| OverlayDesigner | `Overlay` | `CreatedAt` | no |
| OverlayDesigner | `Revision` | `CreatedAt` | no |
| OverlayDesigner | `Revision` | `PublishedAt`, `ArchivedAt` | yes |
| StreamDistribution | `Stream` | `ProvisionedAt` | no |
| StreamDistribution | `Stream` | `LastSuccessAt` | yes |
| SystemVariables | `Variable` | `CreatedAt` | no |

### Types are per context, and the repetition is deliberate

`CreatedAt` appears in five contexts and `RegisteredAt` in three. Each gets
its **own** type in its **own** context — `Automation.Rule.CreatedAt` is not
`OverlayDesigner.Revision.CreatedAt`.

This follows the per-context precedent (`OccurredAt` and `IngestedAt` live in
EventIngestion and are not shared) and the no-cross-context-reference house
rule, which forbids a shared domain type outright: a common `CreatedAt` would
have to live in `Shared.Kernel`, which "holds no domain".

The repetition is the correct outcome, not an accident to be refactored away
later. Story 3's whole value is that two instants cannot be substituted
(FR-015); a shared type would restore exactly the substitutability being
removed.

### The unwrap operator is not optional

`IngestedAt` documents why it exposes `implicit operator DateTimeOffset`: EF
cannot translate member access (`e.IngestedAt.Value < x`) on a value-converted
column, so range and ordering predicates would silently stop translating and
fall back to client evaluation — or fail outright.

Every new timestamp type on a column that is **queried, ordered, or
range-filtered** carries the same operator. Auditing which of the 26 are so
used is a task, not an assumption.

---

## `AggregateVersion` (Story 5)

A single type in `Shared.Kernel`, replacing `int` in three places.

| Location | Today | After |
|---|---|---|
| `Shared.Kernel/AggregateRoot.cs:16` | `public int Version { get; protected set; }` | `AggregateVersion` |
| `Shared.Kernel/Primitives/IVersionedAggregate.cs:12` | `int Version { get; }` | `AggregateVersion` |
| Commands (`ExpectedVersion`) | `int` | `AggregateVersion` |
| 10 EF configurations | `.IsConcurrencyToken()` | unchanged + `.HasConversion(...)` |

**It must be a `record`.** Research R2 established that EF auto-generates the
value comparer from the type's equality, and the concurrency check compares
original against current through that comparer. A `class` without value
equality yields reference comparison and every stale-write check
silently mis-fires — passing writes that should be refused. This is the one
place in this feature where the wrong choice produces a correctness bug rather
than a compile error.

**Invariant**: non-negative. `Version` starts at 0 and increments; a negative
version is meaningless. `Ensure.That(value).AtLeast(0)` — the existing `int`
overload, no new overload needed.

**This is the only new type in `Shared.Kernel`**, and it is admissible there
because a version is a language-level concept, not domain vocabulary — the
same basis on which `Result<T, E>` and `Option<T>` already live there.

---

## Boundary conversions (Story 4)

The types above are constructed once, where untrusted input arrives. Affected
shapes, from the survey:

| Context | Shape | Primitive today |
|---|---|---|
| Automation | `CreateRuleCommand` | `string TriggerSource`, `string TriggerKind` |
| Automation | `GetRuleQuery`, `DryRunRuleQuery` | `string Name` |
| CameraCatalog | `ListCamerasQuery` | `string Sort`, `string Order`, `int Offset` |
| StreamDistribution | `ProvisionStreamCommand`, `RepointStreamCommand` | `string RtspSourceUrl` |
| StreamDistribution | `AuthorizeWhepCommand` | `string BearerToken` |
| SystemVariables | `SetVariableValueCommand` | `string WireValue` |
| Identity | `RegisterDeviceCommand` | `string DeviceType`, `string DeviceIdentifier` |
| Identity | `RotateWebhookClientCommand` | `string IntegrationName` |
| AuditObservability | `GetAuditEventQuery` | `Guid AuditIdentifier` |
| AuditObservability | `GetResourceTimelineQuery` | `string ResourceKind`, `string ResourceIdentifier`, `string Fab` |
| all | every command carrying `ExpectedVersion` | `int` |

`StreamSourceUrl` already exists as a value object and `Stream.SourceUrl`
already uses it — the command feeding it still takes a `string`. Several of
these need no new type at all, only the existing one applied one layer
further out.

A conversion failure at the boundary is a **client error** (FR-020), not a
thrown guard: `Ensure` raises `ArgumentException`, which is a programmer-error
signal. Endpoints parse into `Result<T, ApiError>` and return 400; the guard
chain stays for internal callers who have already validated. Conflating the
two would turn malformed user input into a 500.

---

## What is deliberately untouched

- **`Shared.Contracts`** — a wire format, primitives by design, out of scope.
- **`ApiError(Code, Message, Status)`** — a serialization contract (ADR-0089),
  an exemption of record.
- ~~**`Tile.Row` / `Tile.Col`, `GridDimensions`** — already inside value
  objects; the `int` is the backing value, as with the three string cases
  above.~~ **Wrong for `Tile`, and corrected on 2026-09-02.** True of
  `GridDimensions`, whose ints *are* its backing values. `Tile` stored two
  loose ints and reconstructed `GridPosition` from them, so the primitives
  were its storage, not a value object's backing value; the real reason was
  EF's need for scalar key columns, which is not one of §II's exemptions.
  The coordinate fields are now private and the domain sees only `Position`
  — the schema is unchanged. ADR-0140 rewrites the exemption that admitted
  this reading. `GridDimensions` stands as written.
- **`FabIdentifier.CompareTo` and friends** — `int` return types required by
  `IComparable<T>`. These inflated the original survey and are not properties
  at all.
- **`Label`'s normalized-coordinate `decimal` guards** — an existing ADR-0105
  exclusion with a working local helper, unchanged.
