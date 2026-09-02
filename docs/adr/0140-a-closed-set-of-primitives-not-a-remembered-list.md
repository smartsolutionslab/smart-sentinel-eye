# ADR-0140: A closed set of primitives, not a remembered list

**Status:** **Accepted**
**Date:** 2026-09-02
**Extends:** ADR-0139 (rules that fail the build, not the review)
**Amends:** Constitution §II (Domain-Driven Design with Value Objects)
**Supersedes:** —
**Superseded by:** —

## Context

ADR-0139 replaced §II's three illustrative examples with an exhaustive list of
nine disallowed types, on the reasoning that "a rule illustrated rather than
stated is one every reader draws differently". That reasoning holds. The list
does not.

**The list had a hole on the day it was written.** It bans `int` and `long`
and omits `short`. `AuditEvent.SchemaVersion` is a `short`, so by the letter of
the amended §II it is permitted — a bare numeric primitive on a domain model,
legal because of which widths someone happened to type. `byte`, `sbyte`,
`ushort`, `uint`, `ulong`, `char`, `nint`, `nuint` and `object` are absent on
the same terms, as are `DateTime`, `TimeSpan`, `DateOnly`, `TimeOnly` and
`Uri` — the last of which have obvious domain-model uses this codebase has so
far avoided by luck rather than by rule.

This was found the day after ratification, by checking one aggregate's
properties against §II by hand. Nothing detected it; nothing could have. **The
failure mode is the one ADR-0139 set out to fix, arriving through the fix
itself**: an enumeration written from memory is a record of what its author
recalled, and the next reader cannot tell an omission from a carve-out. §II
says "the list is exhaustive on purpose", which makes every absence look
deliberate.

A second wording defect surfaced with it. The third exemption — "a value
object's own backing value" — is **singular**, and this codebase has composite
value objects: `GridPosition(int Row, int Col)`, `GridDimensions(int Rows, int
Cols)`, `BooleanLabels(string, string)`, `Label(string, decimal ×4, int)`.
Read literally, only single-valued wrappers are exempt and four legitimate
types are in breach. Read charitably, the exemption stretches to anything
"inside a value object" — and that reading is what let `Tile.Row`/`Tile.Col`
be declined in spec 057's `data-model.md` as "already inside value objects;
the `int` is the backing value", when `Tile` in fact stored two loose ints and
reconstructed `GridPosition` from them. A single word admitted both readings,
and the wrong one was taken.

## Decision

### 1. The banned set is a category, not an enumeration

§II no longer lists types by name. A type is disallowed on a domain model if
it is **a C# predefined type** — one with a language keyword spelling — or one
of a short named list of BCL types that carry no domain meaning.

The predefined types, for the reader's convenience and not as the rule itself:
`bool`, `byte`, `sbyte`, `char`, `decimal`, `double`, `float`, `int`, `uint`,
`nint`, `nuint`, `long`, `ulong`, `short`, `ushort`, `string`, `object`.

The named BCL types: `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`,
`TimeOnly`, `TimeSpan`, `Uri`.

**The distinction is the point.** `short` is now banned because it is
keyword-spelled, not because this document remembered it. A future numeric
width, or a type this codebase has not met, is covered without an amendment.
The named list can still develop holes; it is short, it names types rather
than widths, and a hole in it is a missing type rather than a missing member
of a family whose other members are banned.

### 2. The ban binds state, not derived answers

§II gains an explicit scope, because a categorical ban needs one and the
enumerated version got by without one.

**Bound:** members that carry state — properties, record components, and the
constructor or factory parameters that set them.

**Not bound:** values a domain model *computes*. `ActorIdentifier.IsSystem`,
`WebhookIntegration.IsRevoked` and `GridDimensions.Contains(...)` return
`bool`; `IComparable<T>.CompareTo` returns `int`. These were never the target
— spec 057's survey recorded that `CompareTo` and its kind "inflated the
original survey and are not properties at all" — but under a categorical rule
the omission would read as an oversight rather than a boundary.

### 3. The backing-value exemption is plural

