# Feature Specification: A page is not the list

**Feature Branch**: `chore/1982-a-page-is-not-the-list`
**Created**: 2026-09-04
**Status**: Draft
**Input**: Issue #1982, raised by spec 048 — the camera picker asked for one page of 50 and rendered it as the complete set of choosable cameras.

---

## The survey came back clean, and that is the finding

Issue #1982 asked for a survey: find every caller of a paginated endpoint across
`apps/management-web`, `apps/kiosk-web` and `apps/shared`, and sort them into
*pages*, *bounds-and-declares*, or *silently truncates*.

**The survey found zero silently-truncating callers.** `LayoutEditorDialog` was
the one, and spec 048 fixed it. Every remaining caller of a bounded endpoint
either pages through it or bounds it deliberately and says so on screen.

That result changes what this feature should be. A survey whose answer is "clean"
has a half-life measured in the next pull request. It records that on
2026-09-04 nobody was discarding a `count`; it does nothing whatsoever about the
next caller who does. And the next caller is not hypothetical — the defect
compiled cleanly, passed review, and shipped once already, in a codebase where
the correct example (`CamerasPage`) was one directory away from the incorrect one
(`LayoutEditorDialog`), against the same endpoint, written by the same team.

ADR-0139 is titled *"Rules that fail the build, not the review"*, and its whole
argument is this situation: a rule that is stated but unenforced is a rule that
drifts. `PrimitiveBoundaryTests` and `HandlerDeconstructionTests` exist because
this repository already concluded that a convention a reviewer has to remember is
a convention that will eventually not be remembered.

**So this feature is the survey plus the guard.** The survey below is the
evidence; the guard is what keeps it true. See "Why not a document alone" for why
the two are one slice rather than two.

### Why the guard costs nothing to introduce here

The usual objection to landing a build-failing guard alongside an audit is that
the audit finds violations, and the guard then drags the "separate follow-up
work" into the current issue — usually resolved with a baseline or an allowlist
of known offenders.

**That objection does not arise, and only the survey could establish that.** There
are no violations. The guard is green on the tree as it stands, with no baseline,
no allowlist, no suppression and no recorded exception. Nothing about the gate is
being weakened, narrowed or deferred — which matters, because ADR-0144 forbids the
autonomous lane from weakening a gate to reach green, and a new guard shipped with
a list of things it agrees not to look at is exactly that move wearing a different
hat.

Had the survey found even one violation, the honest answer would have been
different: the guard would either have had to carry an exception list — which is
the forbidden move — or the fix would have had to land here, which the issue
explicitly scoped out. **The survey is what makes the guard admissible**, and it
had to be run first to know.

---

## The survey

### What "paginated" means here, and how the callers were found

Searching for callers by name would have under-counted: this repo has done that
before and got 5 where the answer was 12. So the sweep started from the
**response shape**, in both of its spellings, and from the endpoint definitions —
not from a guessed list of pages.

Three greps, across `apps/` and `src/`:

- the **offset shape** — a response carrying `count` / `offset` / `limit`
  beside its rows;
- the **cursor shape** — a response carrying `nextCursor`;
- the **server bound** — every `.Take(` / `pageSize` / `MaximumPageSize` in a
  query handler, so that an endpoint which caps silently could not hide behind a
  contract that does not mention capping.

Then every RTK Query hook exported by `apps/shared/src/api/*.api.ts` was traced to
its call sites, and every consumer of a `.items` / `.rows` / `.chains` /
`.published` field was read. Raw `fetch` was swept too: the only non-RTK network
calls in the apps are the WHEP client and the kiosk latency beacon, neither of
which returns a list.

### Endpoints that bound their response

