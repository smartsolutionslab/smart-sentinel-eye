# Tasks — Spec 206, a row no timeline can reach

**Spec:** `specs/206-a-row-no-timeline-can-reach/spec.md` ·
**Plan:** `specs/206-a-row-no-timeline-can-reach/plan.md` ·
**Issue:** #2429 · **Branch:** `2429-wrong-guid-audit-timeline` · **Base:** `origin/develop` @ `c9018d80`

**Phase-4a colour is declared per story, not per spec** (constitution §Testing, CLAUDE.md §*Phase 4a has two colours*). Do not resolve it by the ambiguity rule — it is not ambiguous here:

| Story | Colour | The task that carries the evidence |
|---|---|---|
| **US3** | **RED** | T001 — the table, written first, failing on exactly the two defects. |
| **US1** | **RED** | T001's failure **is** US1's red. T002 turns it green. |
| **US2** | **CHARACTERISATION, observed green** | T004 — the table captured green *before* the rewrite, and required to pass **unmodified** after (T006). An assertion that has to be edited means behaviour moved: **block, do not adjust.** US2's own evidence is T005/T007's counterfactual. |

`test-writer` runs the tests, returns the **verbatim** output, and that output is quoted in the PR body. The engineer receives it as its brief and **may not edit the tests to pass** (ADR-0139).

**No new ADR, no constitution amendment, no task issues.** The phase-3 gate is *the feature's issue on Project #13* (CLAUDE.md §Workflow) — #2429, added at dispatch. `/speckit-taskstoissues` is **not** to be run: per-task issues stopped at spec 028 and running it would add a dozen items to a board used at feature granularity.

---

## Ordering, and why it is tighter than usual

The natural instinct is "fix first, test second". **Here the test must come first for a structural reason**: the table (US3) is the only instrument that makes US1's defect visible as a failure. Write the fix first and US1 has no red to quote and the phase-4 gate is not met.

And the rewrite (US2) must come **last**, behind a green table, because a behaviour-preserving change with no covering test is a rewrite, not a refactor. US1 and US2 are separate commits in that order — a refactor that is also a bug fix is two changes, and characterisation would otherwise encode the bug as the safety net.

```
T001 (red table)  →  T002 (US1 fix)  →  T003 (comment)
                          ↓
                     T004 (capture green)  →  T005 (counterfactual on the pre-rewrite code)
                          ↓
                     T006 (US2 rewrite)  →  T007 (counterfactual after)  →  T008 (US2 comments)
                          ↓
                     T009 (arch doc comment)  →  T010 (verify)  →  T011/T012 (QA)  →  T013/T014 (PR)
```

## Parallelism (ADR-0109)

| Group | Tasks | Why |
|---|---|---|
| — | T001, T004, T006 | **Serialised.** All three own `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` or depend on its exact state. Not `[P]` against each other under any circumstances. |
| — | T002, T006 | **Serialised.** Both own `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`. |
| A | **T003 `[P]`, T008 `[P]`, T009 `[P]`** | Genuinely disjoint, comment-only, and each in a file no other task opens: `V1ResourceMap.cs`, `ResourceKind.cs`, `BoundaryTests.cs`. Safe to run concurrently with each other and alongside the serial chain. |

**This spec has almost no parallelism, and that is the honest answer rather than a failure to decompose.** Two files carry nearly every change and the phase-4a obligations impose a strict order on them. Marking more tasks `[P]` would hand the orchestrator a fan-out that produces merge conflicts inside a single small diff. The three `[P]` tasks are the whole opportunity.

**No foundational blocker.** Nothing in `Shared.Kernel`, `Shared.Contracts`, `AppHost`, or any Aspire resource changes; no `.csproj` is edited; no migration is generated. There is no gate the rest of the work waits behind — only the red-before-green and green-before-refactor orderings above.

**If the slice has to narrow:** ship **T001 + T002 + T003** (US1 + US3) and file US2 as its own issue. That combination closes #2429 completely and carries its own guard. Do **not** ship US2 without the table — it would be an unnetted rewrite.

---

## US3 + US1 (P1) — the guard, then the fix

### Phase 4a — RED (`test-writer`)

**[T001] [US3] The `(type → kind, identifier)` table, observed failing on both defects**
`tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`

Add to the existing file, in its style (Shouldly, sentence-style names with underscores, `V1ResourceMap.Default`). **Do not modify or delete the six existing facts** — they are US2's characterisation net.

