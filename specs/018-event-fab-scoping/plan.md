# Implementation Plan: Fab-scope event ingestion

**Branch**: `018-event-fab-scoping` | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-event-fab-scoping/spec.md`

## Summary

Resolve the fab from the caller instead of taking it off the query string, on
two reads and one write; give a rejected delivery the fab its topic
establishes, and none where it does not; scope the rejected-delivery list to
the caller's fabs.

This is the sixth and last fab-scoping feature. It is also the smallest in
code and the largest in consequence: the context already models a fab and
already filters on it, so **almost every line of this change sits at the
endpoint boundary** — which is exactly where the missing check belongs.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (ADR-0070), EF Core +
Npgsql, `ServiceDefaults.Authorization` — **both** `FabResolution` halves.
Reads take `ResolveForReadAsync`; the manual write takes `ResolveForWriteAsync`
with the full ADR-0114 table.

**Storage**: PostgreSQL, `event-ingestion-db` (ADR-0009). One table changes:
`dead_letters` gains a nullable `fab`.

**Testing**: xUnit + Shouldly + Moq; integration on the Aspire fixture
(ADR-0052, ADR-0103).

**Target Platform**: Linux containers, Aspire → k3s (ADR-0024, ADR-0025)

**Project Type**: Web service (bounded context). No UI work. **No new
cross-context dependency** — unlike specs 016 and 017, everything this feature
needs is already in this context.

**Performance Goals**: Reads gain one `IN` term on an already-indexed column.
The ingest path is untouched.

**Constraints**: The broker and webhook ingress paths must not change (FR-014,
FR-015). They are the throughput-critical routes and they already establish
the fab correctly.

**Scale/Scope**: Three endpoints changed, one endpoint's query scoped, one
nullable column with an in-SQL backfill. No new aggregate, no new value object
— `FabIdentifier` already exists here.

## The surface, verified before planning

This is the practice that found the leak. Repeated here so the plan is
checkable against the code rather than against the spec:

| | |
|---|---|
| Endpoints | `POST /events/manual`, `POST /events/webhook/{name}`, `GET /events`, `GET /events/{id}`, `GET /events/dead-letters`, plus 3 on `/webhook-integrations` |
| Fab on `Event` | **Yes** — `FabIdentifier`, already filtered on by both read handlers |
| Fab on `DeadLetter` | **No** — `Topic`, `RawPayload`, `Error`, `RejectedAt` only |
| Fab on `WebhookIntegration` | **No** — out of scope (FR-016) |
| `?fabId=` today | **Required** on both reads and the manual write; **unchecked** on all three |
| Webhook ingress | Validates `"/fabs/" + fabId` against the caller's own groups — **already correct** |
| Guard usage | `grep -rn "IFabAuthorizationGuard\|FabClaims" src/EventIngestion/` → **nothing** |
| Dead-letter topic | `fab/{fabId}/{source}/{deviceId}`, and the parse fails two distinct ways |

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. On-Prem First | ✅ | No new infrastructure, no new credential. |
| II. DDD with Value Objects | ✅ | `FabIdentifier` already exists in this context (ADR-0044). Nothing new to add or keep in step. |
| III. Bounded Context Isolation | ✅ | **No cross-context call at all.** Unlike specs 016 (ADR-0116) and 017 (§III), everything needed is local. This is the first fab feature since 015 that adds no coupling. |
| IV. Latency Budget | ✅ | N/A with reason. The ingest paths — broker and webhook — are untouched (FR-014, FR-015). The operator reads are control plane. |
| V. Spec-Driven Development | ✅ | This plan; tasks follow. |
| VI. Aspire Is the Composition Root | ✅ | No new resources or references. |
| VII. Observability | ✅ | FR-012 surfaces the orphan count through logging rather than a new endpoint ([research.md](./research.md) §2). |
| VIII. Safe by Default at Trust Boundaries | ✅ | **The entire feature.** The trust boundary was reading a fab from the request; it now reads it from the caller's claims. FR-010 fails closed on an unestablishable origin. |
| IX. Forward-Compatible Strategy Interfaces | ✅ | None introduced. |

**Gate: PASS**, with no exception to record — the first of the six fab
features that needs none.

## Why this one is small in code and large in consequence

Worth stating so the review is calibrated. The diff will look modest:

- Two read handlers change `== query.Fab` to `fabs.Contains(...)`.
- Three endpoints gain the resolution specs 013–017 already established.
- One nullable column, one backfill, one scoped query.

**The consequence is not modest.** Today any operator holding `sse.events.read`
can read any plant's ingested production data by naming the plant, and any
operator holding `sse.events.write` can inject events into another plant —
which then drive that plant's automation rules (spec 007) and change what its
screens display. That is the only path in the product by which one fab can
alter another's state, and it is a few lines of endpoint code.

**A small diff is the expected shape here**, not a sign the feature is
underscoped.

## Project Structure

### Documentation (this feature)

```text
specs/018-event-fab-scoping/
├── plan.md              # This file
├── spec.md
├── research.md          # Phase 0 — the dead-letter fab, and four findings
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1
├── checklists/
└── tasks.md             # /speckit-tasks — NOT created here
```

### Source Code (repository root)

```text
src/EventIngestion/
├── Domain/DeadLetter/
│   └── DeadLetter.cs             # + nullable Fab, set at Capture from the parsed topic
├── Application/
│   └── Queries/                  # Fabs (plural) on all three queries + handlers
├── Infrastructure/
│   ├── Persistence/Migrations/   # nullable fab column + in-SQL backfill from topic
│   └── Ingress/                  # MqttSubscriberHostedService passes the parsed fab to Capture
└── Api/
    ├── EventsEndpoints.Reads.cs  # ResolveForReadAsync on both reads + dead-letters
    └── EventsEndpoints.Writes.cs # ResolveForWriteAsync on /manual; webhook UNTOUCHED

