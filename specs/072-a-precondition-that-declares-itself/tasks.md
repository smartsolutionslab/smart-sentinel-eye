# Tasks — Spec 072, a precondition that declares itself

**Phase:** 3 (Tasks) · **Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2088

**Engineer:** `backend-engineer` throughout. One C# guard in
`tests/Architecture.Tests` plus twelve fluent lines in three `src/*/Api` files.
No frontend, no infrastructure, no Aspire wiring, no migration, no domain code.
`test-writer` owns T001–T007 per ADR-0144's phase-4 split; the engineer receives
the verbatim red output as its brief and **may not edit the guard to pass**.

**Phase 4a colour: red.** The guard is new behaviour in the test suite and must
be observed failing against unmodified `src/` before any chain is touched
(T008). Characterisation would pin today's `Produces` chains — which are the
thing being changed — so it is the wrong instrument here; the spec's
*Phase 4a* section carries the reasoning.

**Docker-free.** `dotnet test tests/Architecture.Tests` only. No Aspire fixture,
no Postgres, no solution-wide build (ADR-0103). Build the one test project and
the Api projects it reads.

**Parallelism.** Two halves with a hard gate between them. **T001–T007 are
strictly serial** — every one edits the same single new file,
`PreconditionDeclarationTests.cs`, so ADR-0109's disjoint-file condition fails
and no `[P]` would be honest. **T009–T011 are a real fan-out**: three disjoint
`*Endpoints.cs` files, no shared state, no ordering between them.

**Do not fan out T001–T007.** Two agents on one file is a merge conflict, not
throughput.

---

## Foundational — blocks everything

- **[T001] [US-1]** Create
  `tests/Architecture.Tests/PreconditionDeclarationTests.cs` with the
  class-level XML doc stating the guard's claim **and its declared limits**,
  following `StaleCodeConventionTests` (147 lines, same single-claim shape). Add
  the shared readers: `RepositoryRoot()`, `/`-normalisation, `\r` stripping,
  `WithoutComments` + `MaskLiterals`, and the `src/*/Api/**/*.cs` glob.
  *Depends on: nothing. Blocks: T002–T007.*

  **Done when:** the project compiles and a scratch assertion lists the **12**
  endpoint classes and the files declaring each, with forward slashes on both
  platforms. `LayoutEndpoints` must list **three** files and `OverlayEndpoints`
  **three**; if either lists one, the partial-class index is wrong and every
  downstream assertion is green and meaningless.

- **[T002] [US-1]** The mapping reader (FR-001). For each `Map(Get|Post|Put|
  Patch|Delete)` site: capture the statement to its terminating `;`, the route
  literal, the **handler method-group name** (the bare identifier that follows
  the route literal), and the whole chain text for later `ProducesProblem`
  matching. An argument that is not a bare identifier — a lambda, a qualified
  name — is recorded as **unreadable**, not skipped.
  *Depends on: T001. Blocks: T003–T007.*

  **Done when:** `group.MapPost("/{name}/publish", Publish)` in
  `RulesEndpoints.cs` yields handler `Publish` and a chain containing
  `Status409Conflict` but not `Status428PreconditionRequired`; and the reader
  reports **zero** unreadable mappings across `src/*/Api`.

- **[T003] [US-1]** Handler resolution within the declaring class (FR-002).
  Index `(class, method) -> body` across every file declaring that class in the
  same Api project; find a mapping's declaring class by brace containment; body
  extent by brace matching over masked source, as
  `HandlerDeconstructionTests.Balanced` does.
  *Depends on: T001, T002.*

  **Done when:** `OverlayEndpoints.Publish` resolves from
  `OverlayEndpoints.cs` into the body at
  `OverlayEndpoints.Commands.cs:78`. **And** the two `List` handlers and two
  `Disable` handlers in `src/Identity/Api` resolve to their own classes — if
  either resolves ambiguously, name-only resolution has leaked in and the
  Identity mappings are being judged against another class's body.

## Assertions

