# Phase 0 — Research: making a fab identifier orderable

**Feature**: `039-comparable-fab-identifier` · **Spec**: [spec.md](./spec.md)

Seven questions the plan refused to assume. Two answers **correct the spec**, one
sizes the work upward, and one resolves a mechanism choice and a testability
problem at the same time.

---

## 1. The eight copies have already drifted — and it is the same context both times

**Finding**: seven of the eight record bodies are **byte-identical**.
`AuditObservability`'s is not.

```text
AuditObservability/Domain/AuditEvent   body:13c29f1b9031   ← differs
Automation/Domain/Rule                 body:4c2e699f6a8f
CameraCatalog/Domain/Camera            body:4c2e699f6a8f
EventIngestion/Domain/Event            body:4c2e699f6a8f
Identity/Domain/RegisteredClient       body:4c2e699f6a8f
LayoutComposition/Domain/Layout        body:4c2e699f6a8f
StreamDistribution/Domain/Stream       body:4c2e699f6a8f
SystemVariables/Domain/Variable        body:4c2e699f6a8f
```

The difference is one real thing and one cosmetic thing:

```diff
-        Ensure.That(value, nameof(value))
+        Ensure.That(value)
```

...plus a line-wrap on the `.Satisfies(...)` call. **The grammar itself is
identical in all eight** — same length bounds, same `IsValid`, same message — so
the spec's premise holds. What differs is that one copy's validation failure does
not name the parameter it rejected.

**And the same context is the one missing a test.** Seven contexts have a
`FabIdentifierTests.cs`; `AuditObservability` does not (§4). Two independent
signs that this copy was added slightly apart from the others.

**Decision: observed, not fixed.** The `nameof` omission is a one-word
improvement to an error message in a file this feature already touches, which is
exactly what makes it tempting. It is not what this feature is for, and mixing a
message fix into a comparison change muddies a diff whose whole value is being
eight identical edits. **Raised in the PR, left alone in the code.**

It does strengthen the case for the convention test: the copies are *already*
not quite copies, and nothing noticed.

---

## 2. `StringValueObject` provides nothing that interacts with the comparison

```csharp
public abstract record StringValueObject(string Value) : IValueObject<string>
{
    public sealed override string ToString() => Value;
}
```

A positional record, so `Equals` and `GetHashCode` are compiler-generated over
`Value`, and `ToString` is sealed. **Nothing to reconcile**: adding
`IComparable<FabIdentifier>` introduces no conflict with equality, and the
record's own `Equals` already agrees with what an ordinal comparison of `Value`
would call equal — unlike `CameraName`, which overrides `Equals` to compare a
normalised form and therefore *must* compare the same normalised form when
ordering, or the two would disagree.

That is the whole reason the fab identifier's comparison is simpler, and it is
worth stating in the code rather than leaving the difference from `CameraName`
looking like an oversight.

---

## 3. The convention test reads source, and that also solves §5

**Decision**: scan source files, following `StaleCodeConventionTests`' existing
helper — walk up from `AppContext.BaseDirectory` to `SmartSentinelEye.slnx`,
enumerate `src/**/*.cs` excluding `obj/` and `bin/`.

**Reflection was the more obvious choice and is rejected.** All eight Domain
projects *are* referenced by `Architecture.Tests` (checked, not assumed), so
reflection would work today. Two things decide against it:

1. **The test exists for the ninth context.** A ninth Domain project added
   without a reference to `Architecture.Tests` is invisible to reflection and
   visible to a source scan. The case the guard is for is precisely the case
   reflection misses.
2. **Ordinality is not visible by reflection** (§5). `StringComparison.Ordinal`
   is an argument inside a method body; there is no assembly-level artefact for
   it. A source scan can assert it; reflection cannot.

The precision objection — that a source scan could match the word `IComparable`
in a comment — is handled by matching the **record declaration** rather than the
bare word.

`NameMutabilityConventionTests` records the same reasoning for reading source
("there is no assembly-level artefact to inspect"), so this follows an
established precedent rather than inventing a mechanism.

---

## 4. Coverage is a real risk, not a hypothetical — and it sizes the work upward

**Finding**: the tightest gate has **~2% of headroom** and this feature would
spend more than that.

