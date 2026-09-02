# Implementation Plan: Primitives out of the domain, guards onto `Ensure`

**Branch**: `057-primitives-out-of-the-domain` | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/057-primitives-out-of-the-domain/spec.md`

## Summary

Three rules that this repository already holds — argument guards on `Ensure`
(ADR-0105), value objects over primitives (constitution §II), and red-green
for domain logic (constitution §Testing) — are stated but unenforced, and all
three have drifted. This feature converts the ~200 remaining sites and, more
importantly, makes each rule fail the build rather than a review.

The technical approach is settled by four experiments recorded in
[research.md](./research.md): a second banned-symbols file at
`build/guards/BannedSymbols.txt` (same filename, different directory) coexists
with the existing one; a value-converted property survives as an EF
concurrency token with the column type unchanged, provided the version type is
a `record`; converting the BCL string guards is *not* exception-preserving and
needs an explicit `.IsNotNull()` in the chain; and generated migrations must be
exempted by path, not by the analyzer's generated-code option.

Sequencing is deliberate: enforcement plus guard conversions land first as one
independently valuable change, and the `AggregateVersion` conversion — the
widest surface and the only real technical risk — lands last, so it can be
abandoned without unpicking anything else.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.400)

**Primary Dependencies**: Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0
(enforcement), EF Core 10.0.11 + Npgsql 10.0.3 (value converters),
SonarAnalyzer (metrics), xUnit + Shouldly + Moq (tests)

**Storage**: PostgreSQL. **No schema change** — every conversion is a value
converter onto the existing column.

**Testing**: xUnit; domain unit tests; integration against the real Aspire
stack via `AspireFixture` (Docker required, no Testcontainers, ADR-0103)

**Target Platform**: Linux server (k3s) / Windows dev

**Project Type**: Backend only. The two React apps are untouched.

**Performance Goals**: None. This feature is on no latency leg — see the
constitution check below.

**Constraints**: Behaviour-preserving throughout. No HTTP surface change, no
schema change, no migration generated. Every commit builds on its own
(ADR-0087, rebase-merge).

**Scale/Scope**: ~200 edits across ~70 files. 9 text types, 26 timestamp
types, 1 `AggregateVersion`, ~54 guard conversions, 11 EF configurations,
10 concurrency-token mappings, 1 ADR, 2 governance documents.

## Constitution Check

*GATE: evaluated before Phase 0, re-evaluated after Phase 1 design.*

| Principle | Verdict | Note |
|---|---|---|
| **II — DDD with value objects** | **Advances it** | This is the principle the feature serves. §II is also *amended* by ADR-0139 to name the primitives exhaustively. |
| **III — Bounded context isolation** | Pass | No cross-context reference added. Timestamp types are per-context precisely to avoid one — see data-model.md. |
| **IV — Latency budget** | **Not on the path** | No leg affected. No event-to-overlay code changes behaviour; retyping is compile-time. Stated explicitly because §IV requires every PR touching the path to cite its legs — this one does not touch it. |
| **VII — Dashboard rule** | N/A | Binds implemented latency legs; none are altered. |
| **IX — Forward-compat interfaces** | Pass | No speculative generality. The ban list is trimmed to idioms the codebase actually contains (contracts §1). |
| **Testing — TDD** | **Amended, then followed** | §Testing is split into a red-first and a green-throughout obligation. This feature is the first to comply with both, and could not have complied with the rule as previously written. |
| **Governance — amendments need an ADR** | Pass | ADR-0139 carries both amendments. |
| Rebase-only, commits build alone (ADR-0087) | Pass | Story 5 commits per aggregate. |
| `Ensure.That` guards (ADR-0105) | **Extends it** | From null-only to all argument preconditions. |
| Marten / event sourcing | Pass | Unused, unchanged (ADR-0130). |
| Code metrics (ADR-0084) | Watch | 9 + 26 new types are small files; no method grows. |
| Coverage gates (ADR-0065) | Watch | New domain types land in the ≥ 90% bucket. |

**Gate result: PASS.** Two principles are amended rather than violated, by the
procedure the constitution itself prescribes. The Complexity Tracking table
below is therefore empty.

**Post-Phase-1 re-evaluation: PASS, unchanged.** The design added one type to
`Shared.Kernel` (`AggregateVersion`); data-model.md justifies it on the same
basis as the `Result<T, E>` and `Option<T>` already there — a language-level
concept, not domain vocabulary — so "Shared.Kernel holds no domain" still
holds.

## Project Structure

### Documentation (this feature)

```text
specs/057-primitives-out-of-the-domain/
├── spec.md                          # Phase 1 output (/speckit-specify)
├── plan.md                          # This file
├── research.md                      # Phase 0 — four experiments
├── data-model.md                    # Phase 1 — the exact 36 types
├── quickstart.md                    # Phase 1 — five verification checks
├── contracts/
│   └── enforcement-contract.md      # Phase 1 — ban set, scope matrix, unchanged surfaces
├── checklists/
│   └── requirements.md              # Spec quality validation
└── tasks.md                         # Phase 2 (/speckit-tasks — NOT created here)
```

### Source code touched

```text
build/guards/BannedSymbols.txt        NEW — the guard ban list
BannedSymbols.txt                     unchanged (ConfigureAwait, ADR-0049)
Directory.Build.props                 second AdditionalFiles item, own condition
.editorconfig                         RS0030 = none under **/Migrations/*.cs
.specify/memory/constitution.md       §II amended; §Testing split in two
CLAUDE.md                             Phase 4 gate row; value-object house rule
docs/adr/0139-*.md                    NEW — carries both amendments

src/Shared.Kernel/
├── AggregateRoot.cs                  Version : int → AggregateVersion
├── Primitives/IVersionedAggregate.cs Version : int → AggregateVersion
└── Primitives/AggregateVersion.cs    NEW (record — see research R2)

src/<Context>/Domain/<Aggregate>/     9 text types, 26 timestamp types
src/<Context>/Application/            command/query shapes take value objects
src/<Context>/Api/                    parse at the boundary → Result<T, ApiError>
src/<Context>/Infrastructure/
├── Persistence/Configurations/       11 files: HasConversion added
└── Cameras/CameraCatalogFabGuard.cs  the 2 live violations

tests/<Context>.Domain.Tests/         9 invariant tests (written red first)
tests/**/Fakes/                       ~28 guard conversions
tests/Architecture.Tests/             optional: a rule asserting the ban is wired
```

**Structure Decision**: No new project, no new layer. The feature edits
existing bounded contexts in place plus four repo-root configuration files.
The only new shared type is `AggregateVersion`.

## Implementation sequence

Ordered by the spec's story priorities, with the risk deliberately last.

| # | Stories | Content | Lands as |
|---|---|---|---|
| 1 | 1, 6 | ADR-0139, constitution §II + §Testing, CLAUDE.md gate, ban list + scoping, all ~54 guard conversions | One PR. Ban and conversions in **one commit** — the ban breaks the build until the conversions land (contracts §3). |
| 2 | 2 | 9 text types, per context | One PR per context or one for all nine; each type test-first. |
| 3 | 3 | 26 timestamp types, per context | One PR per context. |
| 4 | 4 | Boundary conversions | Follows 2 and 3; needs their types to exist. |
| 5 | 5 | `AggregateVersion` | **One commit per aggregate.** First aggregate proves the concurrency behaviour (FR-023) before the other nine. |

Step 1 is independently valuable and closes the live drift; if the feature
stopped there it would still have paid for itself. Step 5 is severable: if the
concurrency token fights EF in ways research R2 did not surface at model level,
it can be dropped without touching steps 1–4.

## Risks

| Risk | Likelihood | Handling |
|---|---|---|
| Value-converted concurrency token misbehaves at runtime | Low — model validates, column unchanged (R2) | One aggregate first; characterization tests before retyping (FR-023, FR-024) |
| `AggregateVersion` written as a `class` | Low, **high impact** | Would silently pass stale writes. Called out in data-model.md; a test must assert refusal, not just compilation |
| Ban list placed where the analyzer ignores it | Low — settled by R1 | Quickstart check 1 requires observing red; a green build proves the ban is *not* wired |
| Exception type widens on string guards | Certain if unhandled (R3) | Chain `.IsNotNull()` explicitly |
| A timestamp loses EF query translation | Medium | Carry the implicit unwrap; audit which of the 26 are queried/ordered |
| Commit doesn't build alone under rebase-merge | Medium — 92-site change | Step 5 committed per aggregate; verify per commit, not per branch |
| Scope creep into `Shared.Contracts` | Low | Explicitly out of scope; wire format keeps primitives |

## Complexity Tracking

No constitution violations to justify. §II and §Testing are **amended** via
ADR-0139 under the constitution's own amendment procedure, not violated.

## Phase 1 agent-context update

`CLAUDE.md` contains no `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->`
markers, so the automated plan-reference update had nothing to write into.

Markers were **not** injected. `CLAUDE.md` is hand-curated here, and this
feature already edits it as a deliverable (the Phase 4 gate row and the
value-object house rule). Adding a machine-managed block in the same change
would mix a tooling convention into a governance edit — and the guidance in
that file is explicit that it should be trusted over conflicting automation.
Whether to adopt the markers is its own decision, not a side effect of this
plan.
