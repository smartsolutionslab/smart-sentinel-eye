# Contracts: Properties that travel together

**Feature**: 058 | **Date**: 2026-09-02

**No contract changes. That absence is the requirement, not an omission.**

This feature is invisible outside the domain models. FR-008 binds it: every
outward-facing shape stays byte-identical, and SC-005 makes that testable — no
consumer of the HTTP API, the integration events or the archive can tell the
change happened.

## The surfaces this feature touches from the inside, and must not change

| Surface | Where | What it carries today | After |
|---|---|---|---|
| HTTP responses | `*/Api/**`, `*/Application/DTOs/**` | Flat fields — `createdAt` + `createdBy`, `payloadSizeBytes`, `schemaVersion` | **Unchanged.** The mapper reads `entity.Creation.At` instead of `entity.CreatedAt` and writes the same field |
| Integration events | `Shared.Contracts/**` | Primitives by design (§II exemption) | **Untouched.** No message shape, name or version changes |
| Archived audit rows | `MinioAuditChunkArchiver` | A flat projection per row | **Unchanged.** The projection reads the composites and emits the same columns |
| Database columns | every context | See [data-model.md](../data-model.md) | **Unchanged**, including nullability and indexes |

## Why the DTOs stay flat

Grouping the *domain* model does not imply grouping the wire model, and here it
must not. A `createdAt`/`createdBy` pair in a JSON response is a serialization
contract with existing consumers, including both React apps. Nesting it would
be a breaking API change smuggled in behind a refactor.

The composites therefore stop at the domain boundary: mappers unwrap them on
the way out, exactly as they unwrap value objects today (`.Value`).

## How the absence is verified

- No file under `src/Shared.Contracts/` is modified by this feature.
- DTO record definitions are unchanged; only the mapper expressions that fill
  them move.
- The e2e suite exercises the HTTP surface and is expected to pass untouched —
  a change there would mean FR-008 was breached.
