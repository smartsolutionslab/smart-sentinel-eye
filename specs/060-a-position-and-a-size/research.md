# Phase 0 Research: A position and a size

**Feature**: 060 | **Date**: 2026-09-03 | **Spec**: [spec.md](./spec.md)

One unknown carries this feature's only real technical risk, and it is about
persistence rather than design. It is recorded here **unsettled, with the
experiment and both branches fully specified**, because settling it means
building an EF model and reading it — phase 4b work, not phase 2 reasoning.
The failure this guards against is spec 058's R1 exactly: a mapping that
compiles, passes every unit test, and silently moves or nulls a column.

---

## R1 — Can an owned reference nest three levels deep onto pinned columns?

**Status**: **Settled 2026-09-04 (T101). Yes — the preferred branch is taken.**
The nesting is fine; what EF refused was something this section did not ask
about, and the answer is recorded below the original question rather than in
place of it.

### The answer

Built the model and read the relational shape off it — first in a scratch
harness with an equivalent three-deep model, then on the real
`OverlayDesignerDbContext` after the change:

```
NormalizedPosition.X       col=label_x       clr=Decimal  nullable=False  type=numeric
NormalizedPosition.Y       col=label_y       clr=Decimal  nullable=False  type=numeric
NormalizedSize.Width       col=label_width   clr=Decimal  nullable=False  type=numeric
NormalizedSize.Height      col=label_height  clr=Decimal  nullable=False  type=numeric
```

All four on `overlay_revisions`, all `NOT NULL`, all `numeric` — identical to
what the four loose properties produced. `dotnet ef migrations
has-pending-model-changes` reports *"No changes have been made to the model
since the last migration"*, the same answer it gave before the change, so **no
migration and no snapshot edit**, as SC-004 requires.

### What EF actually refused, and the one line that fixes it

The nesting was never the obstacle. The obstacle is `Label` being a
**positional record**:

```
No suitable constructor was found for the type 'Label'.
    Cannot bind 'Position', 'Size' in
    'Label(string Text, NormalizedPosition Position, NormalizedSize Size, int FontSizePx)'
Note that only mapped properties can be bound to constructor parameters.
Navigations to related entities, including references to owned types, cannot be bound.
```

A constructor parameter binds to a mapped **scalar**; an owned reference is a
navigation and can never be constructor-bound. Today's `Label` binds cleanly
only because all six parameters are scalars. So the fix is a private
scalar-only constructor EF can bind `Text` and `FontSizePx` through, after
which it sets the two navigations itself:

```csharp
private Label(string text, int fontSizePx) : this(text, null!, null!, fontSizePx) { }
```

This is not the "third shape" the decision procedure below warns against. It is
the preferred branch plus EF's standard materialization constructor — the same
accommodation `Tile` already carries, and the fallback branch would have needed
one too.

