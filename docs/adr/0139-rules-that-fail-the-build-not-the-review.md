# ADR-0139: Rules that fail the build, not the review

**Status:** **Accepted**
**Date:** 2026-09-02
**Extends:** ADR-0105 (argument guards on `Ensure.That`), ADR-0059
**Amends:** Constitution §II (Domain-Driven Design with Value Objects), Constitution §Testing
**Supersedes:** —
**Superseded by:** —

## Context

Three rules in this repository are stated but unenforced. All three have
drifted, and the drift was found by measurement, not by review.

| Rule | Written in | State on 2026-09-02 |
|---|---|---|
| Argument guards use `Ensure.That(...)` | ADR-0105, which converted ~277 sites | 2 production sites had reverted to `ArgumentNullException.ThrowIfNull` |
| Value objects are the default; primitives do not cross domain boundaries | §II | 9 `string` and 26 `DateTimeOffset` properties on aggregates |
| Domain logic is TDD red-green-refactor | §Testing | the Phase 4 gate checks only that tests are **green** |

This is a defect class this repository has already named twice: §IV recorded a
latency leg as unbuilt after it was built, and the Phase 3 board gate went
unfollowed for sixteen specs. In each case the record was not wrong when
written — it was simply never checked against what was happening.

Two further findings sharpened the problem.

**ADR-0105 is narrower than it reads.** It converted *null* guards. Argument
preconditions on strings and numeric ranges — `ArgumentException.ThrowIfNullOrWhiteSpace`,
`ArgumentOutOfRangeException.ThrowIfLessThan` — were never in its scope, and 29
such sites exist. So the repository has had two idioms for the same concept
all along, and reviews had to police a boundary the ADR never drew.

**The testing rule cannot be followed as written.** It binds "domain logic"
only, and read as a blanket red-first requirement it is unsatisfiable by
behaviour-preserving work: a refactor's tests must stay *green* throughout, and
a red test during one is a regression, not a step. Spec 057 — which
implements this ADR — would have been the first change unable to comply with
its own governing rule.

## Decision

### 1. The primitive boundary is named exhaustively

§II is amended to list the disallowed types rather than illustrate them with
three examples. On a domain model, these do not appear: `string`, `int`,
`bool`, `double`, `decimal`, `float`, `long`, `Guid`, `DateTimeOffset`.

Four exemptions, each with its reason:

- **`ApiError(Code, Message, Status)`** — a serialization contract (ADR-0089).
  Its strings cross the wire as themselves.
- **Opaque captured payloads** — `DeadLetter.RawPayload`, `AuditEvent.Payload`.
  Exempt from being *parsed or interpreted*, not from having a type: both get
  one that enforces non-emptiness. The content is captured verbatim for
  post-mortem and the system must not care what is in it.
- **A value object's own backing value** — `CameraName.NormalizedValue` and
  its kind. A string-backed value object must expose its string somewhere;
  that is the boundary the rule protects, not a breach of it.
- **`Shared.Contracts`** — a wire format, primitives by design.

### 2. Argument guards cover all preconditions, and the ban is a build error

ADR-0105 is extended from null guards to **every** argument precondition —
null, emptiness, and range. The following are banned:

| Banned | Replacement |
|---|---|
| `ArgumentNullException.ThrowIfNull` | `Ensure.That(x).IsNotNull()` |
| `ArgumentException.ThrowIfNullOrWhiteSpace` | `Ensure.That(x).IsNotNull().IsNotNullOrWhiteSpace()` |
| `ArgumentException.ThrowIfNullOrEmpty` | `Ensure.That(x).IsNotNull().IsNotNullOrWhiteSpace()` |
| `ArgumentOutOfRangeException.ThrowIfLessThan` | `Ensure.That(x).AtLeast(n)` |
| `ArgumentOutOfRangeException.ThrowIfGreaterThan` | `Ensure.That(x).InRange(lo, hi)` |
| `ArgumentOutOfRangeException.ThrowIfNegative` | `Ensure.That(x).AtLeast(0)` |

The last three have **zero call sites today** and are banned anyway. A
prohibition is forward-looking by nature; its purpose is the call site nobody
has written yet. Banning one of a pair and leaving its sibling legal is worse
than banning neither, because the omission reads as a deliberate carve-out.

Enforcement is `Microsoft.CodeAnalysis.BannedApiAnalyzers` with
`RS0030` at `error` — the mechanism already used for ADR-0049 — via a second
ban list at `build/guards/BannedSymbols.txt`.

**`.IsNotNull()` must be chained** ahead of `.IsNotNullOrWhiteSpace()` where a
null input should keep raising `ArgumentNullException`. `Ensure`'s string
chain collapses null and whitespace to `ArgumentException`, where the BCL
helper distinguishes them. `ArgumentNullException` derives from
`ArgumentException`, so nothing catches the difference and no test in this
repository observes it — which is exactly why it is written down here rather
than left to be discovered.

**Scope.** This ban binds more broadly than ADR-0049's: it covers `Shared.*`
and `tests/` as well as the bounded contexts. ADR-0049 exempts `Shared.*`
because such a library might one day ship standalone and legitimately need
`ConfigureAwait`; no equivalent argument exists for argument guards, since
`Ensure` *lives* in `Shared.Kernel`.

