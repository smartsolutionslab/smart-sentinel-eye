# Tasks 180 — A disable that checks the fab

**Spec:** `specs/180-a-disable-that-checks-the-fab/spec.md`
**Plan:** `specs/180-a-disable-that-checks-the-fab/plan.md`
**Issue:** #2240 — **already on Project #13, status Todo**, label `agent:ready`.
Verified by `content.url` against a `--limit 2000` dump (the number filter returns
zero). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED — explicitly, and the red is an attack succeeding**

Behaviour-changing, and security-relevant. There is no ambiguity to resolve, but
the rule would resolve it to red anyway.

**The red that counts is not a missing 403 — it is a 200.** Against unfixed
`src/`, the integration tests I1-I3 must be observed with an operator scoped to
**dresden** successfully disabling a **munich** kiosk and a **munich** device.
The failure output quoted in the PR must show a `200 OK`, not merely a
`ShouldBe` mismatch on a status the endpoint never produced.

**I5 is what makes I1/I2 more than a status check.** It reads the victim client
back from the **Keycloak Admin API** (via `RealmProbe.AuthorisedAdminClientAsync`)
and asserts `enabled == true`. Against unfixed code it reads `false` — the wall
is dark. That is the availability defect observed, not argued. Reading our own
`registered_clients` row instead would be worthless: spec 121 is the proof that
our row and Keycloak can disagree.

**A green-on-arrival security test here is a phase-4 failure, not a shortcut**
(ADR-0139, constitution §Testing). If any of I1-I3 or I5 passes before the fix,
stop: the test is not reaching the vulnerability, and the vulnerability is
confirmed present by reading (`plan.md` §2).

### Engineer: `backend-engineer` (phase 4b)

Phase 4a is `test-writer`, which writes and runs the tests, observes the red,
and returns the **verbatim** output. `backend-engineer` receives that output as
its brief and **may not edit the tests to pass**. C#/.NET across Domain,
Application, Infrastructure and Api in one context is exactly that brief's scope.

`test-adversary` is **not** added. The adversarial case *is* the primary case
here, and I2 (the attacker naming their own fab) is the edge the defect actually
hides behind.

### Reviewer at phase 6: **`security-reviewer`, and `backend-reviewer`**

**Both, and `security-reviewer` is not optional.** ADR-0037's own table requires
`/security-review` when a change is security-sensitive; a cross-fab authorization
bypass on a write path is squarely that. Recorded here so phase 6 cannot be
skipped or under-scoped by a later agent reading only `tasks.md`.

`security-reviewer` is asked specifically to answer, in writing:

1. Does the fix close **AS-3** (attacker names their own fab) and not only AS-2?
   A guard without a fab-scoped lookup closes half the hole and reads as a fix.
2. Is the refusal ordering correct — scope, then `fabId` parse, then guard, then
   `clientId` parse — so that no 400 or 404 confirms another fab's client exists?
3. Does 404-not-403 hold for AS-3, matching `CameraEndpoints.cs:104-106`?
4. Is `DisabledAt == null` still in the new lookup, so re-registration after
   disable still works and a double-disable still 404s?
5. Anything in the diff that widens beyond #2240 into #2280 / #2281 territory.

`backend-reviewer` covers the rest: DDD/boundary correctness, `Ensure.That`,
`Result`/`Option`, EF translation of the new predicate, ADR-0084 metrics.

### The cross-fab attack counterfactual, stated

| Run | Code | Expected |
|---|---|---|
| **Run A** | unfixed `develop` | I1 **200**, I2 **200**, I3 **200/200**, I5 reads `enabled: false`. Quoted verbatim. |
| **Run B** | after the fix | I1 403 `RESOURCE_FAB_NOT_AUTHORIZED`, I2 404 `KIOSK_NOT_FOUND`, I3 403/404, I5 reads `enabled: true`, I4 still 200. |

Run A is the gate. A PR without Run A's output quoted has not met phase 4.

### No new ADR expected

This applies an established pattern to two routes that were missed. Every
decision in play is already made: ADR-0114 (the guard, and that inference stays
scoped to Automation — which is *why* `fabId` is required here rather than
inferred), ADR-0041 (repository contract), ADR-0047/0089 (errors), ADR-0105
(guards), ADR-0044 (per-context `FabIdentifier`), ADR-0091 (`GetWithinFabAsync`
reuses the established name rather than coining a synonym), ADR-0103 (Aspire
fixture), ADR-0139 (observed red), ADR-0036 (smallest change). The lane may not
write an ADR in any case.

