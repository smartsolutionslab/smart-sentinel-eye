# Plan 215 — The 400 an empty fab never got

**Spec:** [`spec.md`](./spec.md)
**Phase:** 2 — awaiting review
**Shape:** a single-context, single-file behaviour fix at an HTTP trust boundary.
No new types, no new files under `src/`, no messaging, no persistence.

---

## Bounded context and layers

**One context: `AuditObservability`. One layer: `Api`.**

| Layer | Touched | Why |
|---|---|---|
| `Domain` | **No** | `FabIdentifier` and `ResourceIdentifier` already refuse an empty value correctly (ADR-0105). The defect is that the refusal is never reached. |
| `Application` | **No** | `GetResourceTimelineQueryHandler` receives a parsed `FabIdentifier`; a malformed fab cannot reach it. A new `GetResourceTimelineError` variant would be unreachable code (ADR-0093). |
| `Infrastructure` | **No** | No query, schema or projection changes. |
| `Api` | **Yes — the only production edit** | `src/AuditObservability/Api/AuditEndpoints.cs`, two private handlers. |
| `ServiceDefaults` | **No — and this is a success criterion (SC-F)** | `IFabAuthorizationGuard` keeps its precondition. Twelve call sites depend on it failing loudly. |

**Cross-context impact: none.** Nothing in `Shared.Contracts` changes; no
integration event is published or consumed; no other context references
`AuditObservability`. The NetArchTest boundary rules are unaffected.

---

## Entities and value objects

No type is added or modified. Two existing value objects carry the invariants the
fix relies on, and both already enforce them:

| Value object | Location | Invariant (unchanged) |
|---|---|---|
| `FabIdentifier` | `src/AuditObservability/Domain/AuditEvent/FabIdentifier.cs` | non-null, non-whitespace; 2–32 chars; lowercase letters / digits / `-`; starts with a letter. Throws `ArgumentException` via `Ensure.That` (ADR-0105). Per-context copy (ADR-0044). |
| `ResourceIdentifier` | `src/AuditObservability/Domain/AuditEvent/ResourceIdentifier.cs` | as today; the fix does not move its parse relative to the handler, only relative to the guard. |

**The invariant this spec actually adds is not on a type — it is on a sequence.**
Stated once, here, so the test has something to name:

> On a handler where `fabId` arrives from the caller, `FabIdentifier.From` runs
> **before** `IFabAuthorizationGuard.EnsureAccessAsync`, and the guard is called
> with the parsed value. Every other resource-identifying parse runs **after**
> the guard.

---

## The change, precisely

### `AuditEndpoints.GetTimeline` — reorder into three ordered gates

Current shape (`AuditEndpoints.cs:112-133`): **guard → parse both → handler**.
Target shape: **parse fab → guard → parse resource → handler**.

| Gate | Input | Failure | Status |
|---|---|---|---|
| 1 | `fabId` (query, required) | `ArgumentException` from `FabIdentifier.From` | `400 AUDIT_INVALID_INPUT` |
| 2 | parsed fab + caller claims | `FabAuthorizationException` | `403 RESOURCE_FAB_NOT_AUTHORIZED` (global handler) |
| 3 | `resourceIdentifier` (route) | `ArgumentException` from `ResourceIdentifier.From` | `400 AUDIT_INVALID_INPUT` |

Gate 3 stays behind gate 2 deliberately (spec SC-5): a caller refused the fab must
not learn whether their resource identifier was well-formed. This is the ordering
`DevicesEndpoints.Disable` and `KiosksEndpoints.Disable` already state in prose.

The existing single `try` block that wraps both parses therefore **splits into
two**, with the guard call between them. The `catch` body is identical in both —
same code, same `ex.Message`, same status — so no error shape changes.

The guard is called with `parsedFab.Value`, matching
`Identity/DevicesEndpoints.cs:134` and `Identity/KiosksEndpoints.cs:136`, not with
the raw `fabId` string.

**Comment budget (constitution §"No drive-by comments").** The existing comment at
`AuditEndpoints.cs:115-120` explains why the parses moved to the edge; it stays and
gains one clause naming why the fab parse now precedes the guard. One `why`, not a
narration of the sequence.

### `AuditEndpoints.Search` — widen one predicate

`AuditEndpoints.cs:76`: `if (fabId is not null)` → `if (!string.IsNullOrWhiteSpace(fabId))`.

The guard call inside is unchanged. `SearchAuditQuery.Fab` continues to receive
`fabId`; when it is empty the handler's existing "no fab named" path already scopes
to `CallerFabs`, which is the behaviour an omitted `fabId` gets today.

**One thing to confirm during implementation, not assume:** that
`SearchAuditQueryHandler` treats `Fab: ""` the same as `Fab: null`. If it does
not, `Fab` must be normalised to `null` at the same site. Read
`src/AuditObservability/Application/Queries/Handlers/SearchAuditQueryHandler.cs`
before editing; SC-8 asserts the resulting rows, so a residual empty-string filter
fails the test rather than passing quietly.

---

## Messaging

