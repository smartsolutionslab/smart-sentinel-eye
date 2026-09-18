# Tasks 182 — A rotation that cannot cross a fab

**Spec:** `specs/182-a-rotation-that-cannot-cross-a-fab/spec.md`
**Plan:** `specs/182-a-rotation-that-cannot-cross-a-fab/plan.md`
**Issue:** #2280 — **already on Project #13, status Todo**, label `agent:ready`.
Verified 2026-09-18 by `content.url` against a `--limit 2000` dump (the number
filter returns zero). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED — and the red is an attack succeeding, not a missing status**

Behaviour-changing and security-relevant. There is no ambiguity, and the rule
would resolve it to red anyway.

**Two reds must be observed, and neither is a `ShouldBe` mismatch on a status the
code never produced:**

1. **US1 / AS-4.** A munich-scoped operator rotating a **dresden** integration
   with `{"fabId":"munich"}` must be seen returning **`200 OK` with a
   `clientSecret` in the body**, and Dresden's Keycloak client secret must be
   read back **changed**. That is credential disclosure plus credential
   destruction, observed rather than argued.
2. **US2 / AS-5.** A munich-scoped **first** rotation naming an unrotated dresden
   integration must be seen leaving that integration at
   `ValidationMode == Jwt` with `KeycloakClientId` pointing at the attacker's
   client. That is the takeover, observed.

**If either passes before the fix, stop.** The test is not reaching the
vulnerability, and both vulnerabilities are confirmed present by reading
(`plan.md` §1, `spec.md` §1). A green-on-arrival security test is a phase-4
failure, not a shortcut (ADR-0139, constitution §Testing).

**One deliberate exception to "tests are not edited", declared here in advance.**
`tests/EventIngestion.Application.Tests/EventHandlers/WebhookIntegrationRotatedV1HandlerTests.cs`
builds its `EventMetadata` with `Fab: null`, which no production publisher ever
sends. `test-writer` corrects that **setup value** to the integration's fab as
part of T005. Assertions are untouched. `backend-engineer` may **not** make any
further edit to any test file.

### Phase 4 agents

- **4a — `test-writer`.** Writes the tests, runs them, observes the two reds, and
  returns the **verbatim** output. That output is `backend-engineer`'s brief and
  is quoted in the PR body.
- **4b — `backend-engineer`.** C#/.NET across Domain, Application and
  Infrastructure in two contexts. **May not edit the tests to pass.**
- **`test-adversary` is not added.** The adversarial case *is* the primary case
  here — AS-4 (attacker names their own fab) and AS-5 (attacker uses the create
  branch) are exactly the edges the defect hides behind, and both are written by
  `test-writer` as first-class scenarios.
- No `frontend-engineer`: no frontend caller exists (spec §8 A4; re-measure
  before relying on it).

### Phase 6 reviewers: **`security-reviewer` AND `backend-reviewer`**

**Both, and `security-reviewer` is not optional.** ADR-0037's table requires
`/security-review` when a change is security-sensitive; a cross-fab authorization
bypass on a write path that returns a credential is squarely that. Recorded here
so phase 6 cannot be skipped or under-scoped by a later agent reading only
`tasks.md`.

`security-reviewer` is asked to answer, in writing:

1. Does US1 close **AS-4** (attacker names their *own* fab) and not merely AS-3?
   A guard without a fab-scoped lookup closes half the hole and reads like a fix.
2. Is AS-4's 412 body **byte-identical in shape** to AS-9's unknown-name 412 — no
   field, message or timing that distinguishes "exists in another fab"?
3. Does US2 refuse a **null** `Metadata.Fab`, and is the refusal a log-and-return
   rather than a throw (which would dead-letter and retry)?
4. Does the new log message avoid naming the *victim's* fab (`plan.md` §5)?
5. Is spec §3.1's residual (the name-squat) correctly characterised as
   denial-of-rotation rather than takeover — i.e. is there any path by which the
   squatted munich client can be used to read or forge dresden traffic?

`backend-reviewer` is asked to confirm the Identity diff really is one production
line plus a comment correction, that the new EventIngestion repository method
mirrors `RegisteredClientRepository.GetWithinFabAsync` in predicate shape and
doc-comment intent, and that nothing new was introduced where an existing pattern
was available (ADR-0036).

### Files this feature may NOT touch

Any change to these is out of scope and must be raised as a scope question, not
made:

- `src/Identity/Api/WebhookRotationEndpoints.cs` → the `List` method, and
  `ListWebhookClientsQueryHandler` — **#2281**.
- `src/Identity/Application/Queries/**` — the three list handlers, same issue.
- `RequireScopeExtensions` / `LegacyManagementBundle` / the realm's client scopes
  — the console-bundle issue.
- `EnrollKioskCommandHandler.cs`, `RegisterDeviceCommandHandler.cs` — same
  method, different defect class (spec §3).
- `src/Shared.Contracts/**` — no contract change is needed and none is permitted
  here (a `V2` would be a decision).
