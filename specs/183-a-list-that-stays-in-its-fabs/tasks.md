# Tasks 183 — A list that stays in its fabs

**Spec:** `specs/183-a-list-that-stays-in-its-fabs/spec.md`
**Plan:** `specs/183-a-list-that-stays-in-its-fabs/plan.md`
**Issue:** #2281 — **already on Project #13, status Todo**, labels `bug` +
`agent:ready`, no `agent:blocked`. Verified 2026-09-18 by a GraphQL read of the
issue's own `projectItems` (project number **13**, Status **Todo**). **No
`item-add` needed.**
**Branch:** `2281-list-endpoints-fab-scope`, cut from `origin/develop` at
`10c96740`, in the worktree `D:/Github/sse-2281`.

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED — and the red is another fab's data in the body**

Behaviour-changing, and a data-disclosure defect. No ambiguity to resolve; the
rule would resolve it to red regardless.

**The red that counts is not a missing filter and not a status code.** These
endpoints answer `200` before the fix and `200` after it. A test asserting a
status here **cannot fail**, and an assertion that cannot fail has been this
session's most frequent test defect.

The evidence quoted in the PR must be a **Dresden `clientId` present in the body
of a Munich-scoped `GET /kiosks` with no `fabId`** — and the same for `/devices`
and `/webhook-integrations`. Assert on content:

- the dresden client id is **not** in the response, and
- the set of distinct `fab` values in the response is exactly `{"munich"}`.

Both go red today for the right reason, and neither can be satisfied by anything
except the rows actually changing.

**A green-on-arrival test here is a phase-4 failure, not a shortcut** (ADR-0139,
constitution §Testing). If I1, I2 or I3 passes before the fix, stop: the test is
not reaching the disclosure, which is confirmed present by reading (`plan.md`
§2). Do not adjust the assertion to make it red — diagnose why the dresden row
is absent (wrong token, wrong fab, client not actually enrolled).

**I4 must be written even though it passes today.** It is the over-narrowing
guard: a fix that resolved a *single* fab instead of the caller's fab **set**
would close the disclosure and break every multi-fab operator, and would ship
green without it. It is not a characterisation exemption — the spec is still
RED as a whole; I4 is one non-regression case inside it.

### Engineer: `backend-engineer` (phase 4b)

Phase 4a is `test-writer`, which writes and runs the tests, observes the red, and
returns the **verbatim** output. `backend-engineer` receives that output as its
brief and **may not edit the tests to pass**. C#/.NET across Application and Api
in one bounded context is exactly that brief's scope. No frontend work exists
(zero callers — spec §7 A2), so no `frontend-engineer`. No infrastructure work
exists (no migration, no AppHost change, no realm edit), so no `infra-engineer`.

`test-adversary` is **not** added. The adversarial case *is* the primary case
here, and the one edge worth naming — the multi-fab caller — is I4, already
mandated above.

### Reviewers at phase 6: **`security-reviewer` AND `backend-reviewer`**

**`security-reviewer` is required, not conditional, and the reasoning is
stronger here than it was for spec 180.** ADR-0037's phase-6 row calls for
`/security-review` when a change is security-sensitive. Spec 180 was a
cross-fab *write*; this is a cross-fab **read** — a confidentiality defect whose
whole content is which bytes reach which principal. Three considerations make
the review non-negotiable:

1. **The disclosure is the deliverable.** Nothing else in the diff matters if a
   row still crosses a fab boundary, and the only way to know is to reason about
   the authorization path, not about C# correctness.