| Endpoint | Shape | Server bound | Client |
|---|---|---|---|
| `GET /camera-catalog/cameras` | offset — `{ items, count, offset, limit }` | max `limit` 200, **refuses** above it (does not clamp) | `listCameras`, `listAllCameraChoices` |
| `GET /audit-observability/audit` | cursor — `{ rows, nextCursor }` | default 50, max 200 | `searchAudit` |
| `GET /audit-observability/audit/{kind}/{id}` | cursor — `{ rows, nextCursor }` | default 50, max 200 | `getResourceTimeline` |
| `GET /event-ingestion/events` | cursor | default 100, max 1000 | **none** |
| `GET /event-ingestion/dead-letters` | `.Take(limit)` | caller-supplied limit | **none** |

`GET /camera-catalog/cameras` is the **only** offset-paginated endpoint in the
system. Everything else that bounds a response does so by cursor.

### Every caller, classified

| Caller | Endpoint | Classification |
|---|---|---|
| `apps/management-web/src/features/cameras/CamerasPage.tsx` | `listCameras` (offset) | **pages** — holds `offset` in state, reads `data.count`, renders "Showing X to Y of Z"; Next disabled at `offset + items.length >= totalCount`, and the offset is reset when a filter changes so a later page cannot outlive the population it indexed into |
| `apps/shared/src/api/cameras.api.ts` → `listAllCameraChoices` | `listCameras` (offset, walked) | **pages, and declares the bound** — walks up to 5 × 200 with a `seen` set against page-boundary duplicates, and returns `complete` rather than leaving each consumer to re-derive `items.length >= count` |
| `apps/management-web/src/features/layouts/LayoutEditorDialog.tsx` | `listAllCameraChoices` | **bounds-and-declares** — renders "Showing N of M cameras" only when `complete` is false, and suppresses the adjacent match-count line so two totals for one list never appear side by side. This is the caller the issue was raised about; spec 048 is its fix |
| `apps/management-web/src/features/audit/AuditPage.tsx` | `searchAudit` (cursor) | **pages** — Next is driven by `data.nextCursor` and disabled when it is `null`; "First page" resets. See the note below on backward movement |
| `apps/shared/src/api/audit.api.ts` → `getResourceTimeline` | timeline (cursor) | **no caller** — the hook is exported and nothing imports it. Cannot truncate because nothing renders it |

**Silently truncating: none.**

### Where the boundary genuinely does not matter, and why

Recording this is the point of the section — an audit that only lists defects
makes the next audit re-derive the same negatives.

**The unbounded list endpoints.** `GET /layouts`, `/overlays`, `/rules`,
`/system-variables`, `/streams`, `/identity/devices`, `/identity/kiosks`,
`/identity/webhook-clients` and `/webhook-integrations` all reach
`ToListAsync` with **no `Skip`, no `Take`, and no cap anywhere in the handler**.
Their consumers — `LayoutsPage`, `OverlaysPage`, `RulesPage`,
`SystemVariablesPage`, the kiosk `PickerPage`, and `LayoutEditorDialog`'s overlay
list — render the whole array.

These are not defects of this shape, and the distinction is exact: **nothing is
dropped**, so nothing is silently absent. There is no `count` to discard because
the server is not withholding anything. A caller cannot truncate a response the
server did not truncate first.

They do carry a *different* exposure — at a thousand rules the whole thousand
cross the wire in one response, with no page and no notice — but that is a
response-size and render-cost concern, not an absence concern, and no issue tracks
it today. It is named here so that a future reader does not mistake the silence
for an oversight.

**`listStreams` takes an explicit identifier list**, and `CamerasPage` passes only
the identifiers of the rows it is currently showing. The set is bounded by the
caller's own page, by construction. (A caller that ever passed all 250 would hit a
URL-length limit long before a truncation, since the identifiers ride the query
string comma-joined — worth knowing, not worth guarding.)

**`getResourceTimeline` has no consumer**, so there is nothing to classify. If one
is written, the guard below is what will make it declare its boundary.

### One finding outside the stated scope

`src/StreamDistribution/Infrastructure/Attribution/CameraCatalogFabLookup.cs` is
the one **backend** caller of the paginated camera endpoint, and the issue scoped
the audit to the three frontend apps. It is recorded because the shape is
language-agnostic and an audit that stops at a language boundary invites the next
one to start over.

