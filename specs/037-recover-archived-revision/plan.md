# Implementation Plan: A layout or overlay archived by mistake can be recovered

**Branch**: `037-recover-archived-revision` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/037-recover-archived-revision/spec.md`

---

## Summary

A chain with no Published and no Draft revision may branch a new draft from its
newest Archived revision, carrying that revision's configuration. The operator
edits and publishes it, and the wall is live again — same identifier, same
history.

The domain change is one line per aggregate. The feature is not one line, because
**the guard it removes is written in three layers per aggregate** and because
opening this path opens a name-collision hole that has to be closed in the same
change.

### The four moving parts, honestly sized

| Part | Where | Size |
|---|---|---|
| The fallback | `Layout.BranchDraft`, `Overlay.BranchDraft` | 1 line + 1 private helper, each |
| The application guard | `BranchDraftRevisionCommandHandler` ×2 | The refusal condition narrows; a name check is added |
| FR-009's name check | Same two handlers | A repository call on a path that had none |
| The app | `LayoutsPage.tsx`, `OverlaysPage.tsx`, and two tests | Smaller than expected — see research §6 |

Plus **ADR-0121**, which is the point of the exercise: it decides that *archived*
means out of service, not unreachable.

### What Phase 0 changed about this plan

- **Smaller than expected**: the frontend. `LayoutsPage` already passes `newest`
  to its edit handler; the parameter is merely named `published`. No new dialog,
  no new prop, no second code path (research §6).
- **Smaller than expected**: FR-009 needs **no** `excluding` parameter. A fully
  archived chain is invisible to its own name lookup by construction (research §1).
- **Larger than expected**: an integration test per aggregate is required, not
  optional. The recovered draft clones an archived revision's EF-owned entities
  under a new owner in the same change-tracker, which is precisely what a fake
  repository cannot model (research §8).
- **A defect found and not absorbed**: a chain with a Published revision under an
  abandoned draft offers no row actions at all. Filed as issue 1879 (research §5).

---

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5.7 / React 19

**Primary Dependencies**: EF Core 10 + Npgsql, Wolverine, RTK Query. **No new
dependency** — this feature adds none in either stack.

**Storage**: PostgreSQL. Revisions are EF **owned entities** (`OwnsMany`) on the
chain. **No migration**: no column, table, index or constraint changes.

**Testing**: xUnit + Shouldly + hand-written fakes; Vitest + Testing Library;
integration through the Aspire fixture (ADR-0103, no Testcontainers).

**Target Platform**: k3s (prod), Aspire AppHost (dev). Unchanged.

**Project Type**: Web — two bounded contexts plus one React app.

**Performance Goals**: not on the §IV latency path. Recovery is a management-app
write; the only kiosk-visible effect is the ordinary publish that follows it,
which is unchanged.

**Constraints**: FR-009 adds one repository round-trip to the branch path, and
only on the recovery branch. Branching a published chain does one fewer query
than the recovery case and is unchanged.

**Scale/Scope**: 2 aggregates × 3 layers, 2 pages, 1 ADR, ~10 new tests, 2 changed
tests, 0 migrations.

---

## Constitution Check

*GATE: must pass before Phase 0. Re-checked after Phase 1 — see below.*

| Principle | Verdict | Note |
|---|---|---|
| **II. DDD with value objects** | **Pass** | No primitive crosses a domain boundary. The fallback is a private query on the aggregate over its own revisions; the aggregate stays the sole entry point for the state change. |
| **III. Bounded context isolation** | **Pass** | Nothing crosses between LayoutComposition and OverlayDesigner. The two changes are independent implementations of one decision, per ADR-0104 — which is the point of that ADR and the reason nothing is extracted. |
| **IV. Latency budget** | **Not on the path** | No leg affected. No integration event added, removed or changed. |
| **V. Spec-driven** | **Pass** | Spec → plan → tasks → implementation; every commit references a task. |
| **VII. Observability** | **Pass** | The existing `BranchedDraftRevision` log line covers the recovery path unchanged; recovery is not a distinct operation and does not want a distinct signal. |
| **VIII. Safe at trust boundaries** | **Pass** | Validation stays at the boundary it is already at. FR-009's refusal is a `Result` failure with an `ApiError`, not an exception. No new endpoint, so no new authorization surface — the recovery reuses the branch endpoint's existing fab scoping. |
| **IX. No speculative generality** | **Pass, and it is the live risk** | Two temptations rejected explicitly: an `excluding` parameter on the name lookup with exactly one correct value at every call site (research §1), and any extraction of the shared lifecycle, which ADR-0104 forbids until a *third* revisioned aggregate exists. |

**Post-Phase-1 re-check**: unchanged. The design added no abstraction, no
interface, no configuration knob and no contract.

---

## Project Structure

### Documentation (this feature)

```text
specs/037-recover-archived-revision/
├── spec.md
├── plan.md                     # this file
├── research.md                 # Phase 0 — nine findings
├── contracts/
│   ├── branch-draft-refusals.md    # what the branch path answers, per chain shape
│   └── archive-confirmations.md    # the replacement sentences, verbatim
├── quickstart.md               # how to see it work, and how to verify it
└── checklists/requirements.md
```

**No `data-model.md`.** The feature adds no entity, no field and no state. It
changes which existing state an existing operation will accept as a source. The
spec's Key Entities section already names the three concepts involved.

### Source code

```text
src/LayoutComposition/
  Domain/Layout/Layout.cs                         # the fallback + a private helper
  Application/Commands/Handlers/
    BranchDraftRevisionCommandHandler.cs          # narrowed guard + FR-009 check
  Application/Commands/BranchDraftRevisionErrors.cs  # FR-009's failure; FR-007's message

