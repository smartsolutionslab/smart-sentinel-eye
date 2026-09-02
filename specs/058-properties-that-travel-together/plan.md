# Implementation Plan: Properties that travel together become one value object

**Branch**: `058-properties-that-travel-together` | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/058-properties-that-travel-together/spec.md`

## Summary

Twelve pairs of properties that are always written together become twelve
composite value objects — 24 loose properties down to 12. Nine of the twelve
are a timestamp beside an actor; the rest are the audit row's actor and
username, its payload and size, and a rule's trigger source and kind.

The technical approach is settled by two experiments recorded in
[research.md](./research.md). An EF owned reference maps a composite onto the
**existing** columns, in the same table, at both nesting depths, with value
converters working on struct and record components alike — provided
`Navigation(...).IsRequired()` is present, without which both columns silently
become nullable. And filtering or ordering on a composite's component still
translates server-side, so the audit context's indexed read path is unaffected.

Sequencing follows risk, smallest first: StreamDistribution's single site is
the proof, the other eight timestamp/actor sites follow mechanically, and
AuditObservability lands last because it is the only context whose write path
is hand-authored SQL rather than the mapping every other context shares.

**One composite is not like the others.** `StoredPayload` derives its size from
its content rather than accepting both, which is the only part of this feature
that removes a defect rather than improving how the model reads.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.400)

**Primary Dependencies**: EF Core 10.0.11 + Npgsql 10.0.3 (owned references,
value converters); xUnit + Shouldly + Moq (tests); SonarAnalyzer (metrics)

**Storage**: PostgreSQL. **No schema change** — every composite occupies the
columns its parts occupy today, verified per context by
`has-pending-model-changes`.

**Testing**: xUnit; domain unit tests per composite; existing tests stay green
throughout. Integration via `AspireFixture` (Docker, ADR-0103) — **CI only**,
this machine has no Docker.

**Target Platform**: Linux server (k3s) / Windows dev

**Project Type**: Backend only. The two React apps are untouched, and FR-008
requires that they cannot tell this happened.

**Performance Goals**: None — this feature is on no latency leg. The one
performance *risk* (an indexed audit query falling back to client evaluation)
was closed by research R2.

**Constraints**: Behaviour-preserving throughout. No HTTP surface change, no
message change, no migration. Constitution §Testing's green-throughout
obligation applies; its red-first obligation does not, because no new behaviour
is introduced.

**Scale/Scope**: 10 new value objects, 9 aggregates, 9 EF configurations, ~12
mappers and handlers, plus the audit write path and archiver. 8 contexts;
`EventIngestion` is untouched — its aggregates have timestamps but no actor.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **§II — DDD with value objects** | This feature *is* §II applied one level up: it groups properties whose relationship the model does not currently express. No primitive is introduced; each composite's components are existing value objects. **The composites are themselves value objects whose components are value objects**, so §II's backing-value exemption is not even reached. |
| **§III — Bounded context isolation** | **Load-bearing, and the reason for FR-002.** Seven near-identical composites exist rather than one shared type precisely because a shared one would need a cross-context reference or a `Shared.Kernel` type carrying domain vocabulary. The duplication is the constitution's price, paid deliberately. NetArchTest continues to enforce it. |
| **§IV — Latency budget** | Not on the event-to-overlay path. No leg affected. The audit read path is not a budget leg, and R2 confirmed its SQL is unchanged regardless. |
| **§V — Spec-driven development** | This document. Phases 1–3 run before any code. |
| **§VI — Aspire is the composition root** | No new runtime resource. |
| **§VII — Observability** | No new signal, no leg becoming subject. |
| **§VIII — Safe by default at trust boundaries** | Unchanged. Composites are constructed inside the domain from already-validated parts; no new trust boundary appears. |
| **§IX — Forward-compatible strategy interfaces** | Not applicable. |
| **§Testing** | Green-throughout, not red-first — behaviour-preserving work. Where a pair has no covering test, one is added first while the old shape compiles (FR-009). |
| **Coverage gates (ADR-0065)** | **The live risk.** Ten new types land in gated Domain assemblies at ≥ 90%. Spec 057 failed CI this exact way. Every composite ships with its own test file in the same task, not a later one. |
| **Code metrics (ADR-0084)** | Each composite is well under 300 LOC; no method grows. |

**Gate: PASS.** No violation to justify, so Complexity Tracking is omitted.

One dependency, not a violation: §II's plural backing-value exemption and its
identity-reference carve-out arrive with ADR-0140 on PR #2021, which is
unmerged. This branch is cut from `develop`, where §II still reads as the older
nine-type list. The composites are consistent with both readings.

## Project Structure

### Documentation (this feature)

```text
specs/058-properties-that-travel-together/
├── plan.md              # This file
├── spec.md              # Phase 1 output (/speckit-specify)
├── research.md          # Phase 0 output — two EF experiments
├── data-model.md        # Phase 1 output — the ten composites
├── quickstart.md        # Phase 1 output — one site, start to finish
├── contracts/README.md  # Phase 1 output — the absence of contract change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Only the paths this feature touches. Every context follows the same four-file
shape, which is why the nine sites are mechanical after the first.