tests/
├── EventIngestion.Domain.Tests/
├── EventIngestion.Application.Tests/
└── Integration.Tests/EventIngestion/
```

**Structure Decision**: existing per-context layout. No new projects, no new
folders, no new value objects. The one structural risk is touching
`EventsEndpoints.Writes.cs`, which holds both the manual write **and** the
webhook — and only one of them may change.

## Phase 0: Research

Complete — [research.md](./research.md). One decision and four findings:

- **§1** settles the dead-letter fab as a stored nullable column with an
  in-SQL backfill, and records that the topic parse fails **two** ways, only
  one of which yields NULL.
- **§2** puts FR-012's orphan count in the log rather than a new endpoint.
- **§3** notes this makes a **required** parameter optional — widening for
  legitimate callers, narrowing for illegitimate ones.
- **§4** is why the webhook is exempt, and why the manual write looked like it.
- **§5** is why the read handlers need almost nothing.

## Phase 1: Design & Contracts

- `data-model.md` — the `DeadLetter` change, the column and backfill, and the
  two parse failure modes written out.
- `contracts/events-api.md` — five endpoints: three that change, one exempt
  with its reason, and the registry explicitly untouched.
- `quickstart.md` — including a malformed-topic delivery, which is the only
  way to observe FR-010 and FR-011.

## Constitution Check — re-evaluated after Phase 1

Design added three things worth re-testing, and **no gate outcome changed**:

- **A nullable `fab` on `dead_letters`** (§VIII). Permanently nullable, unlike
  spec 016's transitional column — a delivery with a malformed address has no
  plant, and `NULL` excluded by `IN` is what makes FR-011 fail closed without
  a special case.
- **An in-SQL backfill guarded by the `FabIdentifier` grammar** (§II). The
  regex stops the migration writing a value the domain would reject on read —
  the defect spec 015 hit. No `RAISE WARNING`, because nothing is guessed.
- **FR-012 as a log record** (§VII) rather than an endpoint, so the orphan
  count is observable without the payload being readable.

**Gate: PASS**, still with no exception to record.

## Complexity Tracking

> No constitutional violations to justify. The table is left empty
> deliberately: specs 016 and 017 each needed a §III exception, and this one
> does not, which is worth seeing at a glance.