The third exemption becomes "**a value object's own backing values**", and
says so explicitly for composites: a value object built from several
primitives is exempt in all of them. `GridPosition`, `GridDimensions`,
`BooleanLabels` and `Label` are exempt types, not tolerated breaches.

The exemption reaches a value object's **own** components and stops there. It
does not reach a primitive held by an entity or aggregate that merely
*constructs* a value object from it — which is the reading that let `Tile`
through, and which the corrected wording now refuses.

### 4. No exemption for EF-mapped scalars

Considered and rejected on evidence, because it is the exemption `Tile` was
tacitly claiming. EF needs scalar columns to key `layout_revision_tiles` on
`(revision_id, row, col)`, and that need is real. It does not require a
primitive on the domain type: the coordinate fields are private and mapped as
field-backed properties, the schema is byte-for-byte unchanged, and the domain
exposes only `GridPosition`. The counter-example sat in the same file all
along — `Tile.OverlayValue` exists for exactly the same EF reason and has
always been a typed `OverlayIdentifier?`.

An exemption is warranted when the rule cannot be followed. This one could.

## Consequences

**One existing breach, and it is fixed in the same change.** A survey of every
property, record component and factory parameter on all domain models found
exactly one member newly in breach: `AuditEvent.SchemaVersion`. It becomes a
value object here. Nothing else in `src/*/Domain` or `Shared.Kernel` uses a
type this amendment newly bans — the codebase already complied with the rule
as intended, and only the rule was wrong.

**Easier.** A reader answers "is this allowed?" by asking whether the type has
a keyword, which requires no memory and no list. The two readings of the
backing-value exemption collapse to one.

**Harder.** A legitimately primitive-typed member on a domain model now
requires an amendment rather than an absence. That is the intent, and it is a
real cost: `object` and `char` are banned without a single call site arguing
for or against them, on the same forward-looking basis ADR-0139 used to ban
three guard helpers with zero call sites.

**Still not mechanical.** ADR-0139 recorded that the primitive rule "does not
fail a build" and that a future ADR could add an architecture test. This is
not that ADR either, and the reason is worth stating rather than deferring
silently: **the exemptions are what make the test hard, not the ban.** Every
value object is a keyword-typed member by construction, so a rule that flagged
banned types would flag all of them first. A useful test must distinguish a
value object's own components from state on an aggregate — which is exactly
the distinction §2 and §3 above just had to write down in prose. The prose is
the prerequisite; the test is the next step, and it is code, not an amendment.

**A hole found by hand implies others found the same way.** This amendment
closes a class rather than an instance, which is the only reason it is worth a
version bump. It does not establish that the rest of §II is sound.

## Alternatives Considered

**Add `short` to the list.** Rejected — the minimal fix, and the one that
reproduces the defect. The list would still be an enumeration from memory, and
`byte` or `char` would arrive later as the same surprise. Closing one hole in
a list whose failure mode is holes is not a fix.

**Revert to illustration** — "primitives do not appear on a domain model", no
list. Rejected for ADR-0139's reason, unchanged and correct: nine `string` and
26 `DateTimeOffset` properties accumulated under exactly that wording, because
every reader drew the line somewhere else.

**A `NetArchTest` rule instead of an amendment.** Rejected as a substitute,
accepted as a successor. A test cannot state what is banned; it can only
enforce a statement. Writing it first would have encoded the singular
backing-value wording — and passed `Tile`, since `Tile.Row` was an `int` on a
type the rule would have had to treat as a value object.

**Exempt `SchemaVersion` as a serialization concern**, as `ApiError`'s strings
are exempt. Rejected. `ApiError` is exempt because its strings cross the wire
*as themselves*, and a type would have to be unwrapped at that boundary
anyway. `SchemaVersion` is read by domain code — `AuditEvent.From` stamps every row
with `SchemaVersion.Current` — and it is exactly the kind of bare number a
mistyped literal would corrupt silently.

## Implementation Notes

`SchemaVersion` keeps `short` as its backing type and its `smallint` column.
The value object wraps it; the schema does not move. Its guard is
non-negativity only — the set of known schema versions is a fact about this
system's history, not an invariant of the type, and encoding "must be 1" would
make the type refuse the second version it exists to distinguish.
