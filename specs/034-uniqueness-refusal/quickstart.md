# Quickstart: Losing the uniqueness race is a refusal, not a fault

**Feature**: `034-uniqueness-refusal` · 2026-08-25

How to see this working, and how to prove the three things most likely to be
wrong. Two of the three are about what the handler must **not** catch.

```sh
dotnet run --project src/AppHost
```

---

## 1. The refusal, seen directly

The application check normally answers first, so provoking this needs the
database to be the one that refuses. The most direct way is to remove the check
from the path — not by editing it, but by racing it (§2), or by asking the
handler directly (its unit test).

What must arrive:

> **409**, `"title": "RESOURCE_ALREADY_EXISTS"`, and a detail that says to
> choose a different name and that retrying unchanged will be refused again.

> **Nothing** in the body naming a constraint, index, table, column, or the
> colliding values. Search the whole response for `ux_` and for the name you
> submitted — the second is the one people forget.

---

## 2. Racing it, which is the honest test

```sh
# Fire N identical creates at once, same name, same fab.
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST \
    "http://localhost:<camera-catalog>/cameras" \
    -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
    -d '{"name":"race-me","rtspUrl":"rtsp://10.0.7.9/h264"}' &
done; wait
```

> Expect exactly **one `201`**. Every other response is a **409**.
> **No `500`, ever** — that is the assertion (SC-002).

Most will be `409 CAMERA_NAME_TAKEN` from the application check, which is
correct and is the common path. Some runs will produce
`409 RESOURCE_ALREADY_EXISTS` instead, from a writer whose check passed before
another's write landed.

**A run where the second code never appears is not a failure.** The race may
simply not have occurred. What must never appear is a `500`. This is the
limitation the spec accepts deliberately: the test can fail to add information,
but it cannot go green while the bug is present.

---

## 3. The two things it must NOT catch

**This is the check most likely to be skipped, because the feature works without
it.**

### A lost update must still be a lost update

Correct a camera twice, quoting the same version:

```sh
curl -si -X PATCH ".../cameras/<guid>" -H 'If-Match: "1"' \
  -d '{"rtspUrl":"rtsp://10.0.5.99/h264"}' ...
```

> Expect **412 `CAMERA_VERSION_STALE`**, unchanged.
>
> If it has become `409 RESOURCE_ALREADY_EXISTS`, the handler is matching
> `DbUpdateException` rather than the SQLSTATE — and it is registered before the
> concurrency handler. `DbUpdateConcurrencyException` **derives from**
> `DbUpdateException`, so this is one line of carelessness away at all times.

### A missing table must still be a fault

```sh
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~DirectWriteHonesty"
```

> Must stay green. It drops `events_<fab>` and requires **`>= 500`**.
>
> Under a type-based match this becomes *"choose a different name"* for a table
> that does not exist — an operator told to rename their way out of missing
> storage.

---

## 4. Nothing else moved

```sh
dotnet test tests/CameraCatalog.Application.Tests
dotnet test tests/Automation.Application.Tests
dotnet test tests/SystemVariables.Application.Tests
dotnet test tests/LayoutComposition.Application.Tests
dotnet test tests/OverlayDesigner.Application.Tests
```

> All green, **with no edits to any of them** (SC-005).

Those suites cover the seven `*_NAME_TAKEN` refusals that answer on the common
path. If any needed changing, the application-level check it guards has been
weakened — which FR-009 forbids and which is exactly how spec 028's defect
happened.

---

## Automated equivalents

| Check | Where |
|---|---|
| 1 | `UniqueConstraintExceptionHandlerTests` — the mapping, deterministically |
| 2 | `UniquenessRaceIntegrationTests` — one success, never a fault |
| 3a | `UniqueConstraintExceptionHandlerTests` — declines a `DbUpdateConcurrencyException` |
| 3b | `DirectWriteHonestyIntegrationTests`, `OutboxSharesTheWritesFateTests` — unchanged |
| 4 | The five contexts' existing suites, unchanged |
