# Plan — Spec 189, A cleanup that says when it failed

**Spec:** `specs/189-a-cleanup-that-says-when-it-failed/spec.md`
**Issue:** [#2274](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2274)
**Branch:** `2274-client-delete-assertion`

---

## 1. Shape of the change

**No bounded context is modified.** The entire diff lands in
`tests/Integration.Tests/Identity/`. There is no Domain, Application,
Infrastructure or Api layer in play; no entity, no value object, no invariant,
no domain event, no integration event, no migration, no DI registration, no
Aspire resource.

| Layer | Touched | Why |
|---|---|---|
| `src/*/Domain` | no | — |
| `src/*/Application` | no | — |
| `src/*/Infrastructure` | no | `HttpKeycloakAdminClient` is *exercised* by the affected test, never edited. #2166 is closed and its decision stands. |
| `src/*/Api` | no | — |
| `src/Shared.Contracts` | no | No cross-context communication changes. |
| `src/AppHost` | no | Read in phase 1 to establish §1.3; not edited. |
| `tests/Integration.Tests/Identity` | **yes** | The two files that carry the divergence. |
| `tests/Architecture.Tests` | no | No boundary rule is affected; NetArchTest's no-cross-context-reference rule is untouched because no project reference changes. |
| `apps/*` | no | — |
| `.github/workflows` | no | CI is the **instrument**, not the subject. Editing it would change what the observation measures. |

**Boundary rules:** unaffected. `Integration.Tests` already references Identity's
Infrastructure assembly (`SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin`
is imported by both files today); nothing new is referenced.

---

## 2. The two files, and what each becomes

### `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs`

**US1** — one statement added inside the existing `foreach` in
`DeleteClientAsync` (`:186-191`), plus the `HttpResponseMessage` local it needs.
The assertion text mirrors `RealmProbe.DeleteAsync`'s **exactly in structure** —
client id, numeric status, and what surviving costs — so the two messages can be
read as one rule rather than two opinions. No signature change, no new method,
no new using.

**US2** — `DeleteClientAsync` is deleted entirely. Its two call sites (`:55`,
`:94`) become `await realm.DeleteAsync(clientId, CancellationToken.None);`. The
`System.Text.Json` using becomes unused and goes with it. The four tests are not
touched other than at those two call sites.

### `tests/Integration.Tests/Identity/RealmProbe.cs`

**US1 and US2** — one comment block only. The class doc at `:25-32` currently
says the delete is *deliberately not folded* and names the open question. Once
the question is answered that sentence is **false**, and a stale comment that
says a thing is unresolved after it is resolved is the exact clerical failure
this repository has had to correct in §IV and in CLAUDE.md's phase-3 row. The
comment is rewritten to record the answer, with the CI run id.

**No code change to `RealmProbe.DeleteAsync`** — except under Outcome B, where
the 404 acceptance lands in *both* helpers in one commit with one stated reason.

---

## 3. Phase order — and why it is not ADR-0037's usual one

**Phase 5's observation is a CI run, and CI runs on a PR.** So phase 7's PR is
opened **before** phase 5, not after it. This is stated here so a later phase
does not read it as a skipped gate or a lane violation.

```
1 Specify      → spec.md                                  [done]
2 Plan         → plan.md                                   [done]
3 Tasks        → tasks.md; #2274 already on Project #13     [done]
4a test-writer → assertion + counterfactual red + revert + real run,
                 two verbatim outputs returned
4b (US1)       → N/A. There is no production code in US1; the assertion IS
                 the deliverable. Recorded in the PR as
                 "Phase 4b: N/A for US1 — the deliverable is test code."
7a             → open the PR (draft), body carrying 4a's verbatim
                 counterfactual red. THE PR IS THE INSTRUMENT.
5  verify      → THE OBSERVATION. Read CI's `integration` job, then read
                 `integration.trx` from the `integration-test-results`
                 artifact for the two tests by name. Write the verification
                 note. Outcome A / B / C / D is decided here (spec §5).
--- branch on the outcome ---
4b (US2)       → Outcome A only. backend-engineer folds; characterisation
                 colour, the four tests green before and unmodified after.
                 Second CI run confirms.
6  QA          → backend-reviewer, WITH the outcome in hand. Cannot
                 meaningfully run earlier: under A it reviews a fold that
                 does not exist yet; under C it would review a diff that is
                 about to be reframed as a finding.
7b             → finalise the PR body per spec §5's table, merge on green.
```

**Why phase 6 cannot precede phase 5 here.** Normally a reviewer can read a diff
before the verification note exists. Here the diff itself is not final until the
observation resolves: Outcome A adds a whole second commit, Outcome B edits both
helpers, Outcome C changes nothing but rewrites the PR into a finding report. A
review run before phase 5 would be reviewing one of four possible changes.

**The lane's "park, do not wait for CI" still applies to the *other* issues.**
Phase 7a opens the PR and the orchestrator may take the next issue; what it may
not do is treat *this* PR's CI as a mere gate. Its result is the deliverable.

---

## 4. Roles

| Phase | Agent | Why this one |
|---|---|---|
| **4a** | `test-writer` | Tests only, runs them, returns verbatim output — which is precisely what US1 is. The counterfactual (deliberately break the DELETE, capture the red, revert) is the phase-4a red discharge and must be done by the agent that may not later edit the test to pass. |
| **4b (US1)** | — | **N/A.** No production code. Recorded, not skipped silently. |
| **4b (US2)** | `backend-engineer` | The fold is a C#/.NET refactor of test helpers in the Identity integration suite. Behaviour-preserving; the engineer receives 4a's verbatim output as its brief and may not edit the tests to pass. |
| **5** | orchestrator / `/verify` | Reading a CI artifact is not an implementation role. |
| **6** | `backend-reviewer` | `KioskInheritedPrivilegeIntegrationTests` lives in `tests/Integration.Tests/Identity/`, namespace `SmartSentinelEye.Integration.Tests.Identity`, and exercises `SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin.HttpKeycloakAdminClient`. It is **Identity-domain integration test code in C#**, not Aspire/CI/Docker plumbing — nothing in the diff touches `AppHost.cs`, a workflow, a realm file or a container. So `backend-reviewer`, not `infra-reviewer`. |
| **6 (Outcome C only)** | `+ infra-reviewer` | Under C the finding is about *Keycloak's answer to a DELETE against a realm the fixture boots*, which is that reviewer's subject matter. Added only then, and only as a second opinion on the finding — not on the C# diff. |

`security-reviewer` is **not** invoked. No auth model, scope, token, secret or
trust boundary changes; the admin credentials involved are the existing
dev-only realm-import constants, unchanged.

---

## 5. What phase 5 must actually read, and what it may not claim

**Read, in this order:**

1. The `ci.yml` run on this PR's head SHA. Confirm the run exists and is
   attached to the tip — a watcher started too early exits at once with "no
   checks reported" and exit 1, which reads exactly like a red run.
2. The `integration tests (Docker)` job's conclusion. A job with **zero steps**
   never ran (GitHub allocation, not the diff) — re-run, do not investigate.
   `cancelled` is not `failure` and is not `success`.
3. The `integration-test-results` artifact → `integration.trx` → the two test
   names, by name, with their `Outcome`. **Download before any re-run**: a
   passing re-run erases the failure from the run's history.

**May claim:** "on run *<id>*, both tests passed with the assertion in place, so
both DELETEs answered a 2xx."

**May not claim:** "the deletes always succeed." One run is one sample of each
population. The spec's honest form is the measured one (spec §8 last bullet).

**May not claim** a green job is the reading (§4 step 5) or that a green local
run is the observation (§4 step 4).

---

## 6. Risks, and the mitigation for each

| Risk | Mitigation |
|---|---|
| The real red arrives and gets "fixed" by weakening the check | Spec §5 decides the response for each status **before** the red exists; §5's last paragraph makes Outcome C an escalation, not a judgement call. ADR-0144 names the blocked outcome. |
| The fold and the assertion land together, making a red unattributable | US2 is a separate story, gated on US1's reading. Spec §8 first bullet. Two commits, each building on its own (ADR-0087 / rebase-merge / `git bisect`). |
| A green run where the assertion was never evaluated | Spec §1.5's zero-element loop. Phase 5 step 6 confirms the enrolment create succeeded. |
| A flaky red read as a finding | Spec §5 Outcome D; re-run twice before concluding. A first run after machine churn looks exactly like a regression. |
| Local verification blocked by another Aspire stack | One machine, one stack. Phase 4a checks for a running AppHost / testhost process before booting, and treats `FailedToStart` as contention until proven otherwise. |
| `RealmProbe`'s class doc left saying the question is open | §2: the comment is part of the diff, in both US1 and US2. |
| The PR merges before the observation is read | Phase 7b is explicitly *after* phase 5 and 6 in §3. A parked PR is not a delivered PR. |

---

## 7. Parallelism (ADR-0109)

**There is none worth having.** Both stories touch the same two files, US2 is
gated on US1's CI reading, and the fixture serialises everything anyway (one
machine, one Aspire stack). No task carries `[P]`. Recorded explicitly so the
orchestrator does not fan out a two-file change into contention.

The foundational item is **US1's CI run**: nothing downstream of it — US2, the
final PR body, phase 6 — can start until it resolves.