It **pages**, correctly, with a `do … while (fetched == pageSize)` at
`PageSize = 200`, accumulating into a `Guid`-keyed dictionary — so page-boundary
duplicates are absorbed for free. It ignores `count`, which is legitimate: the
short-page rule terminates correctly on its own.

The one gap: offset paging over a list under concurrent write can **skip** a row.
A camera registered mid-walk, under the endpoint's default `registeredAt desc`
ordering, shifts every later page down by one, and the row displaced across the
boundary is never fetched. The dictionary absorbs the duplicate case but cannot
invent the missed case. The consequence is one stream left without a fab
attribution until the next run. It is startup-only and it re-runs, which is why
this is a **follow-up candidate, not a defect fixed here** — and why it is not in
this feature's scope.

### What an operator would have not seen — the concrete consequence, kept

No caller truncates today, so this column has no rows. The consequence is recorded
for the one that did, because it is the calibration for everything the guard
exists to catch:

`LayoutEditorDialog` requested `limit=50` with the endpoint's default
`registeredAt desc` ordering, and rendered the result as the complete set of
choosable cameras. At the constitution's 250-camera production target
(§Scale, "Production target: 250 concurrent cameras per fab"), **200 cameras in
250 could not be placed on any wall**. Not greyed out, not marked unavailable, not
mentioned — absent, and absence is indistinguishable from "that camera was never
registered". The 50 shown were the 50 most recently registered, an order no
operator thinks in, so which 50 also changed every time anyone registered a
camera.

---

## Why not a document alone, and why not two issues

**(b) Survey only** would produce a doc-only PR. Phase 4a would then be
inapplicable — there is no behaviour to observe red and none to characterise
green — and ADR-0144 is explicit that 4a is the one phase with no skip
mechanism, so the lane cannot grant itself that exemption. It would be a
**blocked** outcome, parked for a human, over work the lane can actually finish.
Choosing it would mean stopping short of the deliverable to avoid the deliverable.

**(c) Two issues** — survey now, guard once the survey says what to assert — is
the right shape when the survey's output genuinely determines the guard's content.
It does not here. The survey's substance is one table and one sentence, and the
guard's assertion was determinable the moment the response shapes were
enumerated, which happened in the first hour of the survey. Splitting would add a
hand-off that carries no new information across it, and would leave the repository
holding a clean audit with nothing keeping it clean — the exact state ADR-0139
was written against.

**(a) Survey plus guard** is what the work is. The survey is the evidence that
the guard is admissible without an exception list; the guard is what gives the
survey a lifespan longer than one pull request.

### This does not need an ADR

The rule the guard enforces — a caller must not present a bounded response as the
whole population — is not a new architectural decision. Spec 048 established it as
a requirement, argued it, and shipped the fix. This feature enforces the rule
already made rather than making one.

The precedent is direct: **`HandlerDeconstructionTests` and `LogTailCoverageTests`
cite no ADR at all.** Both are build-failing guards introduced by a spec to
enforce a convention recorded elsewhere. `PrimitiveBoundaryTests` needed
ADR-0139/0140 because those *changed* what §II bans; nothing here changes what
anything bans.

Governing ADRs cited: **ADR-0139** (rules that fail the build, not the review —
the form of the guard and the phase-4a red obligation), **ADR-0144** (the
autonomous lane; the gate-weakening prohibition this feature is measured against),
**ADR-0074** (two frontend apps — the scope boundary the sweep covered).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A new caller cannot discard the boundary in silence (Priority: P1)

An engineer adds a screen that reads a bounded endpoint. If it renders the rows
without ever consulting the field that says how many exist, **the build fails**,
naming the file and the field it did not read.

**Why this priority**: This is the whole feature. The survey is a static
artefact; this is the part with a lifespan. Shipped alone it removes the failure
mode, and shipped alone it is the only part of this feature that will still be
doing anything in six months.

