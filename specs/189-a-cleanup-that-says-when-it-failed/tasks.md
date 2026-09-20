# Tasks — Spec 189, A cleanup that says when it failed

**Spec:** `specs/189-a-cleanup-that-says-when-it-failed/spec.md`
**Plan:** `specs/189-a-cleanup-that-says-when-it-failed/plan.md`
**Issue:** [#2274](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2274)
— on Project #13, status Todo, verified with `--limit 2000`. **No `item-add`.**

**Phase-4a colour:** US1 is **RED, discharged by counterfactual** (spec §6).
US2 is **characterisation, observed green** (spec §6, last paragraph).

**No `[P]` markers anywhere.** Two files, one Aspire stack, and US2 gated on
US1's CI reading (plan §7). Fanning this out creates contention, not speed.

---

## Do not

- **Do not fold before the reading.** T013+ are gated on T011's outcome. Folding
  with the assertion in one diff makes a red unattributable — the observation
  must vary exactly one thing.
- **Do not delete, soften, `[Skip]`, suppress or comment out the new
  assertion to reach green.** ADR-0144 names this as a blocked outcome. If it
  fires, spec §5's table is the response and Outcome C **escalates**.
- **Do not treat a red CI integration job here as a broken build.** It may be
  the finding this issue was filed to obtain. Read spec §5 before touching
  anything.
- **Do not edit `src/`.** Not `HttpKeycloakAdminClient`, not `AppHost.cs`, not a
  realm file. #2166 is closed; its decision stands.
- **Do not edit `.github/workflows/ci.yml`.** CI is the instrument. Changing it
  changes what is being measured.
- **Do not add a `Category` trait** to either test class — that would remove
  them from the very job that answers the question.
- **Do not change `DeleteClientAsync`'s signature** in US1 (ADR-0049's
  `CancellationToken` question resolves itself when US2 deletes the method).
- **Do not close spec §1.5's zero-element blind spot** here.
- **Do not leave `RealmProbe`'s class doc saying the question is open** once it
  is answered (T007 / T016).
- **Do not claim "the deletes always succeed"** on one green run. Say the
  measured figure.
- **Do not offer a green local run as the observation**, and do not offer a
  green CI *job* as the reading — read the trx (T011).
- **Link an issue on its first mention** in each document — the convention in
  specs 186–188. Bare numbers thereafter are fine; invented ones are not.
- **Do not merge before T011 and phase 6 have both run** (plan §3).

---

## US1 (P1) — The observation

*Deliverable: one assertion, shipped, plus CI's reading of it. Valuable either
way it reads.*

**Phase 4a — `test-writer`.**

- **[T001] [US1] Confirm the stack is free.** Check no other Aspire AppHost or
  integration `testhost` process is running (one machine, one stack). A
  concurrent boot yields `FailedToStart`, which reads exactly like a code
  defect. Record what was found.
- **[T002] [US1] Re-read the two helpers on this tree** and confirm spec §1.1's
  quotes still match byte-for-byte at
  `tests/Integration.Tests/Identity/RealmProbe.cs:132-146` and
  `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs:178-192`.
  Depends on: —
- **[T003] [US1] Add the assertion** to `DeleteClientAsync`'s `foreach`: capture
  the `HttpResponseMessage` and assert `IsSuccessStatusCode` with a Shouldly
  custom message naming the client id, the numeric status, and that the client
  survives in the realm the next pass sweeps. Mirror
  `RealmProbe.DeleteAsync`'s message structure; do not invent a second spelling.
  **One statement plus its local — nothing else in the file.**
  Depends on: T002
- **[T004] [US1] Counterfactual: prove the assertion can fail.** Temporarily
  point the DELETE at a client uuid that cannot exist, run
  `dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release --filter "FullyQualifiedName~KioskInheritedPrivilegeIntegrationTests"`,
  and observe **both** delete-calling tests fail with the new message.
  **Capture the output verbatim** — this is the phase-4a red and it is quoted in
  the PR body. An assertion never seen failing does not satisfy the gate.
  Depends on: T003
- **[T005] [US1] Revert the deliberate break** and confirm by diff that the only
  remaining change is T003's assertion. A restored file keeps its old timestamp,
  so force the rebuild rather than trusting an incremental one.
  Depends on: T004
- **[T006] [US1] Preliminary local run.** Same filter, unbroken. Record each
  test's outcome verbatim. **Label it preliminary** — one machine, one fresh
  realm, one pass; it is not the observation.
  Depends on: T005
- **[T007] [US1] Update `RealmProbe`'s class doc** (`:25-32`) so it no longer
  says the delete is deliberately unfolded and the question unchecked. At this
  point it should say the assertion now exists on both sides and the reading is
  pending, with the issue link. Final wording lands in T016/T019.
  Depends on: T003
- **[T008] [US1] Commit.** Conventional Commits (ADR-0030), no
  `Co-Authored-By` (ADR-0086). One commit that builds on its own.
  Suggested: `test(identity): the kiosk cleanup reports a refused delete`.
  Depends on: T005, T007

**Phase 4b for US1: N/A** — no production code; the assertion is the
deliverable. Record the line
`Phase 4b: N/A for US1 — the deliverable is test code.` in the PR body rather
than leaving it unexplained.

**Phase 7a — open the PR early, because the PR is the instrument.**

