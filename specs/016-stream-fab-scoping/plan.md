# Implementation Plan: Fab-scope stream distribution

**Branch**: `016-stream-fab-scoping` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-stream-fab-scoping/spec.md`

## Summary

Give `Stream` a fab **derived from its camera**, scope the two read endpoints to
the caller's fabs, and fill the fab for streams that predate the change at
runtime rather than in SQL.

This is the fourth fab-scoping feature and the first where **nothing is asked
of an operator**. A stream exists only because a camera was registered, and
`CameraRegisteredV1` has carried the camera's fab since spec 015. There is no
`?fabId=`, no inference, no ambiguity and no refusal — the whole ADR-0114
decision table is irrelevant here, and copying it across would be the reflex
that cost spec 015 three requirements.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (ADR-0070), EF Core +
Npgsql, Wolverine (ADR-0042), `ServiceDefaults.Authorization` — `FabClaims`
only. **Not `FabResolution`**: it resolves a fab *for a write from a caller*,
and there is no such write here.

**Storage**: PostgreSQL, `stream-distribution-db` (ADR-0009)

**Testing**: xUnit + Shouldly + Moq; integration on the Aspire fixture
(ADR-0052, ADR-0103)

**Target Platform**: Linux containers, Aspire → k3s (ADR-0024, ADR-0025)

**Project Type**: Web service (bounded context). No UI work — the management
app has no streams surface of its own.

**Performance Goals**: The read path gains one equality term on an
already-indexed column. The startup backfill is one-time and bounded by the
number of unattributed streams.

**Constraints**: **Cameras and streams are in separate databases.** No
migration here can read the cameras table. This single fact shapes FR-008 and
FR-009 and is the reason this plan differs structurally from spec 015's.

**Scale/Scope**: 250-camera target, so up to ~250 streams. One aggregate, two
read endpoints, one integration-event handler, one hosted service.

## The surface, verified before planning

Spec 015 withdrew three requirements because its plan was drafted by analogy
rather than against the code. That is not repeated here. What exists:

| | |
|---|---|
| Endpoints | `GET /streams`, `GET /streams/{cameraIdentifier}`, `POST /streams/authorize` |
| Aggregate | `Stream` — `Camera`, `Path`, `SourceUrl`, `State`, `TranscodeMode`, `LastSuccessAt`, `LastError`, `ProvisionedAt`, `ProvisionedBy` |
| Creation path | `Stream.Provision(...)`, called **only** from `ProvisionStreamCommandHandler` |
| What triggers it | `CameraRegisteredIntegrationEventHandler`, subscribing to `CameraRegisteredV1` |
| Startup work | `MediaMtxReconciler` (`IHostedService`) — reads the streams table, pushes paths to MediaMTX |
| HTTP to CameraCatalog | **None.** This context calls no other context today. |

**No operator-driven write exists.** That is why there is no fab resolution in
this plan: there is no request in which an operator could name a fab.

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. On-Prem First | ✅ | No new infrastructure. |
| II. DDD with Value Objects | ✅ | `FabIdentifier` as this context's own copy per ADR-0044 — the sixth. Grammar must match the other five; a test asserts it. |
| III. Bounded Context Isolation | ⚠️ **See below** | This feature adds the **first** HTTP call from StreamDistribution to another context. |
| IV. Latency Budget | ✅ | N/A with reason. Stream reads are operator-facing; the SFU path is unchanged. The `/authorize` callback — the only latency-sensitive route — is deliberately untouched. |
| V. Spec-Driven Development | ✅ | This plan; tasks follow. |
| VI. Aspire Is the Composition Root | ✅ | The camera-catalog client is registered by name and resolved by Aspire service discovery, as `ReverseIndexSeederHostedService` already does in SystemVariables. |
| VII. Observability | ✅ | The runtime backfill records how many streams it attributed and how many it could not — FR-008 and FR-010. |
| VIII. Safe by Default at Trust Boundaries | ✅ | FR-009 fails closed: an unattributed stream is visible to nobody. |
| IX. Forward-Compatible Strategy Interfaces | ✅ | None introduced. |

### §III — the one thing to argue about

StreamDistribution has never called another context over HTTP. This feature
makes it call CameraCatalog to learn each pre-existing stream's fab.

It does not breach ADR-0016: the call is over the published HTTP API, carries no
value objects across the boundary, and creates no project reference. It is the
same shape as `ReverseIndexSeederHostedService`, which already calls
overlay-designer from SystemVariables.

But it is new coupling in a context that had none, so it is stated rather than
slipped in. Three things bound it:

1. **One-time.** It runs only for streams whose fab is null. Once filled, the
   call never happens again.
2. **Startup only**, in the reconciler that already runs there — no request-path
   dependency, so a CameraCatalog outage cannot affect serving video.
3. **Failure is already handled.** `MediaMtxReconciler` catches and logs rather
   than blocking host start. An unreachable CameraCatalog leaves streams
   unattributed and therefore invisible, which is FR-009's intent — but this
   must be **deliberate**, asserted by a test, not inherited from an existing
   `try/catch`.

**Gate: PASS**, with §III recorded as a justified, bounded exception rather
than a clean sheet.

#### What implementation added to this, and why it needed a decision

Two facts turned up when the call was actually written, neither of which this
plan had checked:

1. **CameraCatalog has no read-by-identifier route** — only `POST /cameras`
   and `GET /cameras` (#1435). A stream's camera cannot be resolved
   individually; the catalogue is read whole and indexed.
2. **`GET /cameras` is itself fab-scoped** and needs a token. But a stream's
   fab is precisely what is unknown, so the read cannot be narrowed to the
   right fab in advance — a caller holding one fab would resolve that fab's
   streams and leave every other plant's permanently unattributed.

So the pass presents a dedicated service account that is a member of every fab
group and holds `sse.cameras.read` and nothing else. That is a real widening of
who can read the camera catalogue, so it is **ADR-0116** rather than a line in
this plan: read-only, one route, startup only, and never again once no
unattributed stream remains.

Point 3 above — that the failure path be chosen rather than inherited — is
asserted by `StreamFabAttributionFailureTests`.

## Why the obvious alternative was rejected

`20260728210420_PersistStreamSourceUrl` hit this exact cross-database wall and
solved it by **deleting the unbackfillable rows**, reasoning that a stream is
derived state rebuilt from `CameraRegisteredV1`.

**That precedent does not transfer, and the difference matters.**

Those rows were *already broken*: `StreamSourceUrl.From("")` throws, so the EF
converter faulted on every read of the table. Deleting them cost nothing
because the table was unusable either way.

Here the rows are **functional** — video is flowing; only the attribution is
unknown. And the derivation is not a recovery mechanism: `MediaMtxReconciler`
reads *from* the streams table and pushes paths outward; it does not rebuild
streams from cameras. Nothing republishes `CameraRegisteredV1`.

So deleting would stop live video from every pre-existing camera until someone
re-registered it — trading a metadata gap for an outage. Verified, not assumed.

## Project Structure

```text
src/StreamDistribution/
├── Domain/Stream/
│   ├── FabIdentifier.cs          # NEW — this context's own copy (ADR-0044)
│   └── Stream.cs                 # + Fab, required by Provision, never set alone
├── Application/
│   ├── Commands/                 # + Fab on ProvisionStreamCommand
│   ├── EventHandlers/            # reads Metadata.Fab; drops the event without one
│   └── Queries/                  # + Fabs on both reads
├── Infrastructure/
│   ├── Persistence/Migrations/   # nullable fab column; no backfill in SQL
│   └── Reconciler/               # + fab resolution for unattributed streams
└── Api/StreamEndpoints.cs        # both GETs scoped; /authorize untouched

tests/
├── StreamDistribution.Domain.Tests/
├── StreamDistribution.Application.Tests/
└── Integration.Tests/StreamDistribution/
```

**Structure Decision**: existing per-context layout. No new projects. No
`apps/` work — the management app has no streams surface.

## Phase 0: Research

Two questions are open and belong in [research.md](./research.md):

1. **Where does the fab resolution live** — inside `MediaMtxReconciler`, or a
   second `IHostedService` beside it? The reconciler already has the scope and
   the DbContext, but it exists to reconcile MediaMTX paths, and attribution is
   a different job.
2. **How wide is the FR-009 window in practice**, and does it need closing
   before first read? After deploy, every pre-existing stream is invisible
   until the backfill completes. That is correct and may still be unacceptable.

## Phase 1: Design & Contracts

- `data-model.md` — the `Stream` change and the nullable-then-tightened column.
- `contracts/streams-api.md` — **two** endpoints gaining a fab, and the third
  explicitly not.
- `quickstart.md` — including a database of unattributed streams, which is the
  only way to observe FR-008 and FR-010.