**Independent Test**: With the guard in place and the tree green, temporarily
restore the spec-048 defect — remove the truncation notice and the `complete`
read from `LayoutEditorDialog.tsx`, leaving it rendering `cameras.items` alone —
and confirm the guard fails and names that file. Then restore the file and
confirm it passes. **A guard that has only ever been observed green proves
nothing**, which is spec 048's own Independent Test argument applied to the
guard rather than to the notice.

**Acceptance Scenarios**

1. **Given** the repository as it stands, **When** the architecture suite runs,
   **Then** the paginated-consumer guard passes with no allowlist, no baseline
   and no recorded exception.
2. **Given** a module that calls a bounded-list hook and never references that
   hook's boundary field, **When** the suite runs, **Then** it fails, naming the
   module, the hook, and the field that was not read.
3. **Given** a module that calls a bounded-list hook and does reference its
   boundary field, **When** the suite runs, **Then** it passes.
4. **Given** a new bounded response type added to `apps/shared/src/api/` and a
   `build.query` that answers with it, **When** the suite runs and that
   *(type, hook)* pair is not in the guard's register, **Then** it fails, saying
   that a new paginated contract was added and its consumers are therefore
   unguarded. Registering the type name without its hook does not restore green.
5. **Given** a bounded-list hook with no consumer at all (`getResourceTimeline`
   today), **When** the suite runs, **Then** it passes — an unconsumed endpoint
   cannot mislead anyone.

---

### User Story 2 - The audit is readable a year from now (Priority: P2)

A future engineer asking "did anyone check whether we render pages as lists?"
finds the answer, the method, and the negatives — including which lists were
looked at and found not to matter, and why.

**Why this priority**: Real but lesser. It is the part of the deliverable that
decays, and it decays gracefully: a stale table misleads far less than a missing
guard.

**Independent Test**: Read `spec.md` alone and reconstruct which endpoints bound
their responses, which callers consume them, and which lists were deliberately
excluded — without reading any source.

**Acceptance Scenarios**

1. **Given** the spec, **When** a reader looks for the classification of any
   caller of a bounded endpoint, **Then** it is in the table with its
   classification and the evidence for it.
2. **Given** the spec, **When** a reader asks why `RulesPage` is absent from the
   defect list, **Then** the "boundary genuinely does not matter" section answers
   it without them re-deriving it from the handlers.

---

### Edge cases

- **A caller that names the boundary field and ignores it.** The guard sees a
  reference, not a use, so this passes. It is a declared limitation, not an
  oversight — see `plan.md` §Declared limitations. The guard makes the omission
  loud; it does not make misuse impossible.
- **A test file calling a bounded hook.** Tests are excluded from the consumer
  sweep — both repository conventions, `*.test.ts(x)` and `*.spec.ts(x)`; a
  fixture that renders `items` alone is a fixture, not a screen. Only the first
  suffix was excluded as first implemented, which left `client.spec.ts` inside a
  sweep this line said it was outside; corrected in phase 6.
- **The producer module itself.** `cameras.api.ts` calls the endpoint through
  `baseQuery` inside `listAllCameraChoices` and is the thing that computes the
  boundary. It is excluded by path, not by exception.
- **A new app directory.** The guard globs `apps/*/src` and `apps/shared/src`
  rather than naming the two apps, so a third app is covered on the day it
  exists — *provided it puts its sources in `src/`*. One that does not is not
  quietly skipped: the corpus assertion (FR-008) requires every `apps/*` package
  to contribute, so a different layout fails the build and is taught to the
  sweep rather than left outside it.

---

## Requirements *(mandatory)*

- **FR-001**: The repository MUST hold a written classification of every caller,
  in `apps/`, of an endpoint whose response bounds the population it describes —
  as *pages*, *bounds-and-declares*, or *silently truncates* — with the evidence
  for each classification.
- **FR-002**: The audit MUST record the lists where the boundary does not apply
  and state why, so a later audit does not re-derive them.