1. A hand-written table covering **all 20** concrete `IIntegrationEvent` types in `Shared.Contracts`, exactly as enumerated in `spec.md` §*The complete audit*. Each row is `(typeof(TContract), expected ResourceKind, a factory that constructs it, the expected identifier value)`.
2. **Every factory stamps a distinct value in every field.** Each `Guid` gets its own `Guid.CreateVersion7()`; each `string` gets `"<PropertyName>-sentinel"`; each `DateTimeOffset` a distinct instant. Hand-written, one per contract — **no AutoFixture** (ADR-0054).
3. `Every_integration_event_pivots_on_the_resource_it_is_about` — a `[Theory]` over the table: `Lookup` returns the expected `Kind` and the expected sentinel.
4. The two defect rows additionally assert `ShouldNotBe(<the CausingEventIdentifier sentinel>)`, so the row names the wrong answer as well as the right one.
5. `Every_integration_event_has_a_row_in_the_mapping_table` — reflect over `Shared.Contracts` for concrete `IIntegrationEvent`s and assert the table's key set covers them. **A 21st contract fails here, naming itself.**
6. `Every_mapping_table_row_names_a_mapped_type` — the reverse direction, against `V1ResourceMap.Default.MappedTypes`.
7. Include `LayoutRevisionPublishedV2`. The corpus is `IIntegrationEvent`, not "the V1s"; a `*V1.cs` glob misses it, and both the issue's sweep and the first pass of this spec's did.

**The expected values are written by the table's author from each contract's declared shape. They must not be read back from `V1ResourceMap`** — an assertion that checks its own input cannot fail. This is the single review point that decides whether the guard is real.

**Expected red, quoted verbatim into the PR:** two rows fail —
`OverlayHighlightRequestedV1` expected `overlay`, actual `layout`; and
`SystemVariableValueRequestedV1` expected `Name-sentinel`, actual a guid.
**Eighteen rows and both completeness assertions must pass on the first run.** If more than two rows fail, the table is wrong, not the production code — fix the table before proceeding. That check is the counterfactual proof that this guard catches what it claims and nothing else.

### Phase 4b — GREEN (`backend-engineer`)

**[T002] [US1] The two hand-tweaks**
`src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`

Add to `BuildHandTweaks`, in the file's existing idiom:

| Contract | Kind | Identifier |
|---|---|---|
| `LayoutComposition.OverlayHighlightRequestedV1` | `DomainResourceKind.Overlay` | `OverlayIdentifier` |
| `SystemVariables.SystemVariableValueRequestedV1` | `DomainResourceKind.Variable` | `Name` |

Each gets a one-line comment saying **why the convention cannot serve it** — defect A: the event is published into `LayoutComposition` but its subject is an overlay (the same shape `ResolvedOverlayTextChangedV1` is already tweaked for); defect B: the only `Guid` on the contract is the triggering event's, and the variable's handle is its name, which is how SystemVariables routes every request.

**Do not touch `BuildConventionPicker`.** Not `:158`, not `:165-172`, not `IdentifierPropertyNames`. Reversing the precedence breaks six correct mappings (`spec.md` §*Where the issue is wrong* (2)). If the diff reaches that method, phase 6 blocks it.

**Done when:** T001 is fully green, with no edit to any assertion added in T001.

**[T003] [P] [US1] Correct the comment that describes the opposite of the code**
`src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs:160-162`

The comment claims the allow-list fallback covers *"V1s whose first Guid property is not the aggregate id (e.g. it's an actor or a parent reference)"*. `if (pick is null)` makes that case unreachable — that is the bug, described as if it were the design. Rewrite it to state what the code does: the allow-list runs only for a contract with **no `Guid` property at all**; a contract whose first `Guid` is the wrong one needs a hand-tweak in `Conventions`.

**Comment-only. No code change in this file.** Verify with `git diff` that the hunk contains no executable line.

---

## US2 (P2) — the registry binds through the compiler

### Phase 4a — CHARACTERISATION, observed green (`test-writer`)

**[T004] [US2] Capture the covering tests green before the rewrite**

Run the full `AuditObservability.Application.Tests` suite at the T002/T003 commit and return the **verbatim passing output**. This is the safety net: the 20-row table plus the six pre-existing facts. Quote it in the PR alongside the red from T001.

A refactor with no covering test is a rewrite — this task is what makes T006 a refactor. **The captured tests must pass unmodified after T006.**

**[T005] [US2] Counterfactual on the pre-rewrite code — prove the mechanism is broken**

Against the code as it stands *before* T006: rename `WebhookIntegrationRotatedV1.IntegrationName` → `IntegrationLabel` in `src/Shared.Contracts/Identity/`, build the solution, and observe:

- the solution **builds clean**, and
- `V1ResourceMap.Default.Lookup(typeof(WebhookIntegrationRotatedV1), …)` returns `ResourceIdentifier: None` — a silently-lost pivot.

Revert the rename. Quote the transcript.