**`Tile`'s private-scalar fallback was therefore not taken**, and the constraint
spec 057 found (a field-only property's name must match its field) never came
into play.

### One thing neither branch predicted

`Revision.Branch` copies the base revision's `Label` with `label with { }`,
because sharing one CLR instance across two revisions makes EF re-key an owned
entity onto a new principal and throw. **A `with` expression is shallow.** Once
`Position` and `Size` are themselves owned entities keyed on the `Label`, the
shallow copy hands the new `Label` the old one's two instances and reproduces
that failure one level down. The copy now reaches both. Exercised over real SQL
by `An_archived_overlay_can_be_branched_edited_and_published_again`.

### The two `Navigation(...).IsRequired()` lines

Kept, and mapped as the plan specifies. Worth recording precisely: with
`.IsRequired()` already set on each of the four **properties**, removing the two
navigation lines did **not** make the columns nullable in this EF version. They
stay because they say the owned reference itself is mandatory, and because the
sibling `Creation` mapping carries them — not because a measurement here showed
them changing the column.

---

<details>
<summary>The question as it stood before T101 ran</summary>


**The question**: `Label` is mapped today as

```
Overlay → OwnsMany(Revisions) → OwnsOne(Label) → Property(NormalizedX).HasColumnName("label_x")
```

After the refactor it would be

```
Overlay → OwnsMany(Revisions) → OwnsOne(Label) → OwnsOne(Position) → Property(X).HasColumnName("label_x")
```

Spec 058's R1 verified an owned reference **inside an owned collection** — two
levels — and recorded that as "one level deeper than anything the codebase does
today". This is one deeper still. Nothing in the repository does it, so it is
not knowable from the repository.

**Why it is in doubt rather than obviously fine**: owned-reference defaults are
all wrong for this feature and have to be overridden individually. Left alone
EF names the column `Position_X`, not `label_x`; and it treats the navigation
as optional, which makes the column nullable against a `NOT NULL` schema.
Spec 058 records the one load-bearing line — `Navigation(...).IsRequired()` —
without which both columns become nullable, nothing fails, and it surfaces only
when someone generates an unrelated migration. That is issue #2022, and this
feature would be creating four more instances of it.

**Experiment** (no database needed, same method as spec 058 R1): build the
`OverlayDesignerDbContext` model in a scratch harness and read the relational
model directly — table name, column name, CLR type and nullability for each of
the four. Compare against the four rows the current model produces. EF Core
10.0.11 / Npgsql 10.0.3.

**Decision procedure**:

- **If the relational model reports `overlay_revisions.label_x/label_y/
  label_width/label_height`, non-nullable, `numeric`** — take the nested owned
  reference. `Label` stays a positional record, no private fields, no EF
  materialization constructor. This is the preferred outcome.

- **If EF refuses the nesting, or renames, or nulls a column that
  `Navigation(...).IsRequired()` on both new navigations does not fix** — take
  `Tile`'s shape instead, which is already in the repository and already
  argued in its XML doc:

  ```csharp
  public sealed record Label
  {
      private readonly decimal normalizedX;   // …and y, width, height
      public NormalizedPosition Position => new(normalizedX, normalizedY);
      public NormalizedSize Size => new(normalizedWidth, normalizedHeight);
  }
  ```

  with the four scalars mapped as field-backed properties, exactly as
  `LayoutConfiguration` maps `Tile`'s `row`/`col`. Note the constraint spec 057
  found by running the model rather than reading about it: **EF refuses a
  field-only property whose name differs from its field**, so the field and the
  mapped property name must match (`normalizedX`, not `NormalizedX`).

Either branch satisfies FR-004, FR-005 and FR-010. Neither is a guess: both are
in-repo shapes with a worked precedent. What must not happen is the engineer
discovering the problem mid-refactor and inventing a third shape.

</details>

---

## R2 — Does the guard message survive the move? (settled by reading)

**Decision**: Yes for the two coordinates, and for the two extents only if the
`Satisfies` message is written to match.

`Ensure`'s value chain throws plain `ArgumentException` with `paramName` set —
read from `src/Shared.Kernel/Ensure.cs`:

```csharp
public EnsuredValue<T> InRange(T minimum, T maximum)  // throws ArgumentException
    → $"{parameter} must be in [{minimum}, {maximum}]; got {value}."
public EnsuredValue<T> Satisfies(Func<T, bool> predicate, string message)
    → $"{parameter}: {message}"
```

`Label.EnsureNormalized` today produces
`$"{parameter} must be in [0, 1]; got {value}."` — **character-for-character
what `InRange(0m, 1m)` produces**, provided the factory parameter is still
named `normalizedX` / `normalizedY`. That is why FR-007 requires the old
parameter names on the new factories even though the properties are `X`/`Y`.

`EnsurePositiveNormalized` produces `$"{parameter} must be in (0, 1]; got
{value}."`. `InRange` cannot express an exclusive lower bound, so this becomes
`Satisfies`, whose format is `$"{parameter}: {message}"` — a colon where the
original had a space. The message therefore changes for the two extents unless
the engineer accepts the colon. **Ruling: accept the colon.** Nothing asserts
the string; it reaches a caller only as the `detail` of a `400` whose `title`
(`OVERLAY_INVALID_INPUT`) and status are the contract. This is declared here
rather than discovered at review, and it is the single observable difference
this feature produces.

`GridDimensions.From` already uses `Satisfies` for its cell cap, so the shape
is not new.

**Why this mattered enough to check**: `LabelTests` asserts
`Should.Throw<ArgumentException>` in four range theories over ten `[InlineData]` rows. If
`Ensure` threw `ArgumentOutOfRangeException` those assertions would need
editing, and under ADR-0144 an edited assertion stops the work. It does not,
and `GridPositionTests` — which asserts `ShouldThrow<ArgumentException>()`
against `Ensure.That(row).AtLeast(0)` — is the standing proof.

**Amended 2026-09-04 (phase 4b): `Ensure.That(decimal)` did not exist.** This
section read the guard *chain* — `EnsuredValue<T>.InRange` is generic over any
comparable struct — and did not check the *entry point*. `Ensure.That` had
overloads for `string`, `Guid`, `int` and any reference type, and none for
`decimal`, so `Ensure.That(normalizedX)` did not compile and no value object in
the repository had ever guarded a decimal. Closed by adding the one overload,
mirroring the `int` one added in `3f2c234` for exactly this reason.

The character-for-character claim above is now a test rather than a reading:
`EnsureTests.Decimal_InRange_names_the_parameter_and_the_interval` pins
`"normalizedY must be in [0, 1]; got 2. (Parameter 'normalizedY')"`, which is
the string `OverlayGeometryValidationIntegrationTests` asserts off the `400`.

---

## R3 — Does the compile-time guarantee have an architecture guard? (settled)

**Decision**: No, and none is written.

`PrimitiveBoundaryTests` is **not** the guard for this change and must not be
presented as one. It asserts that the value-object exemption still fires; it
says nothing about whether `Label` groups its coordinates, and it passes both
before and after. Presenting it as the guard would be the "guard that reads the
design artefact" failure — proving the design was written down rather than that
it holds.

ADR-0144 says a refactor whose point is a compile error is verified by "the
type system plus whichever architecture guard asserts the shape". Here that
list has one entry. The guarantee is asserted by the compiler and exercised by
every call site that must be rewritten and must compile — 2 in `Api`, 2 in
`Application`, 1 in `Infrastructure`, and 36 across the two `OverlayDesigner`
test projects. Writing a runtime test that constructs a transposed `Label` is
impossible by construction after the change, which is the whole point.

**No new architecture test is added.** One assertion in an existing one is
repointed; see plan §"The guard assertion".