`Identity.Domain` measured **91.7%** against a **≥ 90%** gate, over roughly 250
non-blank non-comment lines — so about 21 lines are already uncovered. Adding
five uncovered members (`CompareTo` plus four operators) takes it to roughly
26 of 255, or **~89.8%: under the gate**.

**Decision**: every one of the eight gets its comparison covered. That is the
honest answer regardless — an untested `CompareTo` in seven contexts is exactly
the "seven gain something nothing exercises" objection made real — and it makes
SC-003 something demonstrated rather than declared.

**This makes the work eight value objects *and* eight test files**, not eight
value objects and one. Seven contexts have a `FabIdentifierTests.cs` to extend;
`AuditObservability` needs one created (§1).

**Alternatives considered**: covering only `CameraCatalog`'s and accepting the
others' gates (rejected — Identity's would fail, and the others narrow); marking
the operators as not-covered (rejected — the repo has no such mechanism and
introducing one to dodge a gate is worse than the gate).

---

## 5. The ordinality assertion the spec asked for **cannot be written**

**Finding**: under the fab grammar, I could not construct a pair of identifiers
whose ordinal and culture-sensitive orderings disagree on this platform.

The grammar admits lowercase ASCII letters, digits and `-`, starting with a
letter. Probed pairs, each reporting `Math.Sign` of `StringComparison.Ordinal`
versus `StringComparison.InvariantCulture` versus `CompareInfo.Compare`:

| Pair | Ordinal | Culture | ICU |
|---|---|---|---|
| `a-b` / `ab` | −1 | −1 | — |
| `fab-1` / `fab1` | −1 | −1 | — |
| `ab-d` / `abc` | −1 | −1 | — |
| `fab-b` / `faba` | −1 | −1 | — |
| `a-9` / `a1` | −1 | −1 | — |
| `co-op` / `coop` | −1 | −1 | −1 |
| `a-a` / `aa` | −1 | −1 | −1 |

Globalization-invariant mode is **off** (`AppContext` switch reads `False`, and
nothing sets `InvariantGlobalization` in any project file), so this is not the
usual explanation. The grammar is simply too small a character set for the two
comparisons to disagree here.

**This corrects the spec.** Its stated assertion — *"assert an ordering that a
culture-sensitive comparison would get wrong"* — is not constructible.

**Decision**: assert ordinality **structurally instead of behaviourally**. The
convention test asserts each copy's comparison names `StringComparison.Ordinal`.
That is a stronger guarantee than a single behavioural pair would have given
anyway, because it holds for every input rather than for the one that happened to
distinguish them.

**Ordinal remains the right choice** even without a pair that proves it: ICU
behaviour varies by operating system and library version, so a culture-sensitive
comparison could order two fabs one way on a developer's Windows machine and
another on a Linux CI runner. The requirement is about **stability**, and the
structural assertion is what actually guards it.

---

## 6. The comparison has no existing caller beyond the one sort

Searched for `Dictionary<FabIdentifier`, `HashSet<FabIdentifier`,
`SortedSet<FabIdentifier` and `SortedDictionary<FabIdentifier` across `src` and
`tests`: **no matches**.

So nothing today depends on ordering a fab identifier except
`ListCamerasQueryHandler`'s tie-break, and nothing depends on hashing one in a
way this could disturb. The risk profile is as small as it looks — which is worth
recording, because "does this change how anything already behaves?" is the first
question a reviewer of an eight-file diff will ask.

---

## 7. The compiler does **not** catch a missing comparison, so the guard earns its keep everywhere

`OrderBy`/`ThenBy` accept any key type and resolve `Comparer<T>.Default` at
runtime, so `ThenBy(camera => camera.Fab)` compiles whether or not
`FabIdentifier` is comparable. **The reproduction is the proof**: the current
code compiles and throws at run time.

So removing the interface from `CameraCatalog`'s copy would not be caught by the
build — which answers the question of whether the convention test is redundant
for the one context that actually sorts. **It is not.** The guard is the only
compile-or-test-time signal for all eight, including the one with a live caller.

---

## 8. No ADR

This decides no new domain question. ADR-0044 already governs why the eight are
separate copies, and this feature acts on that decision rather than revisiting
it — the whole argument for touching all eight is *ADR-0044 says these are
deliberate copies, so they should not differ where the grammar does not*.

Recorded here and in the spec's Assumptions so a later reader does not go looking
for the record that would explain an eight-file change.
