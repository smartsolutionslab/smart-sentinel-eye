# Spec 240 — A grid nobody sent

**Issue**: #2480 (feature-level; no per-task issues; already on Project #13) · **Branch**: `fix/2480-layouts-null-grid-400`
**Status**: Phase 1 — ready for review · **Lane**: autonomous (ADR-0144)
**ADRs**: ADR-0036 (smallest change; validate at trust boundaries only), ADR-0139 (new behaviour
starts red), ADR-0105 (`Ensure.That` guards and the `ArgumentException` family they throw),
ADR-0038/0046/0066 (value objects own their validation), ADR-0047/0089 (the `400` problem shape the
sibling cases already use), ADR-0070 (Minimal APIs), ADR-0103 (integration tests against the Aspire
fixture), ADR-0052/0053/0054 (xUnit + Shouldly, sentence-style names, hand-written builders),
ADR-0144 (lane). Direct precedent: spec 173 (#2203) — the same "a missing body member escapes a
`catch (ArgumentException)` as a 500" defect in EventIngestion, fixed locally at the endpoint.
Sibling: #2309 (closed) — the domain-side `Ensure` guard in `Layout.CreateDraft`/`EditDraft`.
**Latency budget**: N/A — layout authoring writes (`POST /layouts`, `PATCH …/revisions/{n}`); not on
the event→overlay path (constitution §IV).
**No new ADR.** The narrow fix decides nothing new: the endpoints already answer
`400 LAYOUT_INVALID_INPUT` for every other malformed field; this makes two more fields reach that
answer. The repo-wide alternative (`RespectNullableAnnotations`) *would* need one — see §2.

---

## 1. Premise, reproduced 2026-09-24 at `develop` `a54b11d0`

Reproduced in-process, not read from code: a scratch minimal-API host invoked the **real**
`LayoutEndpoints.CreateDraft` / `EditDraft` (via reflection) behind `UseExceptionHandler()` with the
same five `IExceptionHandler`s `ServiceDefaults` registers (`AuthenticationDefaults.cs:89-99`),
and the default Minimal-API JSON options (no `ConfigureHttpJsonOptions` exists anywhere in `src/`;
`RespectNullableAnnotations` is configured nowhere). Observed:

| request body | status | where it throws |
|---|---|---|
| `POST /layouts` `{"name":"Wall A","tiles":[]}` (grid **omitted**) | **500** | `LayoutEndpoints.Commands.cs:143` NRE |
| `POST /layouts` `{"name":"Wall A","grid":null,"tiles":[]}` | **500** | `:143` NRE |
| `PATCH /layouts/{id}/revisions/1` `{"tiles":[]}` (grid omitted) | **500** | `:354` NRE |
| `POST /layouts` `…,"tiles":[null]` | **500** | `:443` (`ParseTiles` lambda) NRE |
| `PATCH …/revisions/1` `…,"tiles":[null]` | **500** | `:443` NRE |
| control: `grid.rows = 0` | 400 `LAYOUT_INVALID_INPUT` | `GridDimensions.From` → `ArgumentOutOfRangeException` |
| control: `grid: {}` (rows/cols default 0) | 400 `LAYOUT_INVALID_INPUT` | same |
| control: `name` omitted | 400 `LAYOUT_INVALID_INPUT` | `LayoutName.From(null)` → `ArgumentException` |
| control: `tiles` omitted | 400 `LAYOUT_INVALID_INPUT` | `ParseTiles`' `Ensure.That(requests)` → `ArgumentNullException` |
| control: tile `cameraIdentifier` omitted | 400 `LAYOUT_INVALID_INPUT` | `CameraIdentifier.From(Guid.Empty)` |
| control: body literal `null` | 400 | Minimal-API required-body check (`BadHttpRequestExceptionHandler`) |

The 500 body is the generic `"An error occurred while processing your request."` problem — no code,
no field named. No registered `IExceptionHandler` recognises `NullReferenceException`.

**The issue's finding holds, and is one member wider than it says**: a `null` *element* of `tiles`
escapes the same `try` the same way. Every other reference-typed member of the two request DTOs
already reaches the 400 (controls above), so after this spec the whole body surface of both
endpoints answers 400 for absent/null input. That is the full extent — verified member by member,
not assumed.

Not observed against the full Aspire stack (no stack was running; one machine, one stack). The
in-process host exercises the same handler code, the same exception-handler chain and the same JSON
binder; the phase-4a integration tests (§5) will observe it end-to-end over real HTTP with a real
token.

---

## 2. Scope decision — local fix, not `RespectNullableAnnotations`

**Chosen: close the two gaps inside `LayoutEndpoints.Commands.cs`**, so they reach the existing
`catch (ArgumentException)` and its `400 LAYOUT_INVALID_INPUT`.

**Rejected for this issue: `ConfigureHttpJsonOptions(o => o.SerializerOptions.RespectNullableAnnotations = true)`.**
It is not a bug fix; it is a repo-wide contract change, and it would need an ADR:

1. **It changes the answer's shape, not just its status.** A null for a non-nullable member would
   fail in the binder (`JsonException` → `BadHttpRequestException`) before the handler runs, so the
   caller gets `BadHttpRequestExceptionHandler`'s generic 400, not the context's own
   `LAYOUT_INVALID_INPUT` code that the sibling fields use. Every context's per-field error codes
   (`EVENT_INVALID_INPUT`, `LAYOUT_INVALID_INPUT`, …) would split into two populations.
2. **It also applies on serialization.** With the flag on, *writing* a response whose non-nullable
   reference property holds `null` throws. Every response DTO in nine contexts would need auditing
   first; a miss turns a working `200` into a `500` — the exact defect class this issue is about,
   introduced repo-wide.
3. **It affects every endpoint of every context at once** — including bodies that are correct today
   because a value object's `.From(null)` already refuses with a context-specific 400 (the `name` and
   `tiles` controls above).