Two exemptions remain, by two different mechanisms:

- **`AppHost`**, by project. It does not reference `Shared.Kernel` by design,
  so the replacement is not merely unused there — it is unavailable. It
  currently contains **zero** guards of any kind, so the exemption is presently
  unexercised; it is kept because the reason for it has not changed.
- **`**/Migrations/*.cs`**, by path, via `.editorconfig` severity.

On migrations, ADR-0105's stated reason — "regenerated by tooling; never
hand-edited" — is **not accurate** and is corrected here. This repository's
migration files carry hand-written XML doc comments explaining their intent.
The exemption stands on the narrower and true ground: they *can* be
regenerated, and an inline suppression would not survive it.

### 3. The testing rule splits in two

§Testing's single bullet is replaced by two obligations, distinguished by
what the change does rather than by which layer it touches.

- **New behaviour — red first.** The test is written first, is **observed
  failing**, and the failure is quoted in the PR body. This applies to domain,
  application and infrastructure alike, not to domain logic only.
- **Behaviour-preserving refactor — green throughout.** The inverse
  obligation. Covering tests must exist and pass *before* the change and stay
  passing after. A red test at any point is a regression, not a step. Where a
  path being changed has no covering test, one is added first, while the old
  shape still compiles.

The Phase 4 gate in `CLAUDE.md` gains the corresponding evidence requirement.
Quoting an observed failure is the only honest proof available: nothing in CI
can establish after the fact that a test was written before the code.

## Consequences

**Easier.** The guard rule stops depending on reviewer memory — it fails at
the engineer's desk, in seconds, naming its replacement. The primitive
boundary becomes answerable from the constitution alone. The testing rule
becomes something a change can actually comply with, in both directions.

**Harder.** Adding a legitimate new guard idiom now requires amending a ban
list, not just writing code. Two ban lists with different scopes exist, which
a reader must not conflate. And `Shared.*` and `tests/` lose an exemption they
had under ADR-0049 — deliberately, but it is a real narrowing.

**Not delivered.** The guard rule becomes mechanical; the **primitive** rule
and the **TDD** rule do not. Both remain enforced by review and by a PR
quotation. They are stronger than before, because both now state their scope,
their exemptions and their evidence — but neither fails a build. A future ADR
could add an architecture test asserting no aggregate exposes a banned
primitive. This one does not, and recording that plainly is preferable to
implying a guarantee nobody built.

**Cost.** Roughly 200 mechanical edits across ~70 files, sequenced so that the
enforcement and the guard conversions land first and independently.

## Alternatives Considered

**An architecture test scanning source text**, instead of the analyzer.
Rejected: strictly weaker. It fails in CI rather than at the keyboard, minutes
later instead of seconds, and a `NetArchTest`-style rule cannot see a call
site's syntax at all — it would have to grep, which is what the analyzer does
properly.

**Appending to the existing `BannedSymbols.txt`** rather than adding a second
list. Rejected: it would silently widen ADR-0049's `ConfigureAwait` ban into
`Shared.*` and tests, changing an unrelated decision as a side effect of this
one.

**Naming the second list `BannedSymbols.Guards.txt`.** Rejected on evidence.
The analyzer matches additional files by the prefix `BannedSymbols.`, and
[roslyn-analyzers#5622](https://github.com/dotnet/roslyn-analyzers/issues/5622)
records that multi-file support has already regressed once. Both files are
therefore named `BannedSymbols.txt` and distinguished by directory, which both
the old and the current matching logic accept.

**Leaving the testing rule as one blanket red-first bullet.** Rejected: it is
unsatisfiable by refactoring, and a rule that a change cannot comply with is
how the other two rules on this page drifted.

**Treating `DateTimeOffset` as an accepted primitive**, exempting 26
properties. Rejected: §II's own illustration already promises that "a
`Timestamp` knows whether it is `source` or `ingestion` time-based", and two
such types (`OccurredAt`, `IngestedAt`) have existed in EventIngestion since
spec 006. The pattern was never propagated; exempting it would have written
the omission into the rule.

**Exempting `int ExpectedVersion`** as an infrastructure concern. Considered
seriously — the aggregate version arrives as an HTTP `If-Match` value and is
arguably not domain vocabulary. Rejected as a deliberate scope decision. The
consequence is `AggregateVersion` in `Shared.Kernel`, admissible there on the
same basis as `Result<T, E>` and `Option<T>`: a language-level concept, not
domain vocabulary.

## Implementation Notes

Spec `057-primitives-out-of-the-domain` implements this ADR in eight phases.
Two details are load-bearing and easy to get wrong:

- **`AggregateVersion` must be a `record`.** EF Core derives the concurrency
  value comparer from the type's equality. A `class` without value equality
  yields reference comparison, and every optimistic-concurrency check silently
  passes writes it should refuse. Nothing fails to compile.
- **The ban must be observed failing before anything is converted.** A ban
  list the analyzer never reads produces no diagnostic and is indistinguishable
  from full compliance. A green build at that point is the failure signal.

Nullable persisted properties keep nullable value-object references rather
than `Option<T>`, per the existing ADR-0048 carve-out documented in
`StreamConfiguration.cs` and `Stream.cs`. `Option<T>` is mapped in zero EF
configurations.
