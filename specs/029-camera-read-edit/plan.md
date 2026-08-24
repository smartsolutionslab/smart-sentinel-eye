# Implementation Plan: Read a single camera, and correct one

**Branch**: `029-camera-read-edit` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/029-camera-read-edit/spec.md`

## Summary

Two operations on one camera, keyed by identifier: read it, and correct its
address. The read is what makes the edit possible — an edit must quote a
version (FR-004) and **no camera endpoint exposes one today**.

Phase 0 came back against the spec twice, and both are for the Phase 2 gate
rather than this plan to settle:

1. **The payoff US1 claims is not available.** There is no client-side
   over-fetch to remove, because the management app has no single-camera view
   at all. SC-001/SC-002 misdescribe the win. The endpoint stays justified —
   by US2 and by FR-006 — but for different reasons than the spec gives.
2. **Correcting an address silently desynchronises the SFU.** `CameraRegisteredV1`
   hands the URL to StreamDistribution, which provisions MediaMTX to pull from
   it; `Stream.SourceUrl` is assigned only in `Provision` and no behaviour
   changes it. An edited address leaves the SFU streaming from the old one
   indefinitely — a failure that looks like success. This is spec 028 FR-008's
   shape and the spec has no requirement for it.

Both are set out in [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (ADR-0070), EF Core, Wolverine (dispatch + RabbitMQ, ADR-0042/0088), .NET Aspire (composition root)

**Storage**: PostgreSQL. **No migration** — `Version` is already mapped as a concurrency token and no new state is introduced.

**Testing**: xUnit + Shouldly + hand-written fakes (ADR-0052/0054); integration against the real Aspire stack via `AspireFixture` (ADR-0103, no Testcontainers); `NetArchTest` for boundaries.

**Target Platform**: Linux containers; k3s in production, Aspire AppHost in dev.

**Project Type**: Web service (bounded context inside a modular monolith), plus a possible cross-context consumer.

**Performance Goals**: Not on the event-to-overlay path. A single-camera read is a primary-key lookup within one fab.

**Constraints**: Two-layer optimistic concurrency, no retry-on-conflict (ADR-0043, ADR-0113). Refusals for another fab must be byte-identical to refusals for a non-existent camera (FR-006).

**Scale/Scope**: 250 cameras per fab (constitution §Scale). Two endpoints, one new aggregate behaviour, and — if finding 2 is adopted — one integration event with one cross-context consumer.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **II. DDD with value objects** | PASS. `CameraIdentifier`, `RtspUrl` already exist; no primitive crosses a domain boundary. The address change is a behaviour on `Camera`, not a setter. |
| **III. Bounded context isolation** | PASS **only if** finding 2 goes through `Shared.Contracts`. CameraCatalog must not reference StreamDistribution; the address change is announced as a versioned `*V1`, exactly as retirement is. NetArchTest enforces it. |
| **IV. Latency budget** | **N/A** — not on the event-to-overlay path. No leg affected, nothing to cite. |
| **V. Spec-driven development** | PASS. Spec → plan → tasks, gated. This plan advances no gate on its own. |
| **VII. Observability** | PASS. FR-011 audits the change; `Architecture.Tests` enforces that every `*V1` has an audit handler. §VII's dashboard rule binds implemented latency legs only (ADR-0117) and this touches none. |
| **VIII. Safe by default at trust boundaries** | PASS, and this is the principle the feature most engages. Fab resolution precedes every other precondition (FR-007); refusals are indistinguishable (FR-006); `If-Match` absence is 428 rather than a silent fallback to no concurrency control. |
| **IX. Forward-compatible interfaces** | N/A. No new strategy seam; no speculative generality. |

**No violations.** Complexity Tracking omitted — nothing to justify.

**Post-design re-check**: unchanged. The design adds no project, no cross-context reference, and no new abstraction; the only new seam is an integration event, which is the prescribed mechanism rather than an exception to it.

## Project Structure

### Documentation (this feature)

```text
specs/029-camera-read-edit/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── cameras-api.md   # Phase 1 output
├── checklists/
│   └── requirements.md  # Phase 1 (specify) output
└── tasks.md             # Phase 2 output — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/
├── CameraCatalog/
│   ├── Domain/Camera/
│   │   ├── Camera.cs                     # + ChangeAddress behaviour, terminal guard
│   │   └── Events/
│   │       └── CameraAddressChangedDomainEvent.cs      # new
│   ├── Application/
│   │   ├── Commands/
│   │   │   ├── ChangeCameraAddressCommand.cs           # new
│   │   │   ├── ChangeCameraAddressErrors.cs            # new
│   │   │   └── Handlers/ChangeCameraAddressCommandHandler.cs
│   │   ├── Queries/
│   │   │   ├── GetCameraQuery.cs                       # new
│   │   │   ├── GetCameraErrors.cs                      # new
│   │   │   └── Handlers/GetCameraQueryHandler.cs
│   │   ├── DTOs/
│   │   │   ├── CameraDto.cs                            # new — read-one shape, carries Version
│   │   │   └── CameraSummaryDto.cs                     # + Version
│   │   └── EventHandlers/
│   │       └── CameraAddressChangedDomainEventHandler.cs
│   ├── Infrastructure/Persistence/
│   │   └── CameraRepository.cs           # reuses GetWithinFabAsync (spec 028); no new method expected
│   └── Api/CameraEndpoints.cs            # + GET /cameras/{camera}, + PATCH/PUT address
├── Shared.Contracts/CameraCatalog/
│   └── CameraAddressChangedV1.cs         # new — if finding 2 is adopted
└── StreamDistribution/                   # consumer half — if finding 2 is adopted
    ├── Domain/Stream/Stream.cs           # + RepointTo(StreamSourceUrl)
    └── Application/
        ├── Commands/RepointStreamCommand.cs + handler
        └── EventHandlers/CameraAddressChangedIntegrationEventHandler.cs

