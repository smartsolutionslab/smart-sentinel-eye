# Implementation Plan: Retire a camera

**Branch**: `028-retire-camera` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-retire-camera/spec.md`

## Summary

Give the Camera aggregate the terminal state it already has a value for, and let
the rest of the system react. A retire behaviour raises a domain event, a
command/handler/endpoint exposes it with spec 015's fab resolution, and
`CameraRetiredV1` announces it. StreamDistribution consumes that announcement,
removes the SFU path, and moves the stream to a terminal state the health
watcher ignores.

Name reuse needs **no schema change** — verified in Phase 0, not assumed.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core + Npgsql, Wolverine
(RabbitMQ + Postgres outbox), MediaMTX via `IRtspGateway`

**Storage**: PostgreSQL. **No migration expected** — see research §1

**Testing**: xUnit + Shouldly + hand-written fakes; integration against the
Aspire fixture (ADR-0103)

**Target Platform**: Linux containers, k3s in production, Aspire AppHost in dev

**Project Type**: Multi-context backend service

**Performance Goals**: Not on the event-to-overlay path. Retirement is an
operator action measured in a request, not a frame budget

**Constraints**: Two bounded contexts, joined by an integration event and never
by a shared transaction (constitution §III)

**Scale/Scope**: 250 cameras per fab; retirement is rare per camera and
permanent

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **II. DDD with value objects** | PASS. The transition is a behaviour on the aggregate, not a setter. `CameraStatus` already exists as a value object; no primitive crosses a domain boundary. |
| **III. Bounded context isolation** | PASS, and it is the crux. CameraCatalog and StreamDistribution communicate **only** via `CameraRetiredV1` in `Shared.Contracts`. No project reference is added between them. NetArchTest already enforces this. |
| **IV. Latency budget** | NOT APPLICABLE, stated rather than skipped. Retirement is not on the event-to-overlay path. It *removes* load: a retired camera stops being probed. |
| **VII. Observability** | PASS. The retirement is audited (FR-010) and the StreamDistribution handler is message-driven, so it inherits its cause from the message being handled — no new journey origin needed (spec 027's survey). |
| **VIII. Safe by default at trust boundaries** | PASS. FR-004 requires not-found rather than forbidden for another fab's camera, so the refusal leaks nothing. |
| **IX. Forward-compatible interfaces** | PASS. `IRtspGateway.RemovePathAsync` already exists; no new abstraction is introduced. |

**No violations.** One deliberate note: this feature spans two contexts, which
is unusual for a single spec here. It is justified because FR-008's decision
makes the stream teardown part of the same user-visible outcome — a retired
camera that keeps streaming is not retired in any sense an operator recognises.

### Post-design re-check

Unchanged. The design adds one contract, one domain event, one integration
event handler, and two aggregate behaviours. Nothing crosses a context boundary
except the contract.

## Project Structure

### Documentation (this feature)

```text
specs/028-retire-camera/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── cameras-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
  Shared.Contracts/
    CameraCatalog/
      CameraRetiredV1.cs                      NEW — the announcement

  CameraCatalog/
    Domain/Camera/
      Camera.cs                               Retire behaviour, terminal guard
      Events/CameraRetiredDomainEvent.cs      NEW
    Application/
      Commands/RetireCameraCommand.cs         NEW
      Commands/RetireCameraErrors.cs          NEW
      Commands/Handlers/
        RetireCameraCommandHandler.cs         NEW
      EventHandlers/
        CameraRetiredDomainEventHandler.cs    NEW — publishes the V1
      Queries/                                listing excludes retired by default
    Infrastructure/Persistence/
      CameraRepository.cs                     find-by-identifier within fab
    Api/
      CameraEndpoints.cs                      POST /{camera}/retire

  StreamDistribution/
    Domain/Stream/
      Stream.cs                               Retire behaviour
      StreamState.cs                          terminal state
    Application/
      Commands/RetireStreamCommand.cs         NEW
      Commands/Handlers/
        RetireStreamCommandHandler.cs         NEW — removes the SFU path
      EventHandlers/
        CameraRetiredIntegrationEventHandler.cs  NEW
    Infrastructure/HealthWatcher/
      StreamHealthWatcher.cs                  exclude retired from the sweep

tests/
  CameraCatalog.Domain.Tests/                 transition + terminality
  CameraCatalog.Application.Tests/            handler, idempotency, fab refusal
  StreamDistribution.Domain.Tests/            stream retirement
  StreamDistribution.Application.Tests/       handler, path removal
  StreamDistribution.Infrastructure.Tests/    watcher skips retired
  Integration.Tests/                          name reuse, cross-fab, end to end
```

## Implementation strategy

**MVP is User Story 1 alone** — a camera can reach the retired state and the
retirement is announced. That is independently shippable: the catalogue starts
telling the truth about which hardware exists even if nothing consumes the
announcement yet.

**US2 (name reuse) is assertion work**, not build work. Phase 0 established the
index already supports it, so this is an integration test that would have been
written anyway.

**US3 (listing) and the StreamDistribution side are separable.** If the FR-008
decision is overturned, everything under `src/StreamDistribution/` above drops
out and the rest of the plan stands unchanged.

## Sequencing

```
Contract + domain (Shared.Contracts, Camera.Retire, domain event)
      ↓
Application + API (command, handler, endpoint, publish the V1)   ← US1 ships here
      ↓
Name reuse assertion (integration)                               ← US2
      ↓
Listing exclusion                                                ← US3
      ↓
StreamDistribution: state, retire, handler, path removal
      ↓
Health watcher exclusion                                         ← the noise fix
```

The watcher exclusion is **last and non-optional**. Retiring a camera whose
stream is still swept produces health announcements for hardware that does not
exist — and since #1801 those are no longer silently dropped.

## Three things most likely to go wrong

**Retirement made non-terminal by accident.** `ReportHealthy` and friends must
refuse to move a retired stream. A health probe arriving mid-retirement is the
realistic path back out of the terminal state, and nothing in the current
aggregate would stop it.

**The endpoint answering 403 instead of 404.** FR-004 is a security property,
not a nicety: a distinguishable refusal lets one fab enumerate another's camera
names. Spec 015 already established the pattern; deviating is easy and quiet.

**Idempotency implemented as "no error" rather than "no event".** FR-005 says a
second retire must not raise a second domain event. A handler that returns
success while re-raising would double-announce, and every downstream consumer
would see two retirements — the audit trail would show a camera retired twice.