```text
src/
├── StreamDistribution/          # slice 1 — the proof
│   ├── Domain/Stream/Provisioning.cs                          (new)
│   ├── Domain/Stream/Stream.cs                                (pair → composite)
│   ├── Infrastructure/Persistence/Configurations/StreamConfiguration.cs
│   └── Application/**                                          (readers renamed)
├── CameraCatalog/               # slice 2 — Registration
├── Identity/                    # slice 2 — Registration
├── Automation/                  # slice 3 — Creation + Trigger
├── SystemVariables/             # slice 3 — Creation
├── LayoutComposition/           # slice 4 — Creation ×2 (aggregate + revision)
├── OverlayDesigner/             # slice 4 — Creation ×2 (aggregate + revision)
└── AuditObservability/          # slice 5 — Actor + StoredPayload, last
    ├── Domain/AuditEvent/Actor.cs, StoredPayload.cs           (new)
    ├── Infrastructure/Persistence/AuditEventRepository.cs      (hand-written INSERT)
    └── Infrastructure/Archive/MinioAuditChunkArchiver.cs       (projection)

tests/
└── <Context>.Domain.Tests/**    # one test file per composite, same task
```

Untouched: `src/EventIngestion` (timestamps without actors), `src/Shared.Contracts`
(FR-008), `apps/**` (frontend), `deploy/**`.

**Structure Decision**: No new project, no new layer. Each composite lives in
its aggregate's Domain folder beside the types it wraps (ADR-0092), and each
context's existing EF configuration gains an `OwnsOne` block replacing two
`Property` calls.

## Phase sequencing

Ordered by risk and by what can be abandoned without unpicking anything else.

| Slice | Contents | Why here |
|---|---|---|
| 1 | StreamDistribution — `Provisioning` | Smallest possible proof of the whole pattern: one aggregate, one configuration, one site. If the shape reads badly, little was spent. |
| 2 | CameraCatalog, Identity — `Registration` | Two contexts, same shape, no revisions. Confirms the pattern transfers before the nested case. |
| 3 | Automation, SystemVariables — `Creation`, `Trigger` | Adds `Trigger`, the second-simplest composite, alongside a familiar one. |
| 4 | LayoutComposition, OverlayDesigner — `Creation` ×2 each | The nested case: a composite inside an owned collection. Research R1 says it works; this is where that is proven on real code. |
| 5 | AuditObservability — `Actor`, `StoredPayload` | Last and largest. The only hand-written write path, the only archival projection, and the only composite that derives a component. |

Each slice is independently shippable and leaves the codebase consistent — SC-006.

## Phase 1 agent-context update

`CLAUDE.md` still contains no `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->`
markers, so the automated plan-reference update had nothing to write into —
the same finding spec 057's plan recorded, unchanged since.

Markers were **not** injected, for 057's reason and one of this feature's own:
`CLAUDE.md` is hand-curated, and this feature does not edit it at all. Adding a
machine-managed block would be this plan's only change to a governance file,
which is not a side effect a planning step should have. Whether to adopt the
markers remains its own decision.

## Risks

| Risk | Mitigation |
|---|---|
| A composite silently makes its columns nullable | `Navigation(...).IsRequired()` in every configuration, and `has-pending-model-changes` run per slice. This is issue #2022's failure mode, and it fails no test. |
| Coverage gate trips on the new types | Each composite ships with its test file in the same task. Spec 057 failed CI this way; the fix there was deleting unused surface, and the same rule applies — copy no member that has no caller. |
| The audit slice is larger than it looks | Sequenced last, and its extra paths (hand-written `INSERT`, archiver) are named in the spec and data model rather than discovered during implementation. |
| A pre-existing row whose payload size is wrong | Reconstruction preserves it; repair would be a migration (FR-004). Recorded in the data model rather than silently assumed away. |
| Integration and e2e evidence unavailable locally | CI is the gate. The PR must say so rather than implying a full local run. |