**Ask the build, not the file.** A test that greps `Conventions.cs` for the absence of string literals would prove the design was written down, not that it holds. **Restoring the renamed file keeps its old timestamp and MSBuild will skip the rebuild** — touch it or clean before the confirming build, or the result is a stale lie.

### Phase 4b — the rewrite (`backend-engineer`)

**[T006] [US2] `BuildHandTweaks` binds types and properties through the compiler**
`src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`

Replace the seven `Type.GetType("…")` blocks with the generic `Add<TEvent>` helper and one line per tweak, exactly as spelled out in `plan.md` §US2. Specifically:

1. Add `using` directives for the `Shared.Contracts` namespaces used. **No new project reference** — `AuditObservability.Application` already references `Shared.Contracts` (`IntegrationEventAuditHandler.cs:4-11`). Verify the `.csproj` diff is empty.
2. The two `EventIngestion.WebhookIntegrationRegisteredV1` / `…RevokedV1` entries **disappear by construction** — `Add<WebhookIntegrationRegisteredV1>` does not compile because the type does not exist. No separate deletion step, no judgement call.
3. Delete `PickByProperty` — after the rewrite nothing calls it, and dead code that reads as capability is this story's whole subject.
4. `Identify(object? raw) => raw is null ? null : ResourceIdentifier.From(raw.ToString()!)` is the **unchanged** tail of `PickByProperty`. Keep its exposure identical, including that an empty-string pick throws through `Ensure.That(...).IsNotNullOrWhiteSpace()`. **No drive-by error handling** — adding a guard here would also cost US2 its characterisation colour.
5. Collection expression for the dictionary (`Dictionary<Type, V1MappingEntry> map = [];` — already correct today, keep it). No leading underscore on any new field. Lambda parameters named after their subject, not single letters (ADR-0091).

**Done when:** T004's captured tests pass **unmodified**. If any assertion needs editing, stop and report — behaviour moved, and that is a blocker, not an adjustment.

**[T007] [US2] Counterfactual after the rewrite — prove the mechanism now holds**

Repeat T005's rename against the rewritten code. Expect a **compile error naming the property** (CS1061). Revert, rebuild, confirm green. Quote both transcripts (T005 and T007) side by side in the PR — they are US2's entire evidence.

Same timestamp caveat as T005.

**[T008] [P] [US2] Record that `ResourceKind.Webhook` is currently unproducible**
`src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:27`

One comment on the `Webhook` property: after the dead `EventIngestion` tweaks are gone, nothing in the system writes this kind; the live webhook path is `WebhookIntegration` via `Identity.WebhookIntegrationRotatedV1`. It stays in `All` because the vocabulary is a public closed list and removing it turns `GET /audit/webhook/x` from 200-empty into 400 for an unrelated reason.

**Comment-only.** No change to `All`, no change to `From`, no new value. Verify with `git diff` that the hunk contains no executable line.

---

## US3 (P3) — the architecture guard points at the stronger one

**[T009] [P] [US3] One line on the existing coverage guard's doc comment**
`tests/Architecture.Tests/BoundaryTests.cs:217-221`

The comment ends *"the audit row would still be written but the timeline endpoint would never surface it"* — the exact failure this spec fixes, which the test does not catch, because it asserts a type has **an** entry and a wrong entry satisfies it. Add one line naming `V1ResourceMapTests`' table as the guard that asserts *which* entry, so a reader of the weaker guard is not left believing it checks more than it does.

**Do not delete or weaken the existing test.** It guards a real, different thing and still runs when the Application test project does not. Weakening a gate to reach green is one of the three things the autonomous lane may not do.

**Comment-only.**

---

## Phase 5 — Verify (`/verify`)

**[T010] Observe it end to end against the real Aspire stack**

Execute `spec.md` §*Independent end-to-end test procedure* in full and write `specs/206-a-row-no-timeline-can-reach/verification.md`. Non-negotiable contents:

- The two `Audited` log lines from `AuditingMessageHandler` (`:62-67`), **quoted**, showing `overlay`/`{O}` and `variable`/`spec206Var`. State what the same lines read on `develop` — `layout`/`{O}` and `variable`/`<a guid>`. **A value that was always there proves nothing**; the before/after is the evidence.
- `GET /audit/overlay/{O}` row count **before and after** the highlight, showing the highlight joined the overlay's existing timeline next to its publish row.
- `GET /audit/variable/spec206Var` returning the row, against an empty page on `develop`.
- `GET /audit/layout/{O}` returning **empty** — the row left the timeline it never belonged on. This is the half a "the new query works" note would omit.
- `GET /audit/camera/{guid}` unchanged, proving the other eighteen mappings did not move.
- **`NFR001_AuditIngestLatencyTests` and `NFR002_AuditSearchLatencyTests`, twice each in Release, all four figures quoted.** The first run after machine churn reads exactly like a regression.