src/OverlayDesigner/                              # the same four files, mirrored
  Domain/Overlay/Overlay.cs
  Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs
  Application/Commands/BranchDraftRevisionErrors.cs

apps/management-web/src/features/
  layouts/LayoutsPage.tsx                         # gate + the confirmation sentence
  overlays/OverlaysPage.tsx                       # gate + the confirmation sentence

docs/adr/0121-archived-is-out-of-service-not-unreachable.md

tests/
  LayoutComposition.Domain.Tests/Layout/LayoutTests.cs
  LayoutComposition.Application.Tests/Commands/BranchDraftRevisionCommandHandlerTests.cs
  OverlayDesigner.Domain.Tests/Overlay/OverlayTests.cs
  OverlayDesigner.Application.Tests/Commands/BranchDraftRevisionCommandHandlerTests.cs
  Integration.Tests/LayoutComposition/LayoutLifecycleIntegrationTests.cs
  Integration.Tests/OverlayDesigner/OverlayRevisionLifecycleIntegrationTests.cs

apps/management-web/src/features/
  layouts/LayoutsPage.test.tsx                    # 1 assertion replaced, tests added
  overlays/OverlaysPage.test.tsx                  # 1 assertion replaced, tests added
```

---

## Phase 1 — Design

### The domain change

`BranchDraft` gains one fallback, and the fallback is narrow:

```csharp
Revision baseRevision = CurrentPublishedOrNull() ?? NewestWhenFullyArchivedOrNull()
    ?? throw new InvalidOperationException(
        "BranchDraft requires a Published revision, or a fully-archived chain to recover.");
```

`NewestWhenFullyArchivedOrNull()` returns the highest-numbered revision **only
when every revision is Archived**, and `null` otherwise. Written that way round
deliberately: the condition lives inside the helper, so the call site cannot
accidentally widen it to "the newest revision" — which is the mistake this whole
feature is one careless edit away from.

The throw message changes because the old one becomes wrong: a chain with an open
draft is refused, and telling that operator there is no Published revision to copy
from describes the situation without naming the reason.

**Nothing else in the aggregate moves.** `Publish`, `Revert`, `EditDraft` and
`ArchiveRevision` are untouched; `Revision.NewDraft`'s deliberate cloning of the
grid and tiles is untouched and is what makes FR-002 hold.

### The application change

The handler's pre-check narrows from *no Published revision* to *no Published
revision **and** an open Draft*, and gains FR-009's name check inside the recovery
branch only:

| Chain shape | Today | After |
|---|---|---|
| Has a Published revision | branch from it | **unchanged** |
| No Published, has a Draft | refused | **unchanged** — message now names the reason (FR-007) |
| Every revision Archived | refused | **branch from the newest archived revision** |
| Every revision Archived, name taken elsewhere | refused | refused, with a **new** reason (FR-009) |

Both handlers already destructure their command as the first statement after the
guard, per the house rule. That stays.

### FR-009, and why the check sits where it does

The name check runs **only** in the recovery branch. That is not an optimisation —
it is a correctness condition. A fully archived chain is excluded from
`GetByNameAsync` by the repository's own predicate, so any hit is necessarily a
different chain and no `excluding` parameter is needed. Hoist the same check onto
the published-branch path and it would match the chain against itself and refuse
every branch. The reason belongs in a comment at the call site, because the code
does not show it.

Scopes differ and each context uses its own: the layout's lookup takes the
recovering chain's `Fab`; the overlay's is global and takes none (research §3).

### The frontend change

The gate becomes `revisions.every(r => r.state === 'Archived')` — the chain, not
its last row (research §4). `LayoutsPage`'s edit handler already receives
`newest`; its parameter is renamed to say so. `OverlaysPage` only changes its
gate.

The archive confirmations are rewritten per
[contracts/archive-confirmations.md](./contracts/archive-confirmations.md), which
gives the sentences verbatim rather than leaving them to implementation — the
lesson spec 036 recorded when it found that four confirmations written in one
sitting converge on one sentence.

### Testing strategy

**Domain, per aggregate**: recovery yields a draft numbered max+1 carrying the
archived revision's payload; a draft-only chain still throws; a chain with a
Published revision still branches from the Published one and not the newest.

**Application, per aggregate**: recovery succeeds end to end; a draft-only chain
still returns the existing failure; the name-taken case returns FR-009's failure.

**Integration, per aggregate**: archive → branch → edit → publish through the API
against real Postgres. This is the one that can catch an EF owned-entity failure,
and it is required rather than optional (research §8).

**Frontend**: the gate in both directions per page, and the two replaced wording
assertions.

**The four tests SC-005 protects stay untouched.** If implementation finds itself
editing one, that is a finding to raise, not a fix to apply.

---

## Risks

**1. The fallback gets widened to "the newest revision".** It is the shorter code
and it looks equivalent. It is not: a draft-only chain would then branch, minting
a second competing draft. Mitigated by putting the condition inside the helper
rather than at the call site, and by SC-005 — the four existing refusal tests fail
loudly if it happens.

**2. FR-009's check gets hoisted out of the recovery branch.** "Check the name on
every branch" reads like the more thorough choice. It would refuse every branch of
a healthy chain, because the chain matches its own name. Mitigated by the comment
at the call site and by keeping the published-branch path's test asserting it
still succeeds.

**3. The archive confirmation is softened rather than replaced.** The old sentence
must go, and the tempting replacement — "This cannot be undone" — is false in the
other direction. Mitigated by giving the sentences verbatim in the contract, and by
asserting the new claim as specifically as spec 036 asserted the old one.

**4. The change lands in one twin only.** ADR-0104 makes this the standing risk
for anything touching this lifecycle. Mitigated by pairing every task across both
aggregates and by SC-007.
