# Tasks — Spec 202, a version that survives a restart

Issue #2426. One user story, **US1 (P1)**; every task below carries it.

**Phase 4a colour: RED.** This is behaviour-changing work — the server currently
publishes a version that regresses across a restart and must stop. A test
arriving green is a phase-4 failure, not a shortcut (constitution §Testing,
ADR-0139).

---

## Phase 4a — tests first (test-writer). The engineer may not edit any file below.

**Which red is the evidence.** T001 and T002 compile against the *existing*
public surface — HTTP endpoints and the real SignalR hub — and fail at an
assertion. **Their verbatim output is what the PR quotes.** T003–T005 introduce
`IOverlayTextVersions`, so on unmodified `develop` they are red by *compilation*
until 4b wires it; that is a weaker form of evidence and is recorded as such
rather than presented as a behavioural failure.

| ID | P | Task |
|---|---|---|
| **T001** | | **The restart test — SC-1.** `tests/Integration.Tests/SystemVariables/VersionSurvivesARestartTests.cs` (new). Define a variable, bind it into a published overlay's label, attach a real `HubConnection`, change the value three times and capture the three `Version`s. Restart the `system-variables` resource. Change the value once more. **Assert the fourth `Version` is strictly greater than the third.** Reuse `RestartLosesNothingIntegrationTests.cs:84-120`'s restart helper verbatim — `ResourceCommandService` + `KnownResourceCommands.RestartCommand`, idempotent `StartCommand` in the `finally`, then wait with `WaitBehavior.WaitOnResourceUnavailable` (the default gives up on the very transition being watched). Wait for a **condition**, never a count (ADR-0150). |
| **T002** | | **The REST snapshot after a restart — SC-2.** Same file, same fixture, same restart. After the restart, `GET /system-variables/snapshot?overlayIdentifier=…&fabId=…` with a `sse.variables.read` token and **assert the returned `version` is not lower than the highest version seen before the restart.** Today it is `0`. |
| **T003** | `[P]` | **Application unit tests for the push fan-out.** `tests/SystemVariables.Application.Tests/EventHandlers/*`. Declare `IOverlayTextVersions` in `src/SystemVariables/Application/Resolution/` and a hand-written fake (ADR-0054 — no AutoFixture) in `tests/SystemVariables.Application.Tests/Fakes/`. Assert both `VariableValueChangedDomainEventHandler` and `VariableArchivedDomainEventHandler` take their `Version` from the store, and that a fan-out over N overlays calls `AdvanceAsync` **once**, not N times. Remove the version members from the existing `Fakes/InMemoryReverseIndex.cs`. |
| **T004** | `[P]` | **Snapshot handler unit tests — SC-5, SC-7.** `tests/SystemVariables.Application.Tests/Queries/GetOverlaySnapshotQueryHandlerTests.cs`. Update the six direct constructions for the fourth parameter. Assert the version comes from `IOverlayTextVersions.CurrentAsync`; assert an unknown overlay still returns the 404 error **and advances nothing**; assert the version is read **before** the text is resolved (order the fake records, then assert on that record — not on a comment). |
| **T005** | `[P]` | **Store integration tests — SC-3, SC-4.** `tests/Integration.Tests/SystemVariables/OverlayTextVersionStoreIntegrationTests.cs` (new), against the real database. Cover: a first-ever advance returns the floor, not 1; a second returns floor+1; **a store resolved from a brand-new DI scope continues from the persisted value** (the restart guarantee at unit cost); two concurrent advances on one overlay return two distinct increasing values; a fan-out array containing the same identifier twice does not raise Postgres' *"ON CONFLICT DO UPDATE command cannot affect row a second time"*; `CurrentAsync` for an untouched overlay returns `0`. |
| **T006** | `[P]` | **Baseline the latency figure — R1.** Run `NFR_VariableResolutionLatencyTests` **twice** on unmodified `develop` and record both printed medians. The first run after machine churn reads exactly like a regression; one number is not a baseline. |

**Gate 4a:** T001 and T002 observed failing against unmodified `develop`, output
captured verbatim, and the fixture confirmed to have actually restarted the
resource (a restart test that is green before the fix is testing nothing — R2).

---

## Phase 4b — implementation (engineer)

The engineer receives T001–T006's verbatim output as its brief and **may not
edit any test file listed above** to make it pass.

### The foundational block — not `[P]`, and it lands as one unit

Everything downstream needs the contract and the table. Each commit must build
on its own (ADR-0087 rebase-merge), so this block is one commit.

| ID | Task |
|---|---|
| **T007** | **The migration and the table.** `overlay_text_version (overlay_identifier uuid primary key, version bigint not null)` in `SystemVariablesDbContext`, plus an EF migration in `Infrastructure/Persistence/Migrations/`. Generated migration files are exempt from the `Ensure.That` guard rule (ADR-0105). |
| **T008** | **`OverlayTextVersionStore`.** `src/SystemVariables/Infrastructure/Persistence/OverlayTextVersionStore.cs`, implementing `IOverlayTextVersions` over `SystemVariablesDbContext`. Mirror `VariableValueRequestDedupStore.cs` exactly: `Ensure.That(...)` guards, a `const string sql` in a raw string literal, `ExecuteSqlRawAsync`/`SqlQueryRaw` with positional parameters — **never** interpolation. `AdvanceAsync` is the single `INSERT … SELECT unnest(…) … ON CONFLICT DO UPDATE SET version = overlay_text_version.version + 1 RETURNING …`, de-duplicating its input first (R3). New rows insert at the floor `1_000_000_000`, with plan.md §3's reasoning as the `why` comment — the only comment this file needs. |
| **T009** | **Register it.** Scoped, in `SystemVariablesPersistenceModule` beside the dedup store. Not a singleton — it holds no state, and a singleton could not take the scoped `DbContext`. |