- **[T004] [US-1]** **A1 + A2 / FR-009** — every mapping resolves to exactly one
  handler body; zero, two, or unreadable **fails** naming the shape and the
  shapes the reader accepts.
  *Depends on: T003.*

  **Done when:** all mappings resolve on the unmodified tree, and rewriting one
  mapping's handler argument as an inline lambda produces a failure — not a
  pass, and not a mirror failure.

- **[T005] [US-1]** **A3 / FR-004** — every mapping whose handler body calls
  `ConcurrencyHeaders.TryReadExpectedVersion` or `TryReadUpsertPrecondition`
  declares `Status428PreconditionRequired` in its own chain. Message per the
  plan's *omission* text; names file, line, verb, route, handler, helper.
  *Depends on: T004.*

  **Done when:** red on today's tree, reporting **exactly 9** endpoints across
  **3** files, and **LayoutComposition's five are absent from the report**.

- **[T006] [US-1]** **A4 / FR-005** — the mirror: a mapping declaring 428 whose
  handler calls neither helper fails, with a message **distinct** from T005's.
  *Depends on: T004.*

  **Done when:** green on today's tree (**0** offenders — this is the control
  that handler resolution is not failing open), and adding a 428 declaration to
  `GET /rules/{name}` produces the mirror message, not the omission message.

- **[T007] [US-1]** **A5 + A6 + A7 / FR-006 + FR-008** — the two independent
  sweeps and the pinned corpus. Count `ConcurrencyHeaders.TryRead*` occurrences
  and `Status428PreconditionRequired` occurrences by file sweep, assert each
  equals what the mapping walk found, and pin **17 endpoints across 7 files**.
  *Depends on: T002, T005.*

  **Done when:** it asserts `TryRead* == 17`, `428 declarations == 8` before the
  fix and `== 17` after, and the pinned corpus is 17/7. **And** moving one
  `TryReadExpectedVersion` call out of its handler into a private helper turns
  it red — this is the assertion that keeps the lexical body scan honest, and if
  it stays green both sides are counting the same thing twice and FR-006 is
  decorative.

## Gate — the red observation

- **[T008] [US-1]** Run `dotnet test tests/Architecture.Tests` against
  **unmodified `src/`** and capture the **verbatim** output. This is the
  phase-4a artefact.
  *Depends on: T001–T007. **Blocks T009–T011.***

  **Done when:** the run is **red**, and all four of these hold:

  1. T005 fails reporting **9** endpoints across **3** files — `RulesEndpoints`
     ×2, `OverlayEndpoints` ×5, `SystemVariableEndpoints` ×2.
  2. `LayoutEndpoints.cs`' five endpoints **do not appear**. They are the same
     shape and they are correct; their appearing means the reader is not binding
     a mapping in `LayoutEndpoints.cs` to its handler in
     `LayoutEndpoints.Commands.cs`, and the guard's central claim is unproven.
  3. T006 (the mirror) is **green** — 0 offenders.
  4. T007's `428 declarations == 8` holds.

  Output is quoted in the PR body.

  **If the run is green, stop and report.** Diagnosis: either the sweep matched
  nothing, or handler resolution silently returned "no handler" for every
  mapping and T004 is not wired.

## Declarations — the fan-out

Three disjoint files, no ordering between them. Each: add the
`.ProducesProblem(...)` lines listed, plus **one** comment per file stating the
428/409 pair once — following `CameraEndpoints.cs:68-70`, the existing house
style for this exact explanation. **Change nothing else** — no `Produces<T>`,
no route, no handler body, no `RequireAuthorization`, no `WithSummary`.

- **[T009] [P] [US-1]** `src/Automation/Api/RulesEndpoints.cs` — **+3**.
  428 on `PublishRule` and `ArchiveRule`; **409 on `ArchiveRule`**, whose
  `RULE_STALE` (`HttpStatusCode.Conflict`,
  `ArchiveRuleCommandHandler.cs:38`) is currently undeclared while its sibling
  `PublishRule` declares 409 already.