If phase 4 or 6 concludes an architectural decision *is* needed — for example
that Identity should adopt `FabResolution` wholesale — that is a **blocked**
outcome: stop, comment, label `agent:blocked`. Do not decide it in passing.

### Files phase 4 may touch

**Exactly these, and no others:**

*Production (4b only)*

1. `src/Identity/Api/KiosksEndpoints.cs` — `Disable` signature + guard; and the
   stale route-table comment at lines 33-38
2. `src/Identity/Api/DevicesEndpoints.cs` — same, comment at lines 33-38
3. `src/Identity/Application/Commands/DisableKioskCommand.cs` — add `Fab`
4. `src/Identity/Application/Commands/DisableDeviceCommand.cs` — add `Fab`
5. `src/Identity/Application/Commands/Handlers/DisableKioskCommandHandler.cs`
6. `src/Identity/Application/Commands/Handlers/DisableDeviceCommandHandler.cs`
7. `src/Identity/Domain/RegisteredClient/IRegisteredClientRepository.cs`
8. `src/Identity/Infrastructure/Persistence/RegisteredClientRepository.cs`

*Tests (4a only, except T008 which 4b must fix)*

9. `tests/Integration.Tests/Identity/CrossFabDisableIntegrationTests.cs` — new
10. `tests/Identity.Application.Tests/Commands/DisableKioskCommandHandlerTests.cs`
11. `tests/Identity.Application.Tests/Commands/DisableDeviceCommandHandlerTests.cs`
12. `tests/Identity.Infrastructure.Tests/` — one new file for `GetWithinFabAsync`
13. `tests/Integration.Tests/Identity/RegisteredClientConcurrencyIntegrationTests.cs`
    — **line 54 only**: `DELETE /devices/{clientId}` gains `?fabId=munich`. This is
    the **only** existing HTTP caller of either route in the whole repo (measured,
    twice, by two patterns).

*Artefact*

14. `specs/180-a-disable-that-checks-the-fab/verification.md` — new, phase 5

**Forbidden, explicitly:**

- `src/Identity/Api/WebhookRotationEndpoints.cs` — **#2280's territory**
- the three `List` handlers and their queries (`ListKiosksQuery`,
  `ListDevicesQuery`, and the webhook listing) — **#2281's territory**
- anything under `src/ServiceDefaults/` — the guard is used, never modified
- `src/AppHost/Realms/smart-sentinel-eye-realm.json` — the realm already seeds
  every principal these tests need; editing it requires deleting the volume
  (a restart keeps the old realm and the stack still looks healthy)
- `src/Identity/Infrastructure/Migrations/` — `fab` is an existing column
- `tests/Architecture.Tests/ConcurrencyConflictDeclarationTests.cs` — the route
  register keys on paths, which do not change. Run it; do not edit it. If it goes
  red, that is a finding, not a file to adjust.
- any `Shared.Contracts` message, any new project reference

### Stack

The Aspire stack (**pid 3312**) is running and **must be left running**. The
integration tests need it (ADR-0103: no Testcontainers). Two consequences:

- `dotnet build` can fail **MSB3027** while the stack holds Identity's binaries.
  If that happens, report it — do **not** stop the stack.
- One machine, one Aspire stack. Do not boot a second; a concurrent boot gives
  `FailedToStart` that reads exactly like a code defect.

Mint tokens from Aspire's **proxied** Keycloak endpoint (`CreateKeycloakClient`
does this), never the container's mapped port, or everything 401s on issuer.

---

## Tasks

`[P]` marks tasks owning disjoint files that may run in parallel.

### Foundational — blocks everything else

- **[T001] [US1]** `IRegisteredClientRepository.GetWithinFabAsync(ClientId,
  FabIdentifier, CancellationToken)` + its implementation in
  `RegisteredClientRepository`, mirroring `CameraRepository.GetWithinFabAsync:21-33`
  (fab in the predicate) **and keeping** `GetByClientIdAsync`'s
  `DisabledAt == null` clause. `GetByClientIdAsync` stays — enrolment and
  registration still need the global duplicate check.
  *Blocks T004, T005. Nothing else in the repo is blocked; this is one context.*

### Phase 4a — `test-writer`