4. It would be an Api-layer global policy decision nobody has made; the autonomous lane may not
   make it (ADR-0144).

The repo-wide exposure audit the issue asked for is in §8. If that audit shows the class is wide
enough to warrant a global answer, that is a separate issue carrying an ADR — not this one.

---

## 3. What kind of change this is

**Behaviour-changing → phase 4a colour RED.** Four inputs that answer `500` today will answer
`400 LAYOUT_INVALID_INPUT`. Every input that answers 400 or 2xx today answers exactly the same
afterwards.

---

## 4. User stories

### US1 (P1) — Omitting the grid is a bad request, not a server fault

An operator console (or any API client) that sends a layout create or draft edit without a `grid`
gets `400 LAYOUT_INVALID_INPUT` naming `grid` — the same answer as a grid with zero rows — instead of
an anonymous 500 that reads as a server outage and names no field.

**Independent test**: against the Aspire stack, with a real `sse.layouts.write` token,
`POST /layouts` with `{"name":…,"tiles":[]}` and `PATCH /layouts/{id}/revisions/1` (on a real draft,
with a valid `If-Match`) with `{"tiles":[]}` → both `400`, problem `title` `LAYOUT_INVALID_INPUT`,
`detail` contains `grid`; no layout is created and the draft's grid is unchanged.

### US2 (P2) — A null tile is a bad request, not a server fault

The same client sending `"tiles": [null]` gets `400 LAYOUT_INVALID_INPUT` naming the tile, not a 500.

**Independent test**: as US1, with `"tiles":[null]` and a valid grid, on both endpoints.

US2 is independently shippable but is two lines in the same file and the same `try`; it ships in the
same PR. Dropping it would knowingly leave a reproduced sibling 500 in the block this PR edits.

---

## 5. Requirements

- **FR-001** — `POST /layouts` with `grid` absent or `null` answers `400`, problem `title`
  `LAYOUT_INVALID_INPUT`, `detail` mentioning `grid` (case-insensitive). No layout is persisted.
- **FR-002** — `PATCH /layouts/{id}/revisions/{n}` with `grid` absent or `null` answers the same.
  The draft is not modified.
- **FR-003** — Either endpoint with a `null` element in `tiles` answers `400 LAYOUT_INVALID_INPUT`,
  `detail` mentioning `tile` (case-insensitive).
- **FR-004** — The refusal happens in the existing validation block, i.e. **at the same point** as
  the sibling malformed-input cases: before fab resolution and before any write; on `PATCH`, before
  the `If-Match` check (unchanged order — a bad grid already precedes 428 today).
- **FR-005** — Every input answering 400 or 2xx today is unchanged: status, `title`, and `detail`
  text (the §1 controls).
- **FR-006** — No change to `CreateLayoutRequest`/`EditDraftRequest` shapes or nullability, to any
  domain type, to `Shared.Contracts`, to JSON options, or to any other endpoint or context.
