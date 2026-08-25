# Contract: how a fab identifier orders, and what the guard requires

**Feature**: `039-comparable-fab-identifier` · **Plan**: [plan.md](../plan.md)

The same edit lands in **eight** files. Given verbatim here because the value of
the diff is that all eight are identical, and eight paraphrases of one idea would
not be.

---

## The declaration

```csharp
public sealed record FabIdentifier : StringValueObject, IComparable<FabIdentifier>
```

## The comparison

```csharp
    /// <summary>
    /// Ordinal, on <see cref="StringValueObject.Value"/> directly.
    ///
    /// <para>
    /// <c>CameraName</c> compares its <c>NormalizedValue</c>, and the difference
    /// is deliberate rather than an oversight here: that type preserves display
    /// casing, so its own <c>Equals</c> compares a normalised form and its
    /// ordering has to agree with it. A fab identifier's grammar admits
    /// lowercase letters, digits and <c>-</c> only, so there is exactly one
    /// spelling of any value and nothing to normalise. A normalisation step here
    /// would be a rule with no input that exercises it.
    /// </para>
    ///
    /// <para>
    /// Ordinal rather than culture-sensitive because the ordering must be the
    /// same everywhere it runs. ICU's behaviour varies by operating system and
    /// library version, so a culture-sensitive comparison could order two fabs
    /// one way on a developer's machine and another on a CI runner — and the
    /// caller that consults this is a database tie-break whose whole purpose is
    /// a stable page boundary.
    /// </para>
    /// </summary>
    public int CompareTo(FabIdentifier? other)
    {
        if (other is null)
        {
            return 1;
        }

        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }

    public static bool operator <(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) < 0;

    public static bool operator >(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) > 0;

    public static bool operator <=(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) <= 0;

    public static bool operator >=(FabIdentifier left, FabIdentifier right) =>
        Comparer<FabIdentifier>.Default.Compare(left, right) >= 0;
```

Placed **immediately after the private constructor and before `From`**, matching
`CameraName`'s layout so the two read alike side by side.

### Why the operators come too

`CameraName` defines them. A fab identifier that could be ordered but not
compared with `<` would be a second, smaller asymmetry standing where the first
one was — and the operators are the reason `Comparer<T>.Default` is used rather
than calling `CompareTo` directly: it handles a `null` on the **left**, which
`left.CompareTo(right)` would not.

### What must not change

`From`, `IsValid`, `MinimumLength`, `MaximumLength` and the existing doc comment.
This feature adds; it does not tidy.

---

## What the convention test requires

`tests/Architecture.Tests/FabOrderingConventionTests.cs`, over every file named
`FabIdentifier.cs` under `src/` (excluding `obj/` and `bin/`):

| Requirement | Why |
|---|---|
| The **record declaration** names `IComparable<FabIdentifier>` | Matched on the declaration line, not the bare word, so a mention in a comment cannot satisfy it |
| The file names `StringComparison.Ordinal` | The ordinality requirement, asserted structurally — see below |
| At least one file is found | A scan that silently matches nothing passes forever. This is the failure mode of every source-reading convention test |

### Ordinality is asserted structurally, and that is not a compromise

The spec asked for a behavioural assertion: two identifiers whose ordinal and
culture-sensitive orderings disagree. **Under this grammar, no such pair could be
constructed on this platform** (research §5) — the character set is too small for
the two comparisons to differ, and globalization-invariant mode is off, so that
is not the explanation.

Asserting the source is a **stronger** guarantee than the pair would have been: it
holds for every input, rather than for the one input that happened to
distinguish the two comparisons. It is also why this test reads source — there is
no assembly-level artefact for a `StringComparison` argument, so reflection could
not make this assertion at all.

### The failure message

Must name the **file** and say what breaks. The runtime failure this prevents —
`At least one object must implement IComparable`, from deep inside LINQ — names
neither the field being sorted nor the query doing the sorting, which is why it
cost half an hour the first time. A guard that fails with `Assert.True(false)`
hands the next reader the same problem in a new place.

---

## What the listing must do

`ListCamerasQueryHandler` is **unchanged**. What changes is that its existing
tie-break can now be exercised from a test:

| Given | Then |
|---|---|
| Two cameras tying on the primary sort key, differing by fab | Returned **in fab order** |
| The same, on the other sortable field | The tie is broken by fab there too |

**Assert the order, not the absence of an exception.** A comparison returning 0
for every pair also stops the throw, while leaving exactly the defect the
tie-break exists to prevent: two rows with no defined relative order, and a page
boundary that can show one of them twice and the other never.

**Both sort paths**, because they are separate expressions and one test exercises
one of them.