- `docs/adr/**`, `.specify/memory/constitution.md` — the lane may not write an
  ADR or amend the constitution.
- Any migration under `src/*/Infrastructure/Migrations/` — no schema change.

---

## Dependency shape (for the orchestrator's fan-out)

There is **no foundational task**. Nothing in `Shared.Kernel`, `Shared.Contracts`
or `AppHost` changes, so nothing blocks anything else across contexts.

```
US1 (Identity)       T001 ─ T002 ─ T003 ─┐
                                          ├─ T007 ─┬─ T008 [P] ─┐
US2 (EventIngestion) T004 ─ T005 ─ T006 ─┘         └─ T009 [P] ─┴─ T010 ─ T011 …
                     └──────── [P] with the US1 column ────────┘
```

`[P]` marks tasks that own **disjoint files** (ADR-0109). US1 touches only
`src/Identity/**` + `tests/Identity.*` + one integration-test file; US2 touches
only `src/EventIngestion/**` + `tests/EventIngestion.*` + a different
integration-test file. **The two stories can be built in parallel by two
engineers, or in either order by one.** They land in **one PR** — see spec §4 for
why splitting them would ship a half-fix.

Within a story, phase 4a (tests) strictly precedes phase 4b (source). The
`──` arrows above are that ordering, not a suggestion.

---

## Phase 4a — tests first, observed RED (`test-writer`)

### US1 — Identity

- **[T001] [P] [US1]** Add unit coverage to
  `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`
  for **AS-4**: a `RegisteredClient` seeded in fab `dresden` at version 7, a
  command carrying fab `munich` and `ExpectedVersion = Some(7)`. Assert the
  result is a failure of variant `WebhookClientNotFound`, that
  `IKeycloakAdminClient.RotateClientSecretAsync` was **never** called (Moq
  `Verify(..., Times.Never)`), and that no `WebhookIntegrationRotatedV1` was
  published. Use `InMemoryRegisteredClientRepository.Seed(...)` — it already
  models the version bump.
  **Red:** today the handler succeeds and the Keycloak mock is called.

- **[T002] [P] [US1]** In the same file, add the **AS-9 twin**: the identical
  command against a repository holding **nothing**, asserting the *same*
  `WebhookClientNotFound` variant with the *same* message. The two assertions
  must be written so that a future divergence between them fails — this is the
  test that the answer carries no fab information. (This one is green today and
  is a characterisation guard, not part of the red.)