### Parallel after the block

| ID | P | Task |
|---|---|---|
| **T010** | `[P]` | **Strip the version from the reverse index.** Remove `versionByOverlay`, `NextVersionFor` and `CurrentVersionFor` from `IReverseIndex` and `InMemoryReverseIndex`, and the doc comments describing them (`IReverseIndex.cs:47-59`). Update `InMemoryReverseIndex`'s class doc, which currently lists the version counter among what it holds. |
| **T011** | `[P]` | **`VariableValueChangedDomainEventHandler`.** One `AdvanceAsync` for the whole affected set, before the per-overlay loop; each overlay reads its own version out of the returned map. Keep the existing early return on an empty set. Keep the deconstruction-first shape at `:33`. |
| **T012** | `[P]` | **`VariableArchivedDomainEventHandler`.** Same treatment at `:95`. |
| **T013** | `[P]` | **`GetOverlaySnapshotQueryHandler`.** Fourth constructor parameter; **read the version before resolving the text**, not after (plan.md §5, SC-7); rewrite the stale spec-148 comment at `:12-16` in the same commit. |
| **T014** | `[P]` | **`ResolvedOverlayTextChangedV1`'s doc comment.** `src/Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV1.cs:13-18` says the *"per-overlay version counter"* stays in SystemVariables' reverse index. The record itself does not change — only the sentence that is about to stop being true. |

**Gate 4b:** T001–T005 green, unmodified. Format and analyzers clean. Release
build clean (collection-expression and NRT rules are `warning` under
`TreatWarningsAsErrors`). Coverage gates hold (ADR-0065).

---

## Phase 5 — verification (`/verify`)

| ID | Task |
|---|---|
| **T015** | Walk spec.md's *Independent end-to-end test procedure* against a booted Aspire stack — all seven steps, including the second restart at step 7. Confirm the tile visibly updates at step 5 and the snapshot reports ≥ 4 at step 6. A green T001 is not this: it proves the server, not that a React tile re-renders (`ResolvedTextReachesItsFabTests` says so about itself in its own header). |
| **T016** | Re-run `NFR_VariableResolutionLatencyTests` **twice** and record both medians. Quote all four figures — two before (T006), two after — in the PR. §IV leg: `event → overlay state`, ≤ 200 ms. Write the numbers down in the verification note as well as reporting them: a measurement that exists only in a hand-off message is invisible to every later grep. |

---

## Phase 6 — QA

| ID | Task |
|---|---|
| **T017** | `/code-review`. Specific asks: the SQL is parameterised (no interpolation anywhere near a `Guid` array); no swallowed exception in the store; the `Ensure.That` guards are present and are not `ArgumentNullException.ThrowIfNull` (ADR-0105); the floor's `why` comment survived. |
| **T018** | `/security-review` — the store takes caller-influenced identifiers into raw SQL. Narrow scope: injection surface and the unchanged `sse.variables.read` requirement on the snapshot route (SC-6). |

---

## Phase 7 — PR

| ID | Task |
|---|---|
| **T019** | PR against **`develop`** (`--base develop`), referencing #2426 with a closing keyword and checking the issue's state after the merge — a PR mention auto-closes roughly one time in three. Body carries: T001/T002's verbatim red output; the four latency medians; the §IV leg citation; and the three rejected fix directions in one line each, so the choice is reviewable without opening plan.md. |

---

## Gates and flags for the orchestrator

- **Foundational, blocks everything:** T007–T009 (one commit). T010–T014 fan out
  after it; T003–T006 fan out immediately.
- **No frontend work exists in this spec.** Nothing under `apps/` is touched —
  see plan.md §1, "Rejected as the primary fix: direction 3". Do not dispatch a
  frontend engineer.
- **No contract change.** `Shared.Contracts` gets a corrected comment (T014) and
  nothing else, so no `V2` and no consumer coordination.
- **No ADR is written.** If phase 6 concludes a durable counter table is
  ADR-class, that is a **blocked** outcome (ADR-0144 — this lane implements
  decisions, it does not make them), not an ADR written in passing.
- **Board:** #2426 is already on Project #13 in Todo with `agent:ready` and no
  `agent:blocked` (verified 2026-09-21). Phase 3's gate is satisfied; no
  per-task issues are created (the repo stopped that after spec 028).
- **Spec number 202** — 201 is claimed by PR #2493 (`2290-idempotency-reaper`),
  which is unmerged. Re-check before the PR: two unmerged branches can both
  claim a number, and a parked PR merging in the meantime changes the answer.
