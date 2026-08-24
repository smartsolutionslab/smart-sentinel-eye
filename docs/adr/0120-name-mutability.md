# ADR-0120: A Name Is Mutable Exactly When It Is Not an Address

**Status:** **Accepted**
**Date:** 2026-08-24
**Supersedes:** —
**Superseded by:** —

## Context

No aggregate in this product supports renaming.

```sh
grep -rn "public void Rename\|Rename(" --include=*.cs src/ | grep -v obj
# (no output)
```

Every context's answer to *"this is named wrong"* is the same: create the right
one, archive or retire the wrong one. Issue #1850 reads that uniformity as a
convention — names are immutable, and adding a rename anywhere would break a
pattern.

**That reading does not survive looking at how the aggregates are addressed.**

### Two kinds of name

| Aggregate | API address | Name referenced by |
|---|---|---|
| `Camera` | `/{camera:guid}` | Layouts, via `CameraIdentifier` (a Guid) |
| `Layout` | `/{layoutIdentifier:guid}` | — |
| `Overlay` | `/{overlayIdentifier:guid}` | — |
| `Rule` | **`/{name}`** | nothing outside Automation |
| `Variable` | **`/{name}`** | **Automation, by name** |

Three aggregates are addressed by an identifier and carry a name as an
**attribute**. Two are addressed **by the name itself**.

So renaming is not one operation with one answer:

- Where the name is an attribute, changing it is an **ordinary edit**. Nothing
  refers to the old value, so nothing dangles.
- Where the name is the address, changing it is an **identity change**. Every
  existing reference to the old name stops resolving, and the aggregate is, from
  the outside, a different thing.

The uniform absence of renaming is explained by the hard cases. Nobody decided
the easy ones.

### Why `Variable` is worse than `Rule`

Both are name-addressed, and it would be tidy to exclude them together. It would
also lose the part that matters.

`Rule`'s name appears in URLs and nowhere else — nothing outside `Automation`
references a rule by name. Renaming one costs a bookmark.

`Variable`'s name is **stored data in another bounded context**:

```csharp
public sealed record SetVariableValue(string VariableName, string ValueExpression) : RuleAction
```

That string is persisted with the rule (`RuleConfiguration`) and read at
evaluation (`RuleEvaluator`). It crosses from `Automation` to `SystemVariables`,
where **ADR-0016 forbids a project reference** — so there is no foreign key, no
type, and nothing that could notice the target had moved.

Renaming a variable would leave rules that **silently stop firing**, with no
error raised anywhere in the system. That is a materially different cost from a
broken bookmark, and this ADR keeps the two apart.

### The camera is safe for a reason it did not choose

`Camera` endpoints are keyed on the identifier because **spec 028 made names
reusable** — a name identifies at most one *active* camera per fab, but several
over time, so a name-keyed endpoint could not address a retired one. Specs 028
and 029 keyed on the identifier to solve that.

A decision taken for a different purpose is what makes a camera rename an
ordinary attribute edit today.

## Decision

**A name may be changed only where the aggregate is not addressed by it.**

Stated as a rule rather than as a list, because the list is not trustworthy —
see *Implementation Notes*.

### The rulings that follow

| Aggregate | Renameable | Why |
|---|---|---|
| `Camera` | **Yes** | Identifier-addressed; referenced by identifier. Delivered by spec 033 |
| `Layout` | Yes in principle | Identifier-addressed. Not built; no demand |
| `Overlay` | Yes in principle | Identifier-addressed. Not built; no demand |
| `Rule` | **No** | The name is the address |
| `Variable` | **No, most strongly** | The name is the address **and** stored cross-context data with no integrity to protect it |

An aggregate that is name-addressed **may** become renameable, but only as a
deliberate identity migration with a story for every existing reference — not by
adding a `Rename` method.

## Consequences

**Easier.** A misnamed camera can be corrected without losing its identifier, so
one physical camera keeps one history instead of being split across a retired
record and a new one.

**Harder.** Two aggregates now differ visibly from a third for a reason that is
not visible at the call site. That is why the rule is enforced rather than
documented — the next person to want a rename should be told by the build, not
by this file.

**Unchanged.** Every existing behaviour. This ADR permits something; it requires
nothing to change.

**A limit accepted, not solved.** The rule keys on *addressing*, which is a good
proxy for "something refers to this by name" and not the same thing. A future
aggregate could be identifier-addressed and still have its name copied into
another context's storage — exactly `Variable`'s problem without `Variable`'s
route. The check below would not catch it. Recorded so that the next reader
knows it is a proxy.

## Alternatives Considered

### Names are immutable, product-wide

What #1850 proposes, and the status quo. Simple, uniform, and it makes the
easy case pay for the hard one: correcting a camera's name would keep costing
its identity for a reason that only applies to rules and variables. Rejected.

### Names are mutable, product-wide

Consistent in the other direction, and unsafe. It would permit renaming a
variable, which silently breaks rules. Rejected outright — this is the failure
the ADR exists to prevent.

### Rule on `Camera` alone, and leave the rest open

The smallest change, and the one that would have shipped fastest. Rejected
because it decides the general question by accident: the next aggregate to want
a rename inherits whatever `Camera` did, with no recorded reason, and the
inconsistency surfaces as an argument rather than a rule.

### Document the convention without enforcing it

The failure mode this repository has already lived through. ADR-0119 exists
because a convention six contexts followed by imitation was missed by the
seventh and nothing noticed. A convention that depends on being read is the
thing that failed there.

## Implementation Notes

Enforced by `tests/Architecture.Tests/NameMutabilityConventionTests.cs`, which
reads **source** rather than reflecting over assemblies — the same approach, and
for the same reason, as `StaleCodeConventionTests`.

The detectable signal is the **route parameter constraint**:

| Constrained — identifier-addressed | Unconstrained — addressed by the value |
|---|---|
| `{camera:guid}`, `{cameraIdentifier:guid}` | `{name}` — Automation, SystemVariables |
| `{layoutIdentifier:guid}`, `{overlayIdentifier:guid}` | `{integrationName}` — EventIngestion |
| `{auditIdentifier:guid}`, `{eventId:guid}` | `{clientId}` — Identity |
| `{revisionNumber:int}` | `{resourceIdentifier}`, `{resourceKind}` — AuditObservability |

A context binding a parameter with no type constraint is addressed by that
value, and must not also expose a rename of it.

**The check is deliberately not a list of today's aggregates**, and the right
column is why. The analysis that produced this ADR began from five aggregates;
the routes show at least two more surfaces addressed by a non-identifier. An
enumeration compiled carefully was wrong within the hour — which is precisely
what happened to ADR-0119, where an inventory of seven contexts was complete
until the architecture test found an eighth on its first run.

So the check fails for a context inventing `{slug}` or `{code}` tomorrow, not
only for the ones that exist today. That property was verified by adding a
`RenameRuleCommand` to `Automation` — which binds `{name}` — and watching the
suite go red.

Specified as `033-rename-convention`, from issue #1850, which was itself spec
029's FR-012 filed rather than implied.