**None.** Domain → integration event flow is untouched. No `Shared.Contracts`
message is added, versioned or consumed. This handler is a read path.

---

## Boundary rules

| Rule | Status |
|---|---|
| No cross-context project references (NetArchTest) | Unaffected — one context, one file |
| Communication only via `Shared.Contracts` | Unaffected — no messaging |
| Domain has no I/O or framework refs | Unaffected — Domain untouched |
| `Api` may reference `ServiceDefaults.Authorization` | Already does; unchanged |
| §II: no primitives on a domain model (`PrimitiveBoundaryTests`) | Unaffected — no domain model changes. `fabId` is a `string` on an **Api** handler signature, which §II exempts (wire shapes). |
| ADR-0141 `Option<T>` preference | Does not apply — `Api` layer, explicitly out of that ADR's scope |
| ADR-0105 `Ensure.That` | The endpoint uses `try/catch` around `.From`, not a guard. Correct: this is a trust boundary translating caller input, not an argument precondition. |

---

## Test strategy

Three levels, and the middle one is where the evidence lives.

### Endpoint behaviour — `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`

**Extend the existing file; do not create a new one.** It already boots the
`AspireFixture`, already mints `admin@munich.test`, already asserts the
`munich`/`berlin` pair, and already covers exactly this endpoint. A second file
would duplicate the fixture cost (ADR-0103: no Testcontainers, the Aspire stack is
the expensive resource) and split one endpoint's contract across two files.

New facts (RED): SC-1, SC-2, SC-8.
New facts (characterisation, green from the start): SC-5, SC-6, SC-7, SC-9, SC-10.
Existing facts that must pass unmodified: SC-3, SC-4 — already in the file.

### Unit — none needed, and saying so is the decision

There is no `AuditObservability.Api.Tests` project, and the change is an ordering
of calls inside a private minimal-API delegate. A unit test would need the handler
extracted to a testable seam — an abstraction with no other caller, which ADR-0036
rules out. The ordering invariant is observable over HTTP (SC-5 distinguishes it
from every other ordering), so the integration test is the *direct* test, not a
substitute for one.

`DefaultFabAuthorizationGuardTests` and `FabResolutionTests` stay untouched: SC-F
says the guard does not change, and those tests are the thing that would notice if
it did.

### Architecture — none

Considered and deliberately declined; the reasoning is in spec §"Out of scope".

---

## Risk register

| Risk | Detection | Mitigation |
|---|---|---|
| The reorder trades a 403 for a 400 on a cross-fab request — the fab boundary moves | SC-4, SC-5, SC-9 | SC-4 and SC-9 are existing/characterisation assertions that must pass **unmodified**; an edit to either blocks the work (constitution §Testing) |
| A-1 is wrong and ASP.NET binds `?fabId=` to `null`, not `""` | Phase 4a's red run shows a framework 400, not a 500 | Phase 1 reopens; no code is written on a premise the red run contradicted |
| `Search`'s handler filters on `Fab: ""` and returns zero rows instead of the caller's | SC-8 asserts rows are returned and scoped | Read `SearchAuditQueryHandler` before editing (see §"One thing to confirm") |
| The fix is written against `string.Empty` only and whitespace still 500s | SC-2 | SC-2 is a separate red fact, not a variant of SC-1 |
| A new error code is introduced out of habit | SC-E | Grep `src/` for new `AUDIT_*` codes before the PR |

---

## Constitution and ADR alignment

| Principle / ADR | How this plan satisfies it |
|---|---|
| **ADR-0036** smallest possible change | Two handlers, one file, no new type, no new file, no new error code. A bug fix that changes the bug and nothing else. |
| **ADR-0139** rules that fail the build | The gap is closed where the build can see it: a test over HTTP that fails red today. No new unenforced rule is written down. |
| **ADR-0105** `Ensure.That` guards | Preserved, not weakened. The guard's precondition stays; the caller stops violating it. |
| **ADR-0089 / ADR-0047** `ApiError` / `Result` | Endpoint-boundary parse failures keep the established hand-written `Results.Problem` shape (spec 091 "Shape A"); the handler's `Result<T, GetResourceTimelineError>` path is untouched. |
| **ADR-0093** Application folder structure | Respected by *not* adding an unreachable variant to `GetResourceTimelineErrors.cs`. |
| **ADR-0114** fab inference | `Search`'s new predicate adopts the exact empty ⇒ omitted rule `FabResolution` already applies; nothing about inference is extended to a new endpoint. |
| **ADR-0044** per-context VOs | `AuditObservability.FabIdentifier` used as-is; no sharing. |
| **Constitution §Testing** | RED for the three new-behaviour facts, characterisation-green for the rest; an assertion that must be edited blocks. |
| **Constitution §IV** latency | N/A, stated in the spec. Not on any of the six legs. |
| **§"No drive-by error handling"** | The `catch (ArgumentException)` is at a trust boundary and returns a typed refusal. Nothing is swallowed. |

**No ADR is required.** Every decision is an application of existing ones. If the
reviewer disagrees — in particular with declining the repo-wide architecture test —
that is the one point that would need an ADR, and it is flagged rather than
decided silently.
