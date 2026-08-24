# Implementation Plan: One way to say a version is stale

**Branch**: `031-stale-version-convention` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/031-stale-version-convention/spec.md`

## Summary

One error code is renamed, one shared client predicate is simplified, one
architecture test is added so the convention cannot be missed again, and one ADR
records why.

Phase 0 found this is **smaller than it sounds** — five sites — and that the
convention can be **enforced** rather than only documented, because
`HandlerDeconstructionTests` already reads source files and so a source-scanning
check is an existing shape here.

It also found a second instance of the problem this feature exists to fix:
**spec 029's contract documents an error code that does not exist**
(`PRECONDITION_FAILED`, where the implementation answers
`CAMERA_VERSION_MISMATCH`). Corrected here.

## Technical Context

**Language/Version**: C# / .NET 10, and TypeScript for the shared client

**Primary Dependencies**: none added. `ApiError` (ADR-0089), `problemDetail.ts`, `NetArchTest` + the existing source-scanning test pattern

**Storage**: N/A — no persisted state, no migration

**Testing**: xUnit + Shouldly for the backend; Vitest for the shared client; a source-scanning architecture test for the convention

**Target Platform**: unchanged

**Project Type**: cross-cutting correction — one bounded context, one shared frontend module, one architecture test, one ADR

**Performance Goals**: N/A

**Constraints**: **FR-006** — the six contexts that behave correctly today must not change at all. Their tests must pass **without modification**, which is the only form of that assurance that cannot be edited into agreement.

**Scale/Scope**: 1 code renamed, 16 left alone, 1 predicate simplified, 1 test added, 1 ADR, 1 contract corrected.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **II. DDD with value objects** | PASS. An error code is a wire concern; no domain type changes. |
| **III. Bounded context isolation** | PASS. One context's error vocabulary changes; nothing crosses a boundary that did not already. |
| **IV. Latency budget** | **N/A** — refusal vocabulary is not on the event-to-overlay path. |
| **V. Spec-driven development** | PASS, and this feature exists because a decision was deferred rather than made. FR-007 closes that. |
| **VII. Observability** | PASS. Audit rows carry the event kind, not the refusal code; nothing recorded changes. |
| **VIII. Safe by default at trust boundaries** | PASS, and materially improved: the current state gives one context's operators advice that destroys their work. |
| **IX. Forward-compatible interfaces** | N/A — no strategy seam. |

**No violations.** Complexity Tracking omitted.

**Post-design re-check**: unchanged. The design removes a branch rather than adding one, and the new architecture test is a guard rather than an abstraction.

## Project Structure

### Documentation (this feature)

```text
specs/031-stale-version-convention/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output — the refusal vocabulary
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── refusal-vocabulary.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
docs/adr/
└── 0119-stale-version-vocabulary.md        # new — amends ADR-0113

src/CameraCatalog/Application/Commands/
├── ChangeCameraAddressErrors.cs            # CAMERA_VERSION_MISMATCH → CAMERA_VERSION_STALE
└── Handlers/ChangeCameraAddressCommandHandler.cs

apps/shared/src/api/
├── problemDetail.ts                        # the 412 branch is deleted, not extended
└── problemDetail.test.ts

tests/
├── Architecture.Tests/StaleCodeConventionTests.cs   # new — the convention, enforced
└── CameraCatalog.Application.Tests/Commands/ChangeCameraAddressCommandHandlerTests.cs

specs/029-camera-read-edit/contracts/cameras-api.md  # documents a code that does not exist
```

**Structure Decision**: no new projects or folders. The ADR number is the next
free one; the architecture test sits beside the existing source-scanning test it
is modelled on.

## Implementation phasing

| Phase | Content | Depends on |
|---|---|---|
| **1** | The ADR. The decision, recorded before the code that implements it | — |
| **2** | Rename the code, and correct spec 029's contract | 1 |
| **3** | The architecture test, so a future context cannot miss it | 2 |
| **4** | Simplify the client predicate — **delete** the provisional branch, drop the provisional note | 2, **#1859 merged** |
| **5** | Verify FR-006: the six contexts' tests pass **unmodified** | all |

**Phase 1 first, deliberately.** The whole complaint is that a decision was
deferred and became a comment. Writing the ADR before the code makes the change
an implementation of a decision rather than a decision inferred from a diff.

**Phase 4 is the only part that needs #1859.** Phases 1–3 are backend and can
land independently; after the rename the frontend's provisional branch is
harmless dead code, because it matches on the code as well as the status.

## Key design decisions

**The rename is `CAMERA_VERSION_MISMATCH` → `CAMERA_VERSION_STALE`, and the
status stays 412.** The status is the more correct one and is being made
irrelevant to the advice rather than standardised, so both spellings stay legal.
A reviewer who wants the status standardised is overturning the spec's central
assumption, not this plan.

**The client predicate is simplified, not extended.** After the rename,
`isStaleConflict` is `problemCode(error)?.endsWith('_STALE')` — the doctrine the
helper's own comment already states. The status test goes away entirely, which
is what makes it correct for a 412 and a 409 alike without knowing about either.

**The architecture test scans source, because reflection cannot.** `ApiError`
takes its code as a constructor argument, so the value exists only on an
instance. The literal in the source is the only place to read it without running
anything, and `HandlerDeconstructionTests` already establishes that shape.

**`isTerminalRefusal` keeps its own code list, and that is a known limit.** It
recognises `CAMERA_RETIRED` by name. A general convention for terminal-state
refusals is a bigger question than this feature — there is one such code today —
and inventing a `*_TERMINAL` suffix for a population of one would be exactly the
speculative generality this project avoids. Recorded rather than solved.

## Three things most likely to go wrong

**The six contexts change and nobody notices.** FR-006 is invisible: every test
in this feature can pass while a layouts operator starts seeing different words.
The guard is that their existing tests must pass **unmodified** — if a task
requires editing one, the change was not additive and that is the finding.

**The architecture test is written so loosely it never fires.** A check that
only looks for the exact string `CAMERA_VERSION_MISMATCH` passes forever and
catches nothing. It has to fail for a code a *future* context would plausibly
invent, and the test for the test is to add such a code temporarily and watch it
go red.

**The provisional note outlives the provisional code.** FR-008 exists because a
deferred decision left a comment in shared code. Deleting the branch while
leaving the note saying "pending #1857" would be the same failure in miniature.

## Complexity Tracking

No constitution violations. Section intentionally empty.