**Write every result down as observed.** A measurement reported only to the orchestrator is invisible to every grep and every later reviewer.

Operational reminders, each of which has cost this repo a session: one Aspire stack per machine — stop any running one first; stop the stack before building or MSB3027 reads as a broken build; mint tokens from Aspire's **proxied** Keycloak endpoint, not the container's mapped port; **create the event and then look — do not hunt trace history**, `list_traces` ignores `search`; and check the AppHost PID's start time against the commit before trusting a manual `curl` as evidence, because a live stack can outlive the code it runs.

## Phase 6 — QA

**[T011] `/code-review`** against `plan.md` §*Review focus for phase 6*. The two items that decide whether this spec succeeded are **(1)** the precedence order in `BuildConventionPicker` survived untouched, and **(3)** the table's expected values are hand-written and not read back from the code under test. Raise both there, not after merge.

**[T012] `/security-review` — not required.** No auth, scope, token, secret, trust boundary, idempotency key, or authorization path is touched; the audit endpoints' `RequireAuthorization(Scope.Sse.Audit.Read)` and the caller-side fab guard are unchanged, and the fix writes no new data class. Record the skip with its reason in the PR body rather than running it for form.

## Phase 7 — PR

**[T013] Open the PR** — `gh pr create --base develop` (mandatory flag; CLAUDE.md §Branching). Body must carry:

- `Closes #2429` — a **closing keyword**, and check the issue state after the merge; a bare mention closes it roughly one time in three.
- The **verbatim red output from T001** — two failing rows, eighteen passing — which is US1's and US3's phase-4a evidence (ADR-0139).
- The **verbatim green capture from T004** and the statement that it passed **unmodified** after T006 — US2's characterisation evidence.
- **Both counterfactual transcripts** (T005 on `develop`'s mechanism, T007 after) — US2's own evidence, since it has no red.
- `Phase 4a: US2 is behaviour-preserving — characterisation observed green, not red. US1 and US3 are red.` Say it explicitly; the two colours are the thing a reviewer most easily mistakes.
- The phase-5 observations from T010, including all four NFR figures.
- `Phase 6: /security-review skipped — no auth or trust-boundary surface.`
- The **corrections to the issue's own claims**, named: the title's diagnosis fits only one of the two defects; reversing the picker precedence would break six correct mappings; the corpus contains a V2 that a `*V1.cs` sweep misses; `StreamHealthChangedV1` and `AuditChunkArchivedV1` were checked and are deliberate.
- The **out-of-scope findings** from `spec.md`, named, so the follow-ups are filed against a written record rather than rediscovered.
- No `Co-Authored-By` footer (ADR-0086); the Claude Code line stays.

Commits, in order, each building **on its own** — rebase-merge lands them individually and a commit that compiles only with its successor breaks `git bisect` forever (ADR-0087). Conventional Commits (ADR-0030):

1. `test(audit): assert which resource each integration event pivots on` (T001)
2. `fix(audit): pivot the overlay-highlight and variable-value requests on their own resource` (T002, T003)
3. `refactor(audit): bind hand-tweaks through the compiler, dropping two that resolve to nothing` (T006, T008)
4. `docs(audit): point the coverage guard at the guard that asserts the answer` (T009)

**[T014] Follow-up issues to file** (not part of this PR's diff):

1. **The `variable` kind has two identifier spaces.** After this fix, three contracts pivot on the variable guid and one on its name, so `GET /audit/variable/<name>` and `GET /audit/variable/<guid>` each return half the story. The same, smaller, applies to `device`/`kiosk`, which pivot on `ClientId` while also carrying `RegisteredClientIdentifier`. Needs a decision about which handle the audit trail uses, and probably a deep link from the variable page. `spec.md` §*Out of scope* (1).
2. **`ResourceKind.Webhook` is unproducible** — remove from the closed vocabulary (a 400 where a 200-empty is returned today) or keep. `spec.md` §*Out of scope* (2).
3. **`IdentifierPropertyNames` and `NamespaceToResource` both contain dead entries** — seven property names that match nothing in the corpus, and `"Automation" → rule` for a namespace that does not exist in `Shared.Contracts` (Automation owns no integration event of its own). Harmless after this fix, since the fallback now runs for no contract at all. Cosmetic prune. `spec.md` §*Out of scope* (4).
4. **`V1ResourceMapTests` uses a leading-underscore field** (`_map`), against CLAUDE.md §House rules. Sweep issue, not a drive-by inside this diff. `spec.md` §*Out of scope* (7).