- **FR-007** — An `Idempotency-Key` sent with a refused body is not recorded (unchanged: validation
  already precedes `IdempotentRequest.ExecuteCreateAsync`), so the same key with a corrected body
  creates normally.

---

## 6. Acceptance scenarios

```gherkin
Feature: Absent layout body members are refused as bad input

  # Happy path — the defect, closed (RED today: 500)
  Scenario: Creating a layout without a grid is refused as invalid input
    Given an operator holding sse.layouts.write in fab dresden
    When they POST /layouts with {"name": "<unique>", "tiles": []}
    Then the response is 400
    And the problem title is "LAYOUT_INVALID_INPUT"
    And the problem detail mentions "grid"
    And no layout named "<unique>" exists

  Scenario: Editing a draft without a grid is refused as invalid input
    Given a Draft revision 1 of a layout in the operator's fab, and its current version
    When they PATCH /layouts/{id}/revisions/1 with If-Match <version> and {"tiles": []}
    Then the response is 400 with title "LAYOUT_INVALID_INPUT" and a detail mentioning "grid"
    And the draft's grid and tiles are unchanged

  Scenario: A null tile is refused as invalid input (US2)
    When they POST /layouts with a valid grid and {"tiles": [null]}
    Then the response is 400 with title "LAYOUT_INVALID_INPUT" and a detail mentioning "tile"

  # Conflict — what must NOT change (GREEN before and after)
  Scenario: A valid create still creates
    When they POST /layouts with a 2x2 grid and one tile on a camera in their fab
    Then the response is 201

  Scenario: An explicit zero-row grid keeps its existing refusal text
    When they POST /layouts with {"grid": {"rows": 0, "cols": 2}, ...}
    Then the response is 400 LAYOUT_INVALID_INPUT with detail "rows must be >= 1; got 0. (Parameter 'rows')"

  # Bad request — idempotency key is not burned by the refusal
  Scenario: A key refused for a missing grid can be reused with a corrected body
    When they POST /layouts with Idempotency-Key K and no grid
    Then the response is 400
    When they POST /layouts with Idempotency-Key K and a valid body
    Then the response is 201

  # Auth — unchanged; the body is never read
  Scenario: An anonymous caller omitting the grid is challenged, not validated
    When an unauthenticated caller POSTs /layouts with no grid
    Then the response is 401
```

---

## 7. Independent end-to-end test procedure

1. Boot the stack via the `AspireFixture` (ADR-0103); reset LayoutComposition.
2. Mint a token for `op-dresden@dresden.test` (the single-fab operator the layout suites use).
3. `POST /layouts` omitting `grid` → assert 400 / `LAYOUT_INVALID_INPUT` / detail ∋ `grid`; list
   layouts → the name is absent.
4. Create a valid draft (201), read its version; `PATCH …/revisions/1` with that `If-Match` omitting
   `grid` → assert 400 as above; `GET` the layout → grid and tiles unchanged.
5. Repeat 3 and 4 with `"tiles":[null]` and a valid grid → 400, detail ∋ `tile`.
6. Anonymous `POST /layouts` with no grid → 401.

---

## 8. Repo-wide exposure (the issue's "check before deciding")

See plan.md §6 for the member-by-member audit of every other request body in `src/*/Api`. Its
conclusion decides only whether a **follow-up issue** is filed; it does not widen this spec.

---

## 9. Locked choices

- Minimal APIs; `Results.Problem(title: "LAYOUT_INVALID_INPUT", …, 400)` via the existing catch
  (ADR-0070, ADR-0089).
- Guards use `Ensure.That(x).IsNotNull()` (ADR-0105) — its `ArgumentNullException` is an
  `ArgumentException`, which is what routes it to the existing 400. This mirrors `ParseTiles`'
  existing `Ensure.That(requests).IsNotNull()`, which is how an omitted `tiles` already reaches 400.
- Tests: integration via the Aspire fixture, xUnit + Shouldly, sentence-style names.
- No new package, abstraction, JSON option or exception handler.

## 10. Assumptions

- **A1** — The in-process reproduction is representative of the deployed endpoint (same handler, same
  handler chain, default JSON options). Phase 4a's integration run confirms it over real HTTP.
- **A2** — A `detail` naming the parameter (`(Parameter 'grid')`, `(Parameter 'tile')`) is
  acceptable wording; it matches the sibling `(Parameter 'requests')` / `(Parameter 'rows')` texts.
  Improving those texts is out of scope.