tests/
├── CameraCatalog.Domain.Tests/Camera/CameraAddressChangeTests.cs
├── CameraCatalog.Application.Tests/
│   ├── Commands/ChangeCameraAddressCommandHandlerTests.cs
│   └── Queries/GetCameraQueryHandlerTests.cs
├── StreamDistribution.Domain.Tests/Stream/StreamRepointTests.cs
├── StreamDistribution.Application.Tests/Commands/RepointStreamCommandHandlerTests.cs
└── Integration.Tests/
    ├── CameraCatalog/GetCameraIntegrationTests.cs
    └── StreamDistribution/RepointStreamIntegrationTests.cs
```

**Structure Decision**: The existing per-aggregate Domain layout (ADR-0092) and
per-message-kind Application layout (ADR-0093) already in CameraCatalog and
StreamDistribution. No new project, no new folder convention. The cross-context
half is a separate subtree so it can be dropped as a unit if finding 2 is
decided the other way.

## Implementation phasing

Ordered so each phase is independently shippable and the contested part is last.

| Phase | Content | Depends on | Droppable? |
|---|---|---|---|
| **1** | Read one camera: `CameraDto` with `Version`, `GetCameraQuery` + handler, endpoint with `ETag`, `Version` added to `CameraSummaryDto` | — | No — US2 needs it |
| **2** | Non-enumeration: another fab's camera refused identically to a non-existent one, asserted field by field on **both** endpoints | 1 | No — FR-006 is a security property |
| **3** | Correct the address: `Camera.ChangeAddress` with the terminal guard, command + handler, endpoint with `If-Match` | 1 | No |
| **4** | **The stream follows the address** (FR-013/FR-013a/FR-014): `CameraAddressChangedV1`, `Stream.RepointTo`, the SFU path re-pointed | 3 | **No — adopted at the Phase 2 gate** |
| **5** | Audit + polish: confirm the change is audited naming the operator; full suite; verification note | 3, 4 | No |

**Phase 4 was the contested one and is now settled.** Research finding 2 was
adopted at the Phase 2 gate and is written into the spec as FR-013, FR-013a and
FR-014, so it is a requirement rather than a recommendation. Phases 3 and 4 now
**ship together or not at all**: Phase 3 alone corrects the catalogue while
leaving the SFU pulling the old address, which is a worse state than not having
shipped the edit, because it reports success for a system that is now
inconsistent with itself.

The marginal cost is smaller than the phase count suggests. FR-011's audit trail
needed an integration event regardless, so what Phase 4 adds is the
StreamDistribution consumer and the `RepointTo` behaviour — not the
announcement, which Phase 3 had to raise anyway.

## Key design decisions

**Read-one returns retired cameras (FR-002); the listing still hides them.**
Different questions. "Show me what is out there" excludes hardware that is
gone; "tell me about this camera" is asked *because* the caller already has its
identifier, and answering "not found" for a record that exists would be a lie
that also breaks the audit trail's readability.

**The terminal guard lives on the aggregate, not the handler.** `Camera.Retire`
already refuses re-retirement in the aggregate; `ChangeAddress` must refuse a
retired camera the same way. In the handler it would be bypassable by a second
caller — and spec 028's finding was precisely a rule enforced in one layer and
not another.

**Fab resolution precedes `If-Match` validation.** The tempting order is to
reject a missing precondition header before doing any work; it must not be,
because a 428 for another fab's camera confirms the camera exists. `Fab first,
always` — FR-007, and the sharpest place it bites.

**`Version` goes on both DTOs.** Following `RuleDto`'s stated reason — the list
hands every row a version without a per-row fetch — which lets an operator edit
straight from the listing. This is the one place the feature genuinely reduces
traffic, as against the saving SC-001 claims.

## Three things most likely to go wrong

**The two refusals diverge.** FR-006 needs another fab's camera and a
non-existent one to be byte-identical. They will take different code paths the
moment anyone adds a log line, a header, or a more helpful `detail`. SC-003's
field-by-field comparison is the only thing that catches it, and it must cover
the edit endpoint too — not just the read.

**The address changes and the stream does not.** Now FR-013, so it is a test
rather than a risk — but the test has to look at **the SFU**, not at the
catalogue that requested the change. Asserting the announcement was published,
or that the stream row's stored source changed, both pass while MediaMTX
happily keeps pulling the old address. The assertion is on the path's actual
configured source.

**`If-Match` handling leaks existence.** A 428 answered before fab resolution
tells an operator in Dresden that a Munich camera exists. The test for this is
not "does a missing header return 428" but "does a missing header on *another
fab's* camera return 404".

## Complexity Tracking

No constitution violations. Section intentionally empty.