- **[T002] [US1]** Write `tests/Integration.Tests/Identity/CrossFabDisableIntegrationTests.cs`
  with I1-I5 (`plan.md` §8). Model it on
  `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`.
  Enrol a fresh client per test (real Keycloak clients are minted; do not wipe
  rows). Sentence-style names (ADR-0053), Shouldly, every assertion carrying a
  `because` that says what a failure would mean.
- **[T003] [P] [US1]** Extend `DisableKioskCommandHandlerTests` and
  `DisableDeviceCommandHandlerTests` with the another-fab case, the same-fab case,
  and an assertion that names the kind check as what refuses a device through the
  kiosk route. *Depends on T001 for the command shape to compile; write against
  the signature T001 declares.*
- **[T004] [P] [US1]** `tests/Identity.Infrastructure.Tests` — `GetWithinFabAsync`
  returns `None` for another fab's row and `None` for a disabled row in the right
  fab. *Depends on T001.*
- **[T005] [US1]** **Run A.** Execute T002 against unfixed `src/`. Observe
  I1/I2/I3 answering **200** and I5 reading `enabled: false`. Return the
  **verbatim** output. This is the phase-4 gate; a green run here blocks.

### Phase 4b — `backend-engineer` (brief = T005's verbatim output)

- **[T006] [US1]** `DisableKioskCommand` and `DisableDeviceCommand` gain
  `FabIdentifier Fab`. No new error variant.
- **[T007] [US1]** Both handlers switch to `GetWithinFabAsync(command.ClientId,
  command.Fab, …)`, keeping the `Kind` check. Deconstruct the command first
  (two fields read → the house rule applies).
- **[T008] [US1]** Both `Disable` endpoint handlers gain `ClaimsPrincipal user`,
  `[FromServices] IFabAuthorizationGuard fabGuard` and a required
  `[FromQuery] string fabId`, in the order `plan.md` §6 fixes: parse `fabId` →
  400; `EnsureAccessAsync` → 403; **then** parse `clientId` → 400; then the
  handler. Mirror `Enroll`/`Register` in the same file exactly. Update
  `RegisteredClientConcurrencyIntegrationTests:54` to send `?fabId=munich`.
- **[T009] [US1]** Correct the route-table comments in both files (SC-004): the
  route no longer "runs no fab guard", and 403 now has two producers. Say what
  changed and why 404 rather than 403 answers another fab's client.
- **[T010] [US1]** **Run B.** Re-run T002, T003, T004 plus `Architecture.Tests`
  and the full `Identity.*` suites. All green, and **no test edited** from T005's
  versions except T008's one-line `?fabId=munich`.

### Phase 5 — verification

- **[T011] [US1]** Run `specs/…/spec.md` §5 by hand against the live stack: enrol,
  attack from dresden both ways, confirm the Keycloak client is still enabled,
  then disable legitimately from munich. Write
  `specs/180-a-disable-that-checks-the-fab/verification.md`. **Latency: N/A —
  Identity admin write path, not on the event→overlay path.** Say so explicitly
  rather than omitting it.

### Phase 6 — review

- **[T012] [P]** `/security-review` via **`security-reviewer`**, answering the
  five numbered questions above in writing. **Required, not conditional.**
- **[T013] [P]** `/code-review` via `backend-reviewer`.

### Phase 7

- **[T014]** PR to **`develop`** (`--base develop`), Conventional Commits,
  **no `Co-Authored-By` footer** (ADR-0086 overrides any session attribution
  reminder). Body quotes T005's verbatim Run A output and T010's Run B, states
  Phase 4a colour = RED, and closes #2240 with a closing keyword — then the issue
  state is checked after the merge, because a mention alone rarely closes it.

---

## Dependency graph

```
T001 ──┬─> T003 ─┐
       └─> T004 ─┤
T002 ────────────┼─> T005 (Run A, GATE) ─> T006 ─> T007 ─> T008 ─> T009 ─> T010
                 │                                                          │
                 └──────────────────────────────────────────────────────────┴─> T011 ─> T012 ┐
                                                                                      T013 ┴─> T014
```

**Parallelism is genuinely limited here, and that is a property of the change,
not a failure to decompose.** One bounded context, eight production files that
all move together, and a 4a→4b gate that is sequential by design. T003 ∥ T004 and
T012 ∥ T013 are the only real fan-out. There is no foundational
`Shared.Kernel` / `Shared.Contracts` / AppHost work, so **nothing outside this
spec is blocked** and the orchestrator can run other issues alongside it freely.
