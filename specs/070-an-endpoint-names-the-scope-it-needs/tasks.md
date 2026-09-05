# Tasks — Spec 070, an endpoint names the scope it needs

**Phase:** 3 (Tasks) · **Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2087

**Engineer:** `backend-engineer` throughout. One C# guard in
`tests/Architecture.Tests` plus metadata edits in `src/*/Api`. No frontend, no
infrastructure, no Aspire wiring, no migration. `test-writer` owns T001–T008 per
ADR-0144's phase-4 split; the engineer receives the verbatim red output and may
not edit the guard to pass.

**Phase 4a colour:** **red.** The guard is new behaviour and must be observed
failing on `70f9223` before any endpoint is touched. A green first run is a
phase-4 failure with a specific diagnosis — the sweep matched nothing (T008).

**Parallelism.** Two genuinely different halves. **T001–T008 are strictly
serial** — every one of them edits the same single file,
`EndpointScopeDeclarationTests.cs`, so ADR-0109's disjoint-file condition fails
and no `[P]` is honest there. **T010–T018 are a real fan-out**: eight disjoint
`*Endpoints.cs` files plus one read-only confirmation over the four already-
conformant files, no shared state, no ordering between them. The orchestrator
should expect one serial build-up, a hard gate at T009, then nine parallel
tasks.

**Do not fan out T001–T008.** Two agents on one file is a merge conflict, not
throughput.

---

## Foundational — blocks everything

- **[T001] [US-1]** Create `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs`
  with the class-level XML doc stating the guard's purpose **and its declared
  limits**, following `PaginatedConsumerTests`. Add the shared readers:
  `RepositoryRoot()`, `/`-normalisation, `\r` stripping, and the
  `*Endpoints.cs` glob over `src/*/Api/**`.
  *Depends on: nothing. Blocks: T002–T008.*

  **Done when:** the project compiles and a scratch assertion lists the **12**
  endpoint files by repository-relative path, with forward slashes on both
  platforms.

- **[T002] [US-1]** The chain parser. Accumulate each statement from `Map*` to
  its terminating `;`; join adjacent string literals and `+` concatenations;
  reject interpolation. Record per file the `RouteGroupBuilder <name> =
  app.MapGroup(...)` declarations with their authorization, and bind each
  mapping to its receiver **by variable name**.
  *Depends on: T001. Blocks: T003–T008.*

  **Done when:** `reads.MapGet("/", List)` in `RulesEndpoints.cs` resolves to
  `sse.rules.read` — **not** `sse.rules.write`. **If it resolves to write,
  stop**: the parser is binding by route prefix, both groups are `/rules`, and
  every downstream assertion is green and wrong. Same check for
  `CameraEndpoints.cs`' `writes` / `reads` pair.

- **[T003] [US-1]** Register A by reflection over
  `ServiceDefaults.Authorization.Scope` — walk nested public static classes for
  `public const string` fields; index full constant paths and their tail
  suffixes. No scope string is typed into the test.
  *Depends on: T001.*

  **Done when:** `Scope.Sse.Identity.DeviceClients.Read` resolves to
  `sse.identity.devices.read`, and the register is built without referencing
  `Scope.All`.

## Assertions

- **[T004] [US-1]** **A2 + A3** — every mapping resolves to scoped, anonymous,
  bare, or **unreadable**, and unreadable fails. Every scope argument resolves
  against Register A. `[Theory]` per file.
  *Depends on: T002, T003.*

  **Done when:** all 56 mappings classify as **51 scoped, 2 anonymous, 3 bare**,
  and mangling one chain into an unrecognised shape produces a failure rather
  than a skip.

- **[T005] [US-1]** **A4** — every scoped endpoint's summary contains
  `Required scope: <literal>` for the scope it enforces. `[Theory]` per file.
  *Depends on: T004.*

  **Done when:** red on today's tree, reporting **33** scoped endpoints missing
  the sentence, and green for the 18 that already have it.

- **[T006] [US-1]** **A5** — a summary naming a *different* scope than the one
  enforced fails with a message distinct from T005's, and that message states
  why a wrong scope is worse than an absent one.
  *Depends on: T004.*

  **Done when:** editing one `AuditEndpoints` summary to say
  `sse.audit.write` produces the mismatch message, not the omission message.

- **[T007] [US-1]** **A6 + A7 + A8** — the `UnenforcedByDesign` register,
  checked both ways, shipping the three #2070 routes; and `No OIDC scope:` on
  both anonymous endpoints.
  *Depends on: T004.*

  **Done when:** deleting a register row reddens A6 naming that route; adding a
  row for a scoped route reddens A7 naming the stale row.

- **[T008] [US-1]** **A1 / FR-010** — the denominator guard: the enumerated
  mapping count equals a repository-wide `Map*` sweep under `src/*/Api`.
  *Depends on: T002.*

  **Done when:** it asserts **56**, and moving one endpoint into a file the glob
  does not match turns it red.

## Gate — the red observation