2. **This is the third member of one defect class** (#2240 → spec 180, #2280 →
   spec 182, #2281 → here). The first two were found only because someone looked
   at the authorization path deliberately. A `backend-reviewer` reading for DDD
   and EF correctness would pass a diff that narrows to one fab instead of the
   caller's set.
3. **The change alters two refusal statuses** (`400 → 403` for a malformed
   `fabId`; `200`-with-everything → `403` for a zero-fab caller). Both are
   deliberate, both are alignments with six existing sites, and both are exactly
   the kind of thing that must be *reviewed as a decision* rather than noticed
   later as a regression.

`security-reviewer` is asked to answer, in writing:

1. Can any row from a fab the caller does not hold still reach the response —
   through any of the three endpoints, with or without `fabId`?
2. Does the fix narrow to the caller's fab **set**, not to a single fab? (I4.)
3. Is the resolution the **first** thing the endpoint does, so no refusal or
   ordering difference discloses what exists in another fab?
4. Is the `400 → 403` change for a malformed `fabId` (spec AS-5) correct, and
   does it match `CameraEndpoints.List`?
5. Is the zero-fab `403` correct here, and does it affect any real principal?
6. Anything in the diff that widens beyond #2281 into #2240 / #2280 territory,
   or that touches `src/ServiceDefaults/`.

`backend-reviewer` covers the rest: boundary rules (no cross-context reference),
`Ensure.That` guards (ADR-0105), `Result`/`Option` usage and the ADR-0141
question the removed `Option<T>` raises, EF translation of
`fabs.Contains(client.Fab)`, handler deconstruction, collection-expression style,
ADR-0084 metrics, and the doc comments the change falsifies.

### The counterfactual, stated

| Run | Code | Expected |
|---|---|---|
| **Run A** | unfixed `src/` | I1, I2, I3 **fail**, each showing a dresden `clientId` (and `"fab":"dresden"`) inside a munich-scoped `200` body. I4, I5, I6 pass. Output quoted verbatim. |
| **Run B** | after the fix | I1-I6 all pass; distinct `fab` values are `{munich}` for the munich caller and `{munich, dresden}` for `op-multi`; `Architecture.Tests` and the full `Identity.*` suites green; **no test edited** from Run A's versions. |

Run A is the gate. A PR without Run A's verbatim output has not met phase 4.

### No new ADR expected — and what to do if that is wrong

Every decision in play is already made: ADR-0114 (the `FabResolution` decision
table, read half), ADR-0044 (per-context `FabIdentifier`), ADR-0070 (Minimal
APIs), ADR-0047/0089 (errors), ADR-0105 (guards), ADR-0141 (`Option<T>`, and why
one legitimately disappears — `plan.md` §3), ADR-0103 (Aspire fixture), ADR-0139
(observed red), ADR-0036 (smallest change).

**Spec §7 A1 marks the one judgement**: ADR-0114's "not a general licence" clause
is about *inference* on the write path, which selects a fab; the read half only
narrows to fabs the caller already holds and cannot grant anything. If phase 4 or
6 concludes an ADR **is** owed — or that Identity should adopt `FabResolution`'s
*write* half too — that is a **blocked** outcome: stop, comment with the
reasoning, label `agent:blocked`, move to the next issue. Do not decide it in
passing. The lane may not write ADRs.

### Files phase 4 may touch

**Exactly these, and no others:**

*Production (4b only)*

1. `src/Identity/Api/IdentityFabResolution.cs` — **new**; one copy of
   `ResolveReadFabsAsync` for all three endpoint files, modelled on
   `src/EventIngestion/Api/EventIngestionFabResolution.cs`, taking the
   endpoint's error-code prefix as a parameter
2. `src/Identity/Api/KiosksEndpoints.cs` — `List` only. **`Enroll` and `Disable`
   are not touched.**
3. `src/Identity/Api/DevicesEndpoints.cs` — `List` only
4. `src/Identity/Api/WebhookRotationEndpoints.cs` — `List` only. **`Rotate` is
   #2280's territory and is not touched.**
5. `src/Identity/Application/Queries/ListKiosksQuery.cs` — `Option<FabIdentifier> Fab`
   → `IReadOnlyList<FabIdentifier> Fabs`, doc comment corrected
6. `src/Identity/Application/Queries/ListDevicesQuery.cs` — same
7. `src/Identity/Application/Queries/ListWebhookClientsQuery.cs` — same
8. `src/Identity/Application/Queries/Handlers/ListKiosksQueryHandler.cs`,
   `ListDevicesQueryHandler.cs`, `ListWebhookClientsQueryHandler.cs` — pass the
   set through
9. `src/Identity/Application/Queries/Handlers/RegisteredClientProjection.cs` —
   the unconditional `Where`, plus the doc comment that currently says "optional"

*Tests (4a only)*

10. `tests/Integration.Tests/Identity/CrossFabListIntegrationTests.cs` — **new**
11. `tests/Identity.Application.Tests/Queries/ListKiosksQueryHandlerTests.cs`
12. `tests/Identity.Application.Tests/Queries/ListDevicesQueryHandlerTests.cs`

*Artefact*

13. `specs/183-a-list-that-stays-in-its-fabs/verification.md` — new, phase 5

**Forbidden, explicitly:**

- `src/ServiceDefaults/**` — `FabResolution` is used, never modified. A change
  here would touch nine contexts under an issue scoped to one.
- `src/Identity/Domain/**` and `src/Identity/Infrastructure/**` — no repository
  method, no EF configuration change, **no migration**; `fab` is an existing,
  populated, indexed column.
- `src/Identity/Application/DTOs/RegisteredClientSummaryDto.cs` — fewer rows,
  same rows (spec §7 A4).
- The `Disable` handlers and their commands — **#2240 / spec 180, merged.**
- `WebhookRotationEndpoints.Rotate` and `RotateWebhookClientCommand*` —
  **#2280 / spec 182, PR #2455 unmerged.** Touching them here guarantees a
  conflict with a parked branch.
- `src/AppHost/Realms/smart-sentinel-eye-realm.json` — every principal the tests
  need is already seeded (`admin@munich.test`, `op-dresden@dresden.test`,
  `op-multi@smart-sentinel-eye.test`, `op-berlin@berlin.test`). Editing it needs
  the Keycloak **volume deleted**; a restart keeps the old realm and the stack
  still looks perfectly healthy.
- `tests/Integration.Tests/Identity/RegisteredClientConcurrencyIntegrationTests.cs`
  — both its list calls (`:214`, `:230`) already send `?fabId=`, so it needs no
  edit. Run it; do not edit it. Red there is a finding.
- `tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs` — keys on
  paths, which do not change.
- Any `Shared.Contracts` message, any new project reference, any `apps/` file.

### Stack

**No Aspire stack is running** (checked 2026-09-18: no AppHost process). Phase 4a
must boot one — and check first that a sibling worktree has not booted one in the
meantime. **One machine, one Aspire stack**: a concurrent boot gives
`FailedToStart` that reads exactly like a code defect.

- Stop the stack before any `dotnet build`, or MSB3027 will look like a broken
  build; and a stack booted before this branch's commit serves the **old**
  binaries, so a manual check against it proves nothing.
- Mint tokens from Aspire's **proxied** Keycloak endpoint
  (`CreateAuthenticatedClientAsync` does this), never the container's mapped
  port, or everything 401s on issuer mismatch.
- The first integration run after machine churn can look exactly like a
  regression. Run it twice before reporting a failure that is not the expected
  red.

---

## Tasks

`[P]` marks tasks owning disjoint files that may run in parallel.

### Phase 4a — `test-writer`

- **[T001] [US1]** Write `tests/Integration.Tests/Identity/CrossFabListIntegrationTests.cs`
  with **I1-I6** (`plan.md` §8). Model it on
  `tests/Integration.Tests/Identity/CrossFabDisableIntegrationTests.cs` and reuse
  its helper shapes (`MunichClientAsync`, `EnrollKioskAsync`, `RegisterDeviceAsync`,
  `ProblemTitleAsync`). Add a dresden enrolment helper and an `op-multi` client.
  Prefix every created id with `s183-`. Real Keycloak clients are minted — do not
  wipe rows. Sentence-style names (ADR-0053), Shouldly, and every assertion
  carrying a `because` that says what a failure would **mean**, not what it
  checked. **Assert on body content, never on the 200.**
- **[T002] [P] [US1]** Update `ListKiosksQueryHandlerTests` and
  `ListDevicesQueryHandlerTests` to the new query shape (**10 constructions** —
  4 in the kiosks file, 6 in the devices file, counted, not estimated),
  and add: a fab outside the list is excluded; a two-fab list returns rows from
  both; the `ClientKind` filter still excludes other kinds when the fab matches.
  *Disjoint from T001 — different project, different files.* **Do not** create
  `ListWebhookClientsQueryHandlerTests` (`plan.md` §8 — I3 covers it; add it only
  if phase 4b measures the ADR-0065 Application gate short).
- **[T003] [US1]** **Run A.** Boot the stack, execute T001 and T002 against
  unfixed `src/`. Observe I1/I2/I3 failing **with a dresden `clientId` inside a
  munich-scoped body**, and I4/I5/I6 passing. Return the **verbatim** output.
  This is the phase-4 gate; a green I1-I3 blocks. *Depends on T001, T002.*

### Phase 4b — `backend-engineer` (brief = T003's verbatim output)

- **[T004] [US1]** Add `src/Identity/Api/IdentityFabResolution.cs` — one internal
  static `ResolveReadFabsAsync(user, fabId, fabGuard, unusableFabErrorCode, ct)`,
  body copied from `EventIngestionFabResolution:72-105` including the **per-entry**
  parse and the `Count == 0` → 400 fallthrough. Doc comment says why Identity gets
  a file rather than three private copies (three endpoint files; drift here is a
  tenancy hole). *Blocks T005.*
- **[T005] [US1]** Rewrite the three `List` handlers to call it, taking
  `[FromQuery] string fabId = ""` and passing their existing error-code prefix
  (`KIOSK_` / `DEVICE_` / `WEBHOOK_INVALID_INPUT`). **Resolution first, before
  anything else is evaluated.** No pre-parse of `fabId` — the raw string goes to
  the resolver, which is what produces AS-5's deliberate 403. Touch nothing else
  in these three files.
- **[T006] [US1]** Swap `Option<FabIdentifier> Fab` for
  `IReadOnlyList<FabIdentifier> Fabs` on the three query records and pass it
  through the three handlers. Correct each doc comment — "optionally filtered to a
  single fab" is false afterwards.
- **[T007] [US1]** `RegisteredClientProjection.ListAsync` takes the fab set and
  filters **unconditionally**: `.Where(client => client.Kind == kind &&
  fabs.Contains(client.Fab))`. `Ensure.That(fabs).IsNotNull()`. Correct the class
  doc comment, which currently promises an optional fab.
- **[T008] [US1]** **Run B.** Re-run T001, T002, plus `Architecture.Tests` and the
  full `Identity.*` suites. All green, **no test file edited** from T003's
  versions. Confirm `fabs.Contains(...)` translated against real Postgres rather
  than throwing.

### Phase 5 — verification

- **[T009] [US1]** Run `spec.md` §5 by hand against a **freshly booted** stack:
  enrol in dresden, list as munich (dresden row absent), list as `op-multi` (both
  present, berlin absent), `?fabId=dresden` as munich → 403. Write
  `specs/183-a-list-that-stays-in-its-fabs/verification.md`, quoting the actual
  response bodies. **Latency: N/A — Identity administrative read path, not on the
  event→overlay path.** Say so explicitly rather than omitting it. Record every
  figure observed, in the file — a measurement reported only to the orchestrator
  is invisible to every later grep and reviewer.

### Phase 6 — review

- **[T010] [P]** `/security-review` via **`security-reviewer`**, answering the six
  numbered questions above in writing. **Required, not conditional.**
- **[T011] [P]** `/code-review` via `backend-reviewer`.

### Phase 7

- **[T012]** PR to **`develop`** (`gh pr create --base develop`), Conventional
  Commits, **no `Co-Authored-By` footer** (ADR-0086 overrides the session
  attribution reminder; the PR *body* still gets the Claude Code lines). Body
  quotes T003's verbatim Run A and T008's Run B, states **Phase 4a colour = RED**
  with the body-content evidence, names the two deliberate status changes (AS-5,
  AS-6), and closes **#2281** with a closing keyword — then the issue state is
  checked after the merge, because a mention alone closes it about one time in
  three. Rebase-merge only (ADR-0087). Park, do not wait for CI.

---

## Dependency graph

```
T001 ─┐
T002 ─┴─> T003 (Run A, GATE) ─> T004 ─> T005 ─┐
                                       T006 ─┼─> T008 ─> T009 ─> T010 ┐
                                       T007 ─┘                  T011 ┴─> T012
```

T005, T006 and T007 are written as one sequence rather than three parallel
branches because **T006 breaks the build until T007 lands** — the projection's
signature is the other end of the query records' change. They are one edit in
three files, not three edits.

**Parallelism is genuinely limited, and that is a property of the code sharing,
not a failure to decompose.** One bounded context, nine production files that all
move together through one shared projection, and a 4a→4b gate that is sequential
by design. T001 ∥ T002 and T010 ∥ T011 are the only real fan-out.

**Nothing outside this spec is blocked.** There is no foundational
`Shared.Kernel` / `Shared.Contracts` / AppHost work and `ServiceDefaults` is not
touched, so the orchestrator can run other issues alongside this one freely —
with one caveat: **spec 182 (#2280, PR #2455) touches
`WebhookRotationEndpoints.cs`**. That file is on this spec's list for its `List`
method only, and 182 owns `Rotate`. If 182 merges first, rebase this branch
before phase 4b; if this merges first, 182 must rebase. Do not resolve that by
editing the other spec's method.
