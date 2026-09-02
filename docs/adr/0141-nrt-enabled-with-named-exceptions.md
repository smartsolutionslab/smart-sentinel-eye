# ADR-0141: NRT enabled by default, with named exceptions

**Status:** **Accepted**
**Date:** 2026-09-02
**Amends:** ADR-0048 (NRT disabled, `Option<T>` everywhere) — its NRT half only
**Amends:** `CLAUDE.md`'s stack table (the *Nulls* row)

> **Not a constitution amendment.** The constitution says nothing about nulls —
> its locked stack has no such row, and §Governance's amendment procedure does
> not apply. The claim lives in `CLAUDE.md`. Checked, because two earlier
> statements in this ADR's own issue asserted otherwise.

**Supersedes:** —
**Superseded by:** —

## Context

ADR-0048 disabled Nullable Reference Types at the solution level and chose
`Option<T>` for absences. `Directory.Build.props` still says
`<Nullable>disable</Nullable>`.

**Then 65 of 74 projects override it back to `enable`.** The documented default
survives in nine — and `Shared.Kernel`, where `Option<T>` itself lives, is one
of them. Every bounded context enables NRT in all four of its projects.

This is the defect class this repository has now named five times: §II drifted
twice, the Phase 3 board gate went unfollowed for sixteen specs, §IV recorded a
latency leg as unbuilt after it was built, and a spec promised twelve pairs
after delivering ten. **A rule nobody checked against what was actually
happening.**

The concrete harm, found while writing identical guard tests for two value
objects in spec 058:

```csharp
Should.Throw<ArgumentException>(() => Registration.From(null, someOperator));   // NRT disabled
Should.Throw<ArgumentException>(() => Registration.From(null!, someOperator));  // NRT enabled
```

Get it backwards either way and the Release build fails — `CS8625` where NRT is
on, SonarAnalyzer `S8970` where it is off. Two twin test files differ by one
character for no reason a reader can see without opening a csproj. And a `?`
annotation means something in 65 projects and nothing in nine.

## Decision

**NRT is enabled once, in `Directory.Build.props`. Projects that cannot yet
comply say so explicitly, with a comment. No project restates the default.**

Eight exceptions today:

| Project | Diagnostics |
|---|---|
| `ServiceDefaults` | 56 |
| `ScenarioSimulator` | 52 |
| `Shared.Contracts.Tests` | 38 |
| `StreamDistribution.Application.Tests` | 26 |
| `StreamDistribution.Domain.Tests` | 24 |
| `CameraCatalog.Application.Tests` | 18 |
| `Shared.Kernel.Tests` | 10 |
| `CameraCatalog.Domain.Tests` | 10 |
| **Total** | **234** |

**Measured per project, each built on its own.** An earlier draft of this table
credited the two Application test projects with no diagnostics of their own and
put the total at 138. Both were wrong, and wrong the same way as everything
else in this ADR's history: a whole-solution build stops at the first failing
project, so anything downstream of it is unmeasured rather than clean. Building
each project alone is the only count that means anything here.

> **Progress, 2026-09-02.** Three converted, five remain:
> `Shared.Kernel.Tests`, `CameraCatalog.Domain.Tests` and
> `StreamDistribution.Domain.Tests` are off the list — the three that carried
> the twin-file problem this ADR opens with. Those two `Registration` test
> files are now byte-identical apart from their context and aggregate names.
> Remaining: `ServiceDefaults` 56, `ScenarioSimulator` 52,
> `Shared.Contracts.Tests` 38, `StreamDistribution.Application.Tests` 26,
> `CameraCatalog.Application.Tests` 18 — **190 of the original 234**.

**These are a conversion backlog, not a carve-out.** They are the projects that
genuinely relied on the documented default, and naming them makes the work
countable instead of invisible. Removing one is a normal change; adding one
needs a reason in the csproj comment.

### `Option<T>` is unchanged, and ADR-0048's second half stands