- **[T009] [US-1]** Run `Architecture.Tests` against **unmodified `src/`** and
  capture the **verbatim** output. This is the phase-4a artefact.
  *Depends on: T001–T008. **Blocks T010–T018.***

  **Done when:** the run is **red**, reporting **35** endpoints across **8**
  files — 33 scoped ones missing or misnaming the `Required scope:` sentence,
  plus the 2 anonymous ones missing `No OIDC scope:`.

  Two absences are the cheapest available check that the guard discriminates
  rather than failing everything, and both must hold: the three Identity files
  and `AuditEndpoints.cs` do not appear (already conformant), and **the three
  #2070 routes do not appear** (covered by the register — if they do, the
  register half is not wired). Output is quoted in the PR body.

  **If the run is green, stop and report.** It means the sweep found nothing.

## Endpoint metadata — the fan-out

Eight disjoint files, no ordering between them. Each: add or amend summaries per
FR-011, change **nothing else** — no `RequireAuthorization`, no route, no
handler, no `Produces`. Where a summary is new, one plain sentence of what the
endpoint does plus the scope sentence; **do not invent behavioural claims** (the
guard does not check them and a wrong description is worse than a terse one).

- **[T010] [P] [US-1]** `src/Automation/Api/RulesEndpoints.cs` — +4 summaries
  (Publish, Archive, GetOne, DryRun). Note the two groups: Publish and Archive
  are `sse.rules.write`; GetOne and **DryRun** are `sse.rules.read` — DryRun is a
  `MapPost` on the `reads` group and is the easiest one to get wrong.
- **[T011] [P] [US-1]** `src/CameraCatalog/Api/CameraEndpoints.cs` — ~1
  (ListCameras; `sse.cameras.read`). The other four already conform.
- **[T012] [P] [US-1]** `src/EventIngestion/Api/EventsEndpoints.cs` — +3
  (IngestWebhook → `No OIDC scope:` naming the static bearer; ListEvents,
  GetEvent → `sse.events.read`), ~2 (IngestManual → `sse.events.write`;
  ListDeadLetters → `sse.events.read`).
- **[T013] [P] [US-1]** `src/EventIngestion/Api/WebhookIntegrationsEndpoints.cs`
  — ~3, all `sse.webhooks.write`.
- **[T014] [P] [US-1]** `src/LayoutComposition/Api/LayoutEndpoints.cs` — +8.
  Per-endpoint scopes, not a group scope: two reads, six writes.
- **[T015] [P] [US-1]** `src/OverlayDesigner/Api/OverlayEndpoints.cs` — +8, same
  shape as T014.
- **[T016] [P] [US-1]** `src/StreamDistribution/Api/StreamEndpoints.cs` — +1
  (AuthorizeWhep → `No OIDC scope:` naming the forwarded token the handler
  validates), ~3 (all `sse.streams.read`).
- **[T017] [P] [US-1]** `src/SystemVariables/Api/SystemVariableEndpoints.cs` —
  **+2 only** (SetValue, Archive). **Only the three writes get a scope sentence**
  (`sse.variables.write`). The three GETs stay as they are: they enforce no
  scope, they are the #2070 register rows, and adding
  `RequireAuthorization(Scope.Sse.Variables.Read)` here is **out of scope for
  this spec** and would make a behaviour-preserving change behavioural.
- **[T018] [P] [US-1]** Verify the three Identity files and `AuditEndpoints.cs`
  need **no** edit, and record that in the PR body. This is a read, not a
  change; it is a task because "already conformant" is the claim #850 was closed
  on and it should be confirmed rather than assumed.

*All of T010–T018 depend on T009 and on nothing else.*

## Verification

- **[T019] [US-1]** Re-run `Architecture.Tests`. Green, with the guard's own
  assertions passing and the six neighbouring guards that read these files
  passing **unmodified**.
  *Depends on: T010–T018.*

- **[T020] [US-1]** Run the spec's independent end-to-end procedure, steps 1–6.
  Step 6 is load-bearing: `git diff src/` must touch only `.WithSummary`
  arguments and comments.
  *Depends on: T019.*

  **Done when:** the diff over `src/` contains no `RequireAuthorization`, no
  route template, no handler and no `Produces` change, and the three grep
  figures re-measure as **56 endpoints / 55 summaries / 51 `Required scope:`**.

  Neither shortfall is a miss. The one endpoint left without a summary is `GET
  /system-variables/snapshot`, and the five without the scope sentence are the
  three #2070 GETs plus the two anonymous endpoints, which carry `No OIDC
  scope:` instead. 51 + 3 + 2 = 56.

## Board

Per CLAUDE.md's corrected Phase 3 gate, **no per-task issues**. #2087 is the
feature-level issue; add it to Project #13 if it is not already there:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2087
```

Verify with `--limit 2000` — `item-list` defaults to 30 and a filled board looks
empty without it.

## Blocked / needs a decision before phase 4

1. ~~**#850's framing.**~~ **Resolved.** #850 was closed as delivered for its
   Identity label; the work is now #2087, and every reference in these artefacts
   points there.
2. ~~**Branch prefix.**~~ **Resolved.** Phase 4 runs on
   `test/2087-an-endpoint-names-the-scope-it-needs`, following spec 068.
3. **No ADR is required and none may be written here** (ADR-0144). ADR-0139
   already states the build-failing-rule preference and ADR-0070 fixes the
   endpoint style; this spec implements existing decisions and makes none.