- **[T010] [P] [US-1]** `src/OverlayDesigner/Api/OverlayEndpoints.cs` — **+6**.
  428 on all five write endpoints (`PublishOverlayRevision`,
  `ArchiveOverlayRevision`, `BranchDraftOverlayRevision`,
  `EditDraftOverlayRevision`, `RevertOverlayRevision`); **409 on
  `ArchiveOverlayRevision`** (`OVERLAY_REVISION_STALE`,
  `ArchiveRevisionCommandHandler.cs:35`). The declarations go in
  `OverlayEndpoints.cs`, **not** `OverlayEndpoints.Commands.cs` — the chain is
  in the mapping file, the handler is in the partial.

- **[T011] [P] [US-1]** `src/SystemVariables/Api/SystemVariableEndpoints.cs` —
  **+3**. 428 on `SetSystemVariableValue` and `ArchiveSystemVariable`; **409 on
  `ArchiveSystemVariable`** (`VARIABLE_STALE`,
  `ArchiveVariableCommandHandler.cs:39`). Touch **only** those two mappings —
  the three GETs in this file are #2070's rows and are out of scope here, as
  they were in spec 070.

*All of T009–T011 depend on T008 and on nothing else.* Twelve lines total:
nine 428s and three 409s.

## Verification

- **[T012] [US-1]** Re-run `dotnet test tests/Architecture.Tests`. Green, with
  the guard's seven assertions passing **and** the three neighbouring guards
  that read these same files — `EndpointScopeDeclarationTests`,
  `StaleCodeConventionTests`, `HandlerDeconstructionTests` — passing
  **unmodified**. An assertion in any of them that has to be edited is evidence
  something moved that should not have; block, do not adjust.
  *Depends on: T009–T011.*

- **[T013] [US-1]** Run the spec's independent end-to-end procedure, steps 1–8.
  *Depends on: T012.*

  **Done when:** the three grep figures re-measure as **17 / 17 / 2**; steps 3,
  4, 5 and 6 each produce exactly one failure with the right message; and step 7
  holds — `git diff src/` contains only added `.ProducesProblem(...)` lines and
  three comments, with no `Produces<T>`, route, handler, `RequireAuthorization`
  or `WithSummary` change anywhere.

  Step 7 is the load-bearing one for phase 5: it is what makes "behaviour-
  preserving in the application, corrected in the document" checkable rather
  than asserted.

## Board

Per CLAUDE.md's corrected Phase 3 gate, **no per-task issues**. #2088 is the
feature-level issue; add it to Project #13 if it is not already there:

```sh
gh project item-add 13 --owner smartsolutionslab \
  --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2088
```

Verify with `--limit 2000` — `item-list` defaults to 30 and a filled board looks
empty without it.

## Follow-up issue to file (not done here)

**The Layer 2 conflict no context declares.**
`src/ServiceDefaults/Persistence/ConcurrencyConflictExceptionHandler.cs` turns
EF's `DbUpdateConcurrencyException` into `AGGREGATE_VERSION_CONFLICT` (`409`) on
**every mutating endpoint in every context**, and ADR-0119 records that no
context declares it. That is ~28 endpoints, a superset of this spec's 17, and a
genuinely different fix — the handler is registered centrally, so the answer is
an endpoint filter or convention, not 28 hand-written lines. Cite ADR-0119's
*"An eighth site, which no context declares"* section.

## Blocked / needs a decision before phase 4

1. **No ADR is required and none may be written here** (ADR-0144). ADR-0113
   already states the 428/409 pair; ADR-0119 already amended the stale half and
   set the precedent for guarding a convention with an architecture test. This
   spec implements existing decisions and makes none. **If a reviewer concludes
   ADR-0113 must be amended to say the contract surface must declare what the
   handler returns, that is blocked and must go back to a human.**
2. **Branch prefix.** The brief gives `fix/2088-a-precondition-that-declares-itself`.
   Specs 068 and 070 used `test/` for guard-plus-metadata work. Flagged for the
   orchestrator, not decided here; nothing in these artefacts depends on it.
3. **Re-measure if spec 071 lands first.** It is in flight on another branch and
   is not on `origin/develop` at `0f20dcd`. If it touches `src/*/Api`, re-run
   the spec's three grep commands before T008 and update FR-008's pinned counts
   in the same diff.