This ADR amends the NRT half only. `Option<T>` remains the way absence is
expressed where the domain models it:

- **194** uses across `src/`, 32 of them in Domain.
- **17** repository lookups return `Task<Option<Aggregate>>`, exactly as
  ADR-0048 requires.

**What changes is that the third case is now written down.** Persisted
absences use nullable value-object references — `PublishedAt?`, `RevokedAt?`,
`FabIdentifier?` — 23 of them, and `Option<T>` is mapped in **zero** EF
configurations. That was already true; it was recorded only in ADR-0139's
implementation notes and a comment in `StreamConfiguration.cs`. It is a rule
now:

> `Option<T>` for domain absences and repository lookups. Nullable references
> for persisted state, because EF maps them and does not map `Option<T>`.

### `Shared.Kernel` converts, and its four sites are the interesting ones

The only project that resisted NRT is the one implementing the alternative to
it, and it resisted in exactly the four places it deliberately holds a null:

```diff
-    public EnsuredGuid IsNotEmpty(string message = null)
+    public EnsuredGuid IsNotEmpty(string? message = null)
-    public override bool Equals(object obj) => obj is Option<T> other && Equals(other);
+    public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);
-        new(value, default, isSuccess: true);      // Result.Success has no error
+        new(value, default!, isSuccess: true);
-        new(default, error, isSuccess: false);     // Result.Failure has no value
+        new(default!, error, isSuccess: false);
```

Each states something true rather than suppressing something. `object?` is the
correct signature for the override and was wrong before. The two `default!`
mark slots guarded by `isSuccess` and never read.

## Consequences

**Easier.** One place states the setting. A `?` means the same thing
everywhere except eight named projects. The guard-test spelling stops depending
on which csproj you are in.

**Harder.** Converting the eight is now visible work rather than an absence
nobody counted. `ServiceDefaults` at 56 diagnostics is the real one.

**Unchanged.** No runtime behaviour, no schema, no wire format. The whole
solution builds clean in Release with no warnings, and every unit suite passes.

**Not delivered.** The eight exceptions are not converted here. Doing that in
the same change would mix a governance correction with 234 diagnostic fixes,
and the fixes deserve their own review.

## Alternatives Considered

**Make the code match ADR-0048** — delete the 65 overrides and let everything
inherit `disable`. Measured: **274 errors**, of which 154 are `CS8632` — every
`Type?` annotation in the codebase becoming illegal and having to be deleted,
losing the information it carries. It honours the written decision by stripping
a safety net that 42 production projects rely on today. Rejected.

**Enable everywhere with no exceptions** — **234 diagnostics** across the eight
projects above, measured per project because a whole-solution build stops at
the first failure. Rejected for this change, not forever: it is the same end state,
reached after the backlog above is converted. Bundling it here would put a
governance correction and 234 fixes in one review.

**Leave the settings alone and document the split** — amend ADR-0048 to say
"enabled per project by convention". Cheapest, and it does stop `CLAUDE.md`
asserting something untrue. Rejected because it preserves the
trap: 65 silent overrides and nine silent inheritances, with no way to tell an
exception from an oversight. The point is not that the majority wins; it is
that a default nobody states is a default nobody can check.

## Implementation Notes

**A measurement mistake is recorded here because it changed the recommendation
mid-flight.** The first comparison on issue #2025 reported option A at *8
errors in 1 project*. That was a **short-circuited build** — `Shared.Kernel`
failed, so nothing depending on it compiled, and the list looked complete
because the build stopped rather than because it finished. The true figure is 234 — and even the
corrected 138 was short of it, because that run stopped before `ScenarioSimulator`
and the two Application test projects compiled. Only a per-project build settles
it. The named-exception variant exists because the first correction made it
obvious; the second correction only made it more so.

The same family of error produced a wrong public claim earlier the same day, on
issue #2022, where `dotnet ef --no-build` read Debug binaries built on another
branch. **A build-based measurement is only complete if the build completed.**