- **FR-003**: A build-failing guard MUST assert that every consumer of a bounded
  response references that response's boundary field.
- **FR-004**: The guard MUST assert that its own register is complete against
  `apps/shared/src/api/`, so that a newly added paginated contract cannot arrive
  unguarded. **Completeness is of the (response type, producing hook) pair, not
  of the type name alone**: a register whose type list and hook list are
  independent guarantees only that someone was *told* about the new contract,
  because adding the type name is then a green-restoring edit that leaves the
  hook unswept. The producing hook MUST be derived from the `build.query`
  declaration rather than supplied alongside the type.
- **FR-005**: The guard MUST ship with **no allowlist, no baseline and no
  recorded exception**. If one becomes necessary, that is a blocked outcome
  requiring human acceptance, not a line added to the guard.
- **FR-006**: The guard's failure message MUST name the file, the hook, and the
  field that was not read — enough to act on without opening the guard.
- **FR-007**: The guard MUST be observed failing against a deliberately
  reintroduced instance of the defect before the feature is considered done, and
  that failure MUST be quoted in the PR body (ADR-0139, constitution §Testing).
- **FR-008**: The guard MUST assert that the corpus it sweeps is non-empty and
  structurally intact — every app under `apps/` contributes its sources, and the
  file count is above a floor. A sweep that finds nothing passes every consumer
  assertion vacuously and permanently, and that outcome is indistinguishable
  from compliance. Added in phase 6.

### Key entities

None. No domain model, no bounded context, no persisted state.

---

## Locked technology choices

| Concern | Choice | Why here |
|---|---|---|
| Guard location | `tests/Architecture.Tests/` | Where every other cross-cutting guard lives; already contains five tests that read `apps/` source (`KioskMeasurementContractTests`, `KioskRecoveryRecordTests`, `LatencyLegRecordTests`, `OverlayFrameClaimTests`, `WallClientDeclarationTests`) |
| Guard language | C# / xUnit + Shouldly | ADR-0052; matches every neighbouring guard. A vitest equivalent would put the rule in the same toolchain it polices, and would not run in the architecture suite the merge gate already blocks on |
| Guard style | Source scan with a register held **in the test**, not in a document | Consistency-shaped like `OverlayFrameClaimTests`. Holding the register in the test avoids a guard whose only claim is that a document was written — which proves the design was recorded, not that it holds |
| Test naming | Sentence-style with underscores | ADR-0053 |

---

## Latency budget impact

**N/A.** No leg of the event-to-overlay path is touched. This feature adds one
test file and one specification directory; no runtime code changes, no request is
issued differently, and nothing on the SLO path is read or written.

Recorded explicitly rather than omitted, because constitution §IV's table has
twice been wrong by omission rather than by error.

---

## Success criteria

- **SC-001**: The classification table covers every caller found by the
  shape-first sweep, and a reader can re-run the sweep from the method described
  and reach the same set.
- **SC-002**: The guard fails on the reintroduced spec-048 defect and names
  `LayoutEditorDialog.tsx`; the failure text is quoted in the PR body.
- **SC-003**: The guard passes on the tree as it stands, with no allowlist.
- **SC-004**: The architecture suite's runtime does not materially change — the
  guard reads a few dozen files once.

## Out of scope

- **Fixing any truncating caller.** There are none. Had there been, the issue
  scoped the fix as separate follow-up work.
- **#1983** — the camera endpoint refusing any request above 200 while the
  constitution targets 250 per fab. Tracked separately, and untouched here.
- **The `CameraCatalogFabLookup` skip-under-concurrent-write finding.** Recorded
  above as a follow-up candidate; a fix is a behaviour change to a backend
  startup path and belongs in its own issue.
- **Response size on the unbounded list endpoints.** Named above; no issue tracks
  it; filing one is a judgement call for a human.
- **Backward paging on `AuditPage`.** The cursor is forward-only by construction,
  and "First page" plus a correctly-disabled Next is a declared bound, not a
  silent one. A Previous control is a UX story, not this defect.