- **[T003] [P] [US1]** Add `tests/Integration.Tests/Identity/CrossFabWebhookRotationIntegrationTests.cs`,
  modelled on `CrossFabDisableIntegrationTests.cs`. Against the `AspireFixture`:
  - **I1 (AS-4, the red):** dresden registers + rotates an integration; a
    munich-only token rotates it with `{"fabId":"munich"}` and the dresden
    version. Assert `412` and **no `clientSecret` anywhere in the body**.
  - **I2 (AS-4 evidence):** read the victim's Keycloak client secret via
    `RealmProbe.AuthorisedAdminClientAsync` **before and after** I1's request and
    assert it is **unchanged**. Reading only our `registered_clients` row is not
    sufficient evidence — spec 121 (#2165 / #2207) is the proof that our row and
    Keycloak can disagree.
  - **I3 (AS-3 regression):** same request with `{"fabId":"dresden"}` → `403`
    `RESOURCE_FAB_NOT_AUTHORIZED`. Green today; must stay green.
  - **I4 (AS-1 happy path):** dresden rotates its own → `200`, new secret,
    version incremented.
  **Red:** I1 returns `200` with a secret; I2 sees the secret change.

### US2 — EventIngestion

- **[T004] [P] [US2]** Extend
  `tests/EventIngestion.Application.Tests/EventHandlers/WebhookIntegrationRotatedV1HandlerTests.cs`
  with **AS-6**: an integration seeded in fab `dresden`, an event whose
  `EventMetadata.Fab` is `"munich"`. Assert `ValidationMode` is still
  `BearerValidationMode.StaticHash` and `KeycloakClientId` is still `null`.
  Add the **null-fab** twin with the same assertions.
  **Red:** today both flip the integration to `Jwt`.

- **[T005] [P] [US2]** Correct the **setup data** of the two existing tests in
  that file (`Flips_a_registered_integration_to_JWT_validation` and the
  unknown-integration no-op) so their `EventMetadata` carries the integration's
  fab (`"munich"`) instead of `null` — matching what the only production
  publisher sends. **Assertions are not touched.** Declared in the header above
  as the single permitted test edit; `backend-engineer` may not extend it.
  Add the `InMemoryWebhookIntegrationRepository.GetWithinFabAsync` fake
  implementation here (the fake is a test file, so it belongs to 4a), mirroring
  the production predicate exactly — fab as part of the match, not a filter
  applied after.

- **[T006] [P] [US2]** Add the AS-5 integration assertion to
  `tests/Integration.Tests/EventIngestion/` (new file
  `CrossFabWebhookRotationEffectIntegrationTests.cs`, **not** an edit to
  `WebhookBearerValidationIntegrationTests.cs` — disjoint files keep T003 and
  this one `[P]`): a dresden integration registered and **not** rotated; a
  munich-only token performs a first rotation naming it; poll the integration's
  state and assert `validation_mode` is still `StaticHash` and
  `keycloak_client_id` is still null; assert the original static bearer is still
  accepted by `POST /events/webhook/{name}`.
  **Red:** today it flips to `Jwt` and the static bearer stops working.
  *If bus timing makes this flaky, narrow the assertion — never delete one — and
  record the trade in the PR; T004 is the load-bearing evidence either way.*

- **[T007]** Run the full 4a set, capture **verbatim** output, confirm the two
  reds are the ones declared above (a `200` with a secret; a `Jwt` flip), and
  hand that output to `backend-engineer` unedited.

## Phase 4b — source (`backend-engineer`)

- **[T008] [P] [US1]** In
  `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`,
  replace `clients.GetByClientIdAsync(clientId, cancellationToken)` with
  `clients.GetWithinFabAsync(fab, clientId, cancellationToken)`. Add a short
  `why` comment naming the AS-4 case (an attacker naming their own fab) — not a
  restatement of what the line does. Correct the stale comment at `:129-131`,
  which names `GetByClientIdAsync` as the call the retry would hit; it now names
  the scoped lookup and the reasoning it records is unchanged.
  **No other production file in `src/Identity/` is touched.**

- **[T009] [P] [US2]** In EventIngestion:
  1. `IWebhookIntegrationRepository` — add `GetWithinFabAsync(FabIdentifier fab,
     WebhookIntegrationName name, CancellationToken cancellationToken)` with a
     doc comment stating why the fab is in the predicate rather than compared
     afterwards, mirroring `IRegisteredClientRepository.GetWithinFabAsync`.
  2. `WebhookIntegrationRepository` — implement it with
     `.Where(i => i.Name == name).Where(i => i.Fab == fab)`, `Ensure.That` on
     both arguments.
  3. `Log.cs` — add the `Warning`-level message from `plan.md` §5. Do **not**
     log the victim integration's fab.
  4. `WebhookIntegrationRotatedV1Handler` — bind `metadata` in the
     deconstruction (the local **must** be named `metadata`;
     `HandlerDeconstructionTests` reads source); refuse with the new log and a
     plain `return` when `metadata.Fab` is null or does not parse as a
     `FabIdentifier`; resolve through the scoped lookup. Refusals are
     log-and-return, never throw — a throw would fail the enclosing write and
     dead-letter a message that can never succeed.

- **[T010]** Re-run the full 4a set green. Then run the wider suites that have a
  stake: `Architecture.Tests` (notably `ConcurrencyConflictDeclarationTests`,
  `HandlerDeconstructionTests`, `EventMetadataFabDeclarationTests`,
  `PrimitiveBoundaryTests`), `Identity.Application.Tests`,
  `EventIngestion.Application.Tests`, and the coverage gates (Domain ≥ 90%,
  Application ≥ 80% — ADR-0065). Format and analyzers clean; Release build must
  pass with `TreatWarningsAsErrors`.

## Phase 5 — verify (`/verify`)

- **[T011]** Run `spec.md` §6 end to end against a **freshly booted** Aspire
  stack — check the AppHost process start time against the commit first; a
  persistent `dotnet run` serves the binaries it loaded at boot and will show
  pre-fix behaviour whatever is on disk. Write
  `specs/182-a-rotation-that-cannot-cross-a-fab/verification.md` recording, as
  **observed** values: the 412 body from step 4, the Keycloak secret unchanged,
  the 403 from the dresden-named variant, the legitimate 200, and the US2 state
  read-back. **Latency: N/A — cite §IV and say why** (administrative write path,
  no leg touched). Every figure written down; a measurement reported only to the
  orchestrator is invisible to every later reader.

## Phase 6 — review

- **[T012]** `security-reviewer` — the five questions above, answered in
  writing.
- **[T013]** `backend-reviewer` — minimality, pattern fidelity, boundary
  rules.
- **[T014]** Address or accept-in-writing every finding.

## Phase 7 — PR

- **[T015]** PR to `develop` (`--base develop`, ADR-0028). Body must contain:
  the **verbatim** 4a red output for both AS-4 and AS-5; the phase-5 verification
  note; `Closes #2280`; the **residual from spec §3.1** stated as a known,
  deliberate non-closure with its severity, and a recommendation that the
  orchestrator file a follow-up issue + ADR for it (the lane may not file the
  ADR itself); and the note that #2281 and the console-bundle issue remain open
  and are deliberately untouched. Conventional Commits, **no `Co-Authored-By`**
  (ADR-0086). Each commit must build on its own — rebase-merge lands them
  individually (ADR-0087).