- **[T009] [US1] Push and open the PR as a draft**, `--base develop`. Body
  carries: the question being asked, T004's verbatim counterfactual red, T006's
  preliminary local reading, spec §5's outcome table, and an explicit line that
  **a red integration job here may be the finding, not a defect**. Reference
  [#2274](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2274).
  Depends on: T008
- **[T010] [US1] Start the CI watcher** and confirm the run exists and is
  attached to this PR's tip before attaching to it. A watcher started too early
  exits immediately with "no checks reported" and exit 1, which reads as red.
  Depends on: T009

---

## The gate — Phase 5 is the observation

*Nothing below US1 starts until this resolves. This is the foundational item
(plan §7).*

- **[T011] [GATE] Read the result, from the artifact.**
  1. Confirm the `integration tests (Docker)` job ran — a job with zero steps
     never ran (GitHub allocation); re-run, do not investigate.
  2. **Download `integration-test-results` before any re-run** — a passing
     re-run erases the failure from the run's history.
  3. Read `integration.trx` for
     `A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege`
     and `An_account_the_provider_creates_holds_it_until_something_removes_it`
     **by name**, with their `Outcome` and, if failed, the verbatim message and
     the numeric status.
  4. Confirm the enrolment create succeeded, so spec §1.5's zero-element loop
     did not silently skip the assertion.
  Depends on: T010
- **[T012] [GATE] Classify against spec §5** — Outcome **A** (green),
  **B** (404), **C** (409/5xx/other), or **D** (flaky; re-run twice before
  concluding). Write the phase-5 verification note into
  `specs/189-a-cleanup-that-says-when-it-failed/verification.md` with the run
  id, the job conclusion, the two test outcomes as read from the trx, and the
  classification. **Write the result down as observed** — a measurement reported
  only to the orchestrator is invisible to every later grep.
  Depends on: T011

---

## US2 (P2) — The fold. **Runs only under Outcome A (or B, after its amendment).**

*Phase 4a colour: characterisation, observed green. The four tests in
`KioskInheritedPrivilegeIntegrationTests` are the covering suite; they were
captured green by T011 and must pass **unmodified** after. An assertion that has
to be edited is evidence the fold moved behaviour — block, do not adjust.*

**Phase 4b — `backend-engineer`.**

- **[T013] [US2] Delete `KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync`**
  and point both call sites (`:55`, `:94`) at
  `await realm.DeleteAsync(clientId, CancellationToken.None);`. Remove the
  `System.Text.Json` using if it becomes unused. **Nothing else in the file.**
  Depends on: T012 (Outcome A or B)
- **[T014] [US2] Run the four tests unmodified** and confirm all four pass.
  Capture verbatim. If any assertion needed editing, **stop** — that is the fold
  moving behaviour.
  Depends on: T013
- **[T015] [US2] Confirm the duplication is actually zero.** Re-check #2182's
  eight-row table against this tree and confirm row 8 — and every other row —
  now has one implementation. Grep the general shape, not one spelling.
  Depends on: T013
- **[T016] [US2] Rewrite `RealmProbe`'s class doc** to its final form: the
  question is answered, what the answer was, the CI run id that answered it, and
  the fact that one helper now serves both files.
  Depends on: T014
- **[T017] [US2] Commit as a second commit**, building on its own (rebase-merge
  lands them individually; a commit that only compiles with its successor breaks
  `git bisect`). Suggested:
  `refactor(identity): one client-delete helper, now that the delete is checked`.
  Depends on: T014, T015, T016

---

## US3 (P3) — The finding. **Runs only under Outcome C. Mutually exclusive with US2.**

- **[T018] [US3] Do not change the assertion.** Record the verbatim failure, the
  numeric status, the client id, and **which population** failed — the
  enrolment-created client or the directly-created control. Spec §1.4 says the
  second has ~45 green asserted deletes behind it, so a failure there means
  something different from a failure in the first.
  Depends on: T012 (Outcome C)
- **[T019] [US3] File the follow-up issue** with that evidence, the CI run link,
  and the two candidate responses (ship the assertion red and park; or adopt
  #2166's logging form). Cross-reference
  [#2274](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2274),
  [#2182](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2182)
  and [#2166](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2166).
  Check the board for the same defect already filed before opening a second one.
  Add it to Project #13.
  Depends on: T018
- **[T020] [US3] Escalate.** Apply `agent:blocked` to #2274, comment with the
  verbatim failure, and **ask**. The lane may not choose between parking a red
  PR and adopting the logging form: a principled alignment with production and a
  gate weakening are indistinguishable in the diff (spec §5, last paragraph).
  Depends on: T019

---

## Closing — Phases 6 and 7b

- **[T021] Phase 6 — `backend-reviewer`**, with the outcome in hand. It cannot
  run earlier: under A the fold does not exist yet, under C the diff is about to
  be reframed. Under **Outcome C only**, add `infra-reviewer` as a second
  opinion on the Keycloak finding — not on the C# diff. `security-reviewer` is
  not invoked (plan §4).
  Depends on: T017 or T020
- **[T022] Finalise the PR body** per spec §5's table — A, B, C and D each get a
  different body. State the measured figure, not "always". Keep T004's verbatim
  counterfactual red in the body.
  Depends on: T021
- **[T023] Merge on green.** Read the four CI buckets by hand — `develop` has no
  required status checks, so that read is the only gate; never merge on a
  skipped or cancelled bucket. Use a closing keyword for #2274 and **check the
  issue state after the merge** (a PR mention auto-closes about one time in
  three). Under Outcome C this task does not run — the PR stays parked and
  blocked.
  Depends on: T022
