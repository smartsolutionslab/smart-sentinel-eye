# Phase 0 Research: Primitives out of the domain, guards onto `Ensure`

**Feature**: 057 | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

Four unknowns carried real failure modes. All four were settled by
experiment rather than by reasoning, because three of them fail *silently* —
a ban that never fires, a schema that quietly changes, an exception type that
widens without any test noticing.

---

## R1 — Can a second banned-symbols file coexist with the existing one?

**Decision**: Yes. Add `build/guards/BannedSymbols.txt` as a second
`AdditionalFiles` entry with its own MSBuild condition. Both files are read
and their bans are merged.

**Why this was in doubt**: The repo already ships a root `BannedSymbols.txt`
for the ConfigureAwait ban, scoped in `Directory.Build.props` to non-test,
non-`Shared.*` projects. This feature's ban must bind **more** broadly (it
includes `Shared.*` and tests), so it cannot simply be appended to the
existing file — it needs different scoping, therefore a second file.

[Issue #5622](https://github.com/dotnet/roslyn-analyzers/issues/5622) reports
exactly this not working: with `BannedSymbols.txt` and
`BannedSymbols.Common.txt`, only the former was honoured. The issue is
**Closed with no stated resolution**, so its status against our pinned 5.6.0
could not be read off the tracker.

**Experiment**: A scratch project referencing
`Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0 (the pinned version), with
two additional files carrying distinguishable messages, and one call site for
each ban.

```text
warning RS0030: The symbol 'Task.ConfigureAwait(bool)' is banned … FROM-FILE-ONE
warning RS0030: The symbol 'ArgumentNullException.ThrowIfNull(object?, string?)' is banned … FROM-FILE-TWO
```

Both fired. The bug is fixed in 5.6.0. Corroborating evidence from the
assembly itself: the analyzer's string table contains the literal
`BannedSymbols.` — a **prefix**, not the full `BannedSymbols.txt` — which is
the shape of a multi-file match.

**Consequence for the design**: the second file must still be *named*
`BannedSymbols.txt`; only its directory differs. A file named
`BannedSymbols.Guards.txt` was the obvious first design and is the one thing
here that must **not** be done — the earlier phrasing of this feature proposed
exactly that name. It would be read under 5.6.0's prefix match, but it ties
the ban to an undocumented prefix behaviour that issue #5622 shows has already
regressed once. Same filename, different folder, is matched by both the old
and new logic.

**Alternatives rejected**:

- *Append to the existing root file and widen its scope* — would silently
  extend the ConfigureAwait ban into `Shared.*` and tests, changing an
  unrelated ADR-0049 decision as a side effect.
- *An architecture test scanning source text* — strictly weaker: it fails in
  CI rather than at the keyboard, and SC-001 asks for a desk-time failure.

---

## R2 — Does a value-converted property survive as a concurrency token?

**Decision**: Yes. The conversion is structurally supported and
schema-neutral. Story 5 proceeds as specified, still verified one aggregate
at a time (FR-023) because this experiment settles *model* validity, not
runtime stale-write behaviour.

**Why this was in doubt**: This is the single technical risk the spec names.
A property that is both value-converted and `IsConcurrencyToken()` is a known
rough edge, and if it were unsupported, Story 5 would have to be dropped.

**Experiment**: Built the real model shape against the pinned Npgsql provider
(10.0.3) — an `AggregateVersion` record converted to `int` and marked as the
concurrency token — and inspected the finalized model.

| Property | Result |
|---|---|
| Model validation | passes |
| `IsConcurrencyToken` | `True` — survives the conversion |
| Value converter | present |
| Provider CLR type | `System.Int32` |
| Column type | `integer` — **unchanged**, so SC-004 holds |
| Value comparer | auto-generated |

**The load-bearing detail**: the comparer is what EF uses to decide whether
the original and current token differ. It is auto-generated here **because
`AggregateVersion` is a `record`**, which gives structural equality for free.
A `class` without value equality would produce a comparer that compares
references, and every concurrency check would silently mis-fire. This is not
a stylistic preference — the version type **must** be a record, or must
supply an explicit comparer.

**What is still unproven**: that a stale write is actually refused end to end.
That needs a live database and is exactly what FR-023's one-aggregate-first
sequencing exists to establish. Phase 0 has removed the "is it even possible"
risk; the behavioural proof stays in implementation.

**Alternatives rejected**:

- *Leave the version primitive* — was offered and declined; recorded here
  only to note that R2 removes the technical grounds for reconsidering.
- *A shadow property holding the raw token* — keeps the concurrency check on
  an `int` but reintroduces the primitive one layer down, defeating the point.

---

## R3 — Does converting the string guards preserve behaviour?

**Decision**: No, not by default — and the gap is real. Where a converted
call site must keep raising `ArgumentNullException` for a null input, chain
the guard explicitly:

```csharp
Ensure.That(topic).IsNotNull().IsNotNullOrWhiteSpace();
```

**The divergence**, read from `src/Shared.Kernel/Ensure.cs:76`:

| Input | `ArgumentException.ThrowIfNullOrWhiteSpace` (current) | `Ensure.That(s).IsNotNullOrWhiteSpace()` |
|---|---|---|
| `null` | `ArgumentNullException` | **`ArgumentException`** |
| `"   "` | `ArgumentException` | `ArgumentException` |

`Ensure`'s single-call form collapses both cases to `ArgumentException`. This
contradicts FR-012 as written, and would be invisible to a `catch
(ArgumentException)` — since `ArgumentNullException` derives from it — which
is precisely why it needed checking rather than assuming.

**Measured blast radius**: the repository contains **57**
`Should.Throw<ArgumentException>` assertions and only **3**
`Should.Throw<ArgumentNullException>` ones. None of the three sit on any of
the 13 files carrying the BCL string guards.

**So the risk is low but the fix is nearly free**, and "nearly free" is the
deciding factor: chaining `.IsNotNull()` first costs one call and makes
FR-012 true as written, rather than true-in-practice-because-nothing-checks.
A rule that holds by luck is the failure mode this whole feature exists to
correct.

**Alternatives rejected**:

- *Accept the widening and adjust any failing tests* — nothing would fail
  today, so this looks free; it silently weakens every future caller's
  contract.
- *Add an `IsNotNullOrWhiteSpace` overload that throws the BCL pair* — changes
  a shared helper's semantics for 250-plus existing callers to serve 24 new
  ones.

---

## R4 — How are generated migrations exempted?

**Decision**: A per-path `.editorconfig` severity rule.

```editorconfig
[**/Migrations/*.cs]
dotnet_diagnostic.RS0030.severity = none
```

**Why not the analyzer's own option**: the assembly exposes
`dotnet_banned_api_analyzer.exclude_generated_code`, which looked like the
purpose-built answer. It is not, for this repository: Roslyn's generated-code
heuristic keys on `GeneratedCodeAttribute` and on filename patterns such as
`.g.cs` / `.designer.cs`. **Zero** of this repo's migration files carry
`GeneratedCodeAttribute`, and they are named `20260817202813_DeadLetterFab.cs`
— so the option would exempt the `.Designer.cs` companions and miss the
migration bodies, which are where all 16 banned calls actually live. It would
have produced a half-working exemption that looked deliberate.

**An observation worth recording**: ADR-0105 exempts migrations on the
grounds that they are "regenerated by tooling; never hand-edited". That is not
quite true here — the migration files carry hand-written XML doc comments
explaining their intent. The exemption still stands, because they *can* be
regenerated and an inline suppression would not survive it. But the stated
reason is weaker than it reads, and ADR-0139 should restate it accurately
rather than repeat it.

**Alternatives rejected**:

- *`#pragma warning disable` in each migration* — does not survive
  regeneration, which is the entire reason the exemption exists.
- *Excluding migrations from the `AdditionalFiles` condition* — the condition
  is per-project, and migrations share a project with the infrastructure code
  that must stay covered.

---

## Consolidated decisions

| # | Decision | Basis |
|---|---|---|
| R1 | Second ban list at `build/guards/BannedSymbols.txt`, same filename, own MSBuild condition | Empirical: both files fire under 5.6.0 |
| R2 | `AggregateVersion` proceeds; **must be a `record`** for the comparer | Empirical: model validates, column stays `integer` |
| R3 | Chain `.IsNotNull().IsNotNullOrWhiteSpace()` on converted string guards | Read from `Ensure.cs:76`; 3 assertions repo-wide, none affected |
| R4 | Per-path `.editorconfig` severity for `**/Migrations/*.cs` | Zero migrations carry `GeneratedCodeAttribute` |

## Assumptions carried forward from the spec

The four defaults recorded in the spec's Assumptions section were reviewed
and none needed research to settle: per-context types over a shared base
(existing precedent), timestamps normalize without validating (so **no
`Ensure.That(DateTimeOffset)` overload is required** — this revises the
feature description's expectation that one would be), opaque payloads keep a
non-empty and size-bounded type, and covering tests precede any retyping of an
uncovered path.

## Sources

- [RS0030 BannedApiAnalyzer does not report symbols from multiple files — dotnet/roslyn-analyzers #5622](https://github.com/dotnet/roslyn-analyzers/issues/5622)
