# Spec 189 — A cleanup that says when it failed

**Issue:** [#2274](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2274)
— *"The two client-delete helpers disagree on whether a failed DELETE matters,
and nobody has looked."* Labels `tech-debt`, `agent:ready`, no `agent:blocked`.
**Already on Project #13** (status Todo) — verified with
`gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered on
`content.url`, because the default 30-item page and the number filter both
report an empty board for a board that is not empty. **No `item-add` is
needed.**

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **187** as the
highest merged. **188 is not free**: remote branch `2254-inert-parameters`
already carries `specs/188-an-override-the-apphost-hears/`. Every other remote
head (`feat/1975-…`, `feat/2345-…`, `fix/1995-…` ×2, `perf/1956-…`,
`refactor/1970-…`) was checked for a `specs/189*` directory and none holds one.
This spec is **189**. Re-check before the next spec is cut: two unmerged
branches can both claim a number, and the check has to be repeated after every
parked-PR merge.

**Branch:** `2274-client-delete-assertion`, cut from `origin/develop`.

**Created:** 2026-09-20. Phases 1–3 of ADR-0037.

**ADRs referenced:**

- **ADR-0139** (rules that fail the build, not the review) — the ADR this spec
  serves directly. A cleanup whose failure nothing reports is a rule nobody is
  enforcing; the question here is whether it *can* be enforced without a false
  red.
- **ADR-0144** (the autonomous lane) — its phase-4a colours, and its named
  blocked outcome: **weakening a gate to reach green** (a deleted test, a
  lowered threshold, a new suppression). This spec's whole risk is that the
  cheapest exit from its own possible red is exactly that.
- **ADR-0103** (integration tests are Aspire-only; no Testcontainers) — why the
  only instrument that can answer this question is the Aspire fixture's real
  Keycloak, and therefore why CI's `integration` job is the observation.
- **ADR-0134** (a wall display holds a grant nobody else can) — the feature the
  two affected tests cover, and the reason a residue client stamped
  `sse.kind=kiosk` is not inert.
- **ADR-0036** (smallest possible change; surface assumptions; no speculative
  generality).
- **ADR-0037** (the phased workflow, and its gates).
- **ADR-0052** / **ADR-0053** (xUnit + Shouldly; sentence-style test names).
- **ADR-0049** (`CancellationToken` last, mandatory).
- **ADR-0109** (the `[P]` disjoint-file rule).
- **Spec 092** (`specs/092-the-sweep-is-registered-or-retired/`) §"Do not" —
  where both deferrals originate.
- **Spec 137** (`specs/137-one-copy-of-the-admin-helpers/`) — the fold that
  closed seven of #2182's eight rows and deliberately left row 8 open.

**No new ADR is needed.** Nothing architectural is decided. ADR-0139 already
says a rule belongs in the build; ADR-0144 already says a gate may not be
weakened to reach green; ADR-0103 already says the Aspire fixture is the only
integration instrument. This spec adds **one assertion to one test helper and
reads the result**. It does not amend the constitution.

**Latency budget (§IV): N/A.** The diff lands entirely in
`tests/Integration.Tests/Identity/`. No production assembly changes behaviour,
no leg of `event arrival → overlay rendered` is touched, and no leg's
measurement status moves.

---

## 1. The divergence, re-measured on this tree

Everything in this section was read on this worktree at
`2274-client-delete-assertion` (cut from `origin/develop`) on 2026-09-20. The
issue's line numbers are from an older tree; the ones below are current.

### 1.1 The two helpers

`tests/Integration.Tests/Identity/RealmProbe.cs:132-146` — **asserts**:

```csharp
public async Task DeleteAsync(string clientId, CancellationToken cancellationToken)
{
    using HttpClient admin = await AuthorisedAdminClientAsync(cancellationToken);

    JsonElement clients = await ReadJsonAsync(
        admin, $"admin/realms/{Realm}/clients?clientId={Uri.EscapeDataString(clientId)}", cancellationToken);
    foreach (JsonElement client in clients.EnumerateArray())
    {
        HttpResponseMessage deleted = await admin.DeleteAsync(
            $"admin/realms/{Realm}/clients/{client.GetProperty("id").GetString()}", cancellationToken);

        deleted.IsSuccessStatusCode.ShouldBeTrue(
            $"removing probe client '{clientId}' answered {(int)deleted.StatusCode}; it is still "
            + "in the realm, stamped as this suite planted it, and the next pass over this "
            + "realm will read it as residue it did not create");
    }
}
```

`tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs:178-192`
— **discards**:

```csharp
private async Task DeleteClientAsync(string clientId)
{
    using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

    JsonElement clients = await RealmProbe.ReadJsonAsync(
        admin,
        $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
        CancellationToken.None);
    foreach (JsonElement client in clients.EnumerateArray())
    {
        await admin.DeleteAsync(
            $"admin/realms/{RealmProbe.Realm}/clients/{client.GetProperty("id").GetString()}",
            CancellationToken.None);
    }
}
```

**Line for line these are the same method** — same admin client, same query,
same loop, same URL — except for the four lines of `ShouldBeTrue`. Spec 137
reduced the Kiosk copy to exactly this and stopped, and `RealmProbe`'s class doc
(`:25-32`) says so by name: *"its client-delete loop discards the DELETE
response where `DeleteAsync` below asserts it. Folding the delete would add an
assertion to the two tests that call it, and nothing has ever checked whether
those deletes succeed."*

`RealmProbe.DeleteAsync`'s own doc comment (`:112-131`) argues the unchecked
form is the shape of **#2166**, and accepts the cost explicitly: *"Both call
sites are `finally` blocks, so a throw here replaces a failure from the body.
That is the accepted cost."*

### 1.2 Correction: **two** tests call it, not four

The issue says *"four currently-green characterization tests could go red."*
That is inherited from #2182, where **all four** tests in the file are the
covering suite for the *whole* fold. For **row 8 alone**, only two call the
delete helper:

| Test | Calls `DeleteClientAsync` | What it deletes |
|---|---|---|
| `A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege` | `:55` (finally) | a client created **through enrolment** — `HttpKeycloakAdminClient.CreateClientAsync` |
| `An_account_the_provider_creates_holds_it_until_something_removes_it` | `:94` (finally) | a client created **directly** by `POST admin/realms/…/clients` |
| `An_operator_does_not_hold_the_long_lived_credential_privilege` | — | nothing; reads a realm-file user |
| `A_wall_display_account_does_hold_it` | — | nothing; reads a realm-file user |

So the blast radius of the assertion is **two tests and two DELETEs per CI
run**, and `RealmProbe`'s comment (which says *two*) is the accurate one.

### 1.3 Correction: the realm these tests run against is **not** long-lived

The issue notes *"the realm is long-lived in dev … so a leaked client
persists."* That is true of the **run-mode developer stack**, and it is not the
stack these tests use.

`src/AppHost/AppHost.cs:127-136`:

```csharp
var keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithRealmImport("../AppHost/Realms");

if (isRunMode && !isE2ETests)
{
    keycloak
        .WithLifetime(ContainerLifetime.Persistent)
        .WithDataVolume();
}
```

`tests/Integration.Tests/Fixtures/AspireFixture.cs:281` boots the AppHost with
`E2ETests=true`. So for **every** integration run — local and CI —
`isE2ETests` is true, the branch is skipped, and Keycloak gets **no data volume
and no persistent lifetime**. The realm is rebuilt from the import on every
fixture boot.

**This changes what the finding would mean, and it must not be overstated
later.** A residue left by these helpers persists for **the remainder of one
fixture lifetime**, not between runs — which is exactly, and only, what
`RealmProbe`'s comment claims: *"a client stamped `sse.kind=kiosk` in the realm
the next test in this collection sweeps."* The collection is shared
(`[Collection(AspireCollection.Name)]` on both this file and
`KioskPrivilegeSweepStartupIntegrationTests`), and the sweep test is the one
that would read a residue as something it did not create.

### 1.4 The prior nobody has written down: the assertion has already been running

`RealmProbe.DeleteAsync` has carried its assertion since commit `0776ceaf`
(2026-09-07, *"the realm probe checks its delete and stops leaking clients"*).
`KioskPrivilegeSweepStartupIntegrationTests` calls it at `:99`, `:151` and
`:152`. Neither class carries a `Category` trait, and CI's `integration` job
filters only `Category!=Measurement&Category!=Disruptive&Category!=Maintenance`
— so both classes run on every integration job.

Measured on 2026-09-20: the **15 most recent `ci.yml` runs on `develop`**
(run ids 35260128345 … 35500014633, spanning 2026-09-17T18:39 → 2026-09-20T08:36
— three days, not the twelve a loose reading of the id range might suggest)
**all report the `integration tests (Docker)` job as `success`**. One of the
15, run `35265373878`, has a `failure` run-level conclusion — its `frontend`
job failed and `e2e` was skipped, but `integration tests (Docker)` still
completed `success`, so it counts toward the figure below; recorded here so
this measurement is not itself a case of reporting only the number that
supports the hypothesis. Each of the 15 runs executed at least three asserted
DELETEs through `RealmProbe.DeleteAsync`. **No asserted DELETE against this
realm has been observed failing across this window.**

What that prior does and does not cover:

- **It covers the directly-planted client.** `RealmProbe.PlantAsync` creates by
  `POST admin/realms/…/clients`, which is the same way
  `An_account_the_provider_creates_holds_it_until_something_removes_it` creates
  its control client. That population is already ~45 asserted deletes green.
- **It does not cover the enrolled client.**
  `A_kiosk_enrolled_at_runtime_…` creates through
  `HttpKeycloakAdminClient.CreateClientAsync`, which also creates a service
  account, assigns scope bundles and strips realm roles. Whether deleting
  *that* shape always answers 2xx is the genuinely open half of the question.

So the expected outcome is **green**, on evidence rather than optimism — and
the one population the evidence does not reach is precisely the one this spec
puts under the assertion.

### 1.5 What the observation cannot see, stated up front

The assertion goes **inside the `foreach`**. If the lookup returns **zero**
clients the loop body never runs and nothing is asserted — a client that was
never created, or that some other agent removed first, produces a silent
success. That blind spot exists in `RealmProbe.DeleteAsync` today and this spec
**does not** close it (ADR-0036: smallest possible change). It is recorded here
so a later reader does not mistake a green run for "a client was found and
deleted". Closing it is a separate, cheap follow-up if anyone wants it.

### 1.6 Why the assertion can fire for a reason that is not a mistake

`HttpKeycloakAdminClient.TryDeleteClientAsync`
(`src/Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.cs:400-427`)
is the **production** resolution of this same defect shape, shipped under #2166
(now closed, Done). It does **not** throw. It returns on 2xx, treats **404 as
the outcome the call wanted** and logs it, and logs anything else as a surviving
residue:

```csharp
// 404 is Keycloak reporting no such client, which is the outcome
// this call wanted. Reporting a residue for it would send someone
// hunting a client that is not there.
if (response.StatusCode == HttpStatusCode.NotFound)
{
    logger.HalfEnrolledClientWasAlreadyAbsent(clientId, clientUuid);
    return;
}

logger.HalfEnrolledClientSurvived(clientId, clientUuid, response.StatusCode);
```

`IsSuccessStatusCode` is **false for 404**. So a 404 here would fail the new
assertion while production has already decided a 404 is fine. That is a
pre-identified, legitimate red with a pre-decided answer (§5, Outcome B) — and
writing it down now is the difference between a considered fix and a
panic-relaxation that looks identical in the diff.

---

## 2. User stories

### US1 (P1) — The observation. *Independently shippable; the whole of this spec's delivery.*

> As the engineer who has to trust this suite, I want the Kiosk file's cleanup
> DELETE to report a non-success status, so that the question "do these deletes
> actually succeed" stops being unanswered and a leaked kiosk-stamped client
> stops being invisible to the sweep test that shares its realm.

**The deliverable is one assertion and one CI reading.** It is valuable
whichever way the reading goes: green settles the question and unblocks the
fold; red is the finding the issue predicts, and is worth more than the
tidy-up.

### US2 (P2) — The fold. **Conditional on US1's reading being green.**

> As the next person to edit these tests, I want one client-delete helper rather
> than two, so that #2182's row 8 closes and the duplication genuinely goes to
> zero.

`DeleteClientAsync` is deleted; its two call sites call
`realm.DeleteAsync(clientId, CancellationToken.None)`.

**This story does not run if US1 reads red.** It is not a fallback and it is not
optional-if-time-permits; it is gated on an observation that has not happened
yet.

### US3 (P3) — The finding. **Mutually exclusive with US2; runs only if US1 reads red.**

> As the maintainer, I want a red here recorded as a finding with its verbatim
> status and client id, so that a real cleanup failure is filed rather than
> smoothed away.

---

## 3. Acceptance scenarios (Gherkin)

The four standard shapes map onto the four answers Keycloak's DELETE can give.
`<the helper>` is `KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync`
after US1.

### Happy path — the DELETE succeeds

```gherkin
Given a client created by the test under its own generated client id
When the finally block runs <the helper> and Keycloak answers 204
Then the assertion passes
And the test's own verdict — pass or fail — is the one reported
```

### Conflict — the DELETE is refused and the client survives

```gherkin
Given a client the test created
When the finally block runs <the helper> and Keycloak answers 409 or 5xx
Then the assertion fails
And the failure message names the client id and the numeric status
And it states that the client is still in the realm and that the next pass
    over this realm will read it as residue it did not create
```

### Bad request / already absent — the DELETE answers 404

```gherkin
Given a client id for which Keycloak's client lookup returned a row
When the DELETE for that row answers 404
Then the assertion fails today, because IsSuccessStatusCode is false for 404
And that is a pre-identified outcome, not a defect in the change:
    HttpKeycloakAdminClient.TryDeleteClientAsync already treats 404 as the
    outcome the call wanted
And the response is §5 Outcome B — accept 404 in both helpers with the
    production precedent cited — not deletion of the assertion
```

### Auth — the admin credentials have gone stale

```gherkin
Given RealmProbe.AdminClientSecret no longer matches the realm import
When <the helper> mints its admin token and the Admin API answers 401
Then RealmProbe.ReadJsonAsync's EnsureSuccessStatusCode throws first,
    before any DELETE is issued
And the new assertion is never reached
And the failure is the one #2182 described: a 401 inside a test that is about
    privileges, whose first reading is "the sweep is broken"
```

### The guard can fail at all — the counterfactual

```gherkin
Given the new assertion is in place
When the DELETE is deliberately pointed at a client uuid that does not exist
Then the assertion fails, and the verbatim failure is captured
And the deliberate break is reverted before anything is committed
```

This last one is not optional. An assertion that has never been seen failing is
the defect this repository has named most often, and the reason phase 4a here is
**red** (§6).

---

## 4. Independent end-to-end test procedure

Not "the tests are green" — the two things a green suite cannot show.

1. **Boot the stack the tests boot.** Check no other Aspire stack is running
   first (one machine, one stack; a second boot produces `FailedToStart` that
   reads exactly like a code defect).
2. **Counterfactual, run first.** Temporarily change the DELETE URL in
   `KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync` to a uuid that
   cannot exist. Run
   `dotnet test tests/Integration.Tests/… --filter "FullyQualifiedName~KioskInheritedPrivilegeIntegrationTests"`.
   **Observe the two tests fail, with the new message, naming the status.**
   Capture the output verbatim. Revert the break.
3. **The real local run.** Same filter, unbroken. Record pass/fail per test.
   This is a **preliminary reading only** — one machine, one fresh realm, one
   pass. It is not the observation.
4. **The observation: CI's `integration` job.** Push, open the PR, let
   `integration tests (Docker)` run the full suite against a fresh Aspire
   Keycloak.
5. **Read the result from the artifact, not from the job's colour.** Download
   the `integration-test-results` artifact and read `integration.trx` for the
   two test outcomes by name. A green *job* is not the same as "these two tests
   ran": a filter that selects nothing, a skipped class or a cancelled run all
   look like something else at the job level.
6. **Confirm the DELETE was actually issued.** Read the trx for both test names
   with an `Outcome` of `Passed`, and confirm the enrolment create succeeded —
   otherwise §1.5's zero-element loop means the assertion was never evaluated.
7. **If red, repeat before concluding.** Re-run the integration job twice. A
   first run after infrastructure churn looks exactly like a regression. Two
   further reds make it a finding; a green makes it flake, and flake is its own
   issue.

---

## 5. The two outcomes, decided now

**The plan branches on an observation that has not happened yet, and this spec
says so plainly rather than forcing a single hypothesis into the tasks.** The
decision rule for each outcome is fixed here so that phase 5 and phase 6 read it
rather than improvise it under the pressure of a red PR.

| Outcome | Reading | What ships | PR body says |
|---|---|---|---|
| **A** | Both tests pass, trx confirms both ran | US1 **and** US2 — the assertion, then the fold, as two commits | "The question is settled: over *N* asserted deletes across the enrolled and the directly-created populations, every DELETE answered 2xx. Row 8 of #2182 is closed and the duplication is zero." |
| **B** | Red, status **404** | US1 amended: **both** helpers accept 2xx-or-404, with `TryDeleteClientAsync`'s 404 comment cited at the call site. Then US2. | "The delete answers 404 for *this* shape. Production already decided 404 is the outcome the call wanted (#2166); the assertion was over-strict and now matches. Why the client was already gone is stated." |
| **C** | Red, status **409 / 5xx / anything else** | US1 only, **unchanged**. US3: the finding. | "These deletes do **not** always succeed. Verbatim failure, status, client id, and which of the two populations. A follow-up issue is filed. This PR does not fold." |
| **D** | Red once, green on re-runs | Nothing yet | Flake. File it; do not fold on a flaky reading. |

**Outcome C is the one with a trap, and the trap is named in ADR-0144.** The
cheapest route to a green PR from C is to delete the assertion, or to soften it
to a log line, and *the log-line form is defensible on its own terms* — it is
exactly what #2166 chose for the production twin. That is what makes it
dangerous: a weakening and a principled alignment-with-production look identical
in the diff.

**So Outcome C escalates.** The lane may not choose between "ship the assertion
red and park the PR" and "ship the logging form and file the failure" on its
own. It reports the verbatim failure, files the issue, applies `agent:blocked`,
and asks. Nothing merges that CI has not passed, and nothing here gets to green
by removing the thing that went red.

---

## 6. Phase-4a colour — **RED, discharged by counterfactual**

This is the decision a later phase must not have to make under pressure, so it
is made here with its reasoning.

**Why not characterisation-green.** Characterisation requires the covering tests
to pass **unmodified** after the change (constitution §Testing; ADR-0144). Here
the covering test's own cleanup path is *the thing being modified*. There is no
"unmodified" state to hold to. #2182 correctly called the *helper extraction* a
characterisation refactor; row 8 is the part of it that is not one, which is
exactly why spec 137 left it out.

**Why not plain red.** Plain red says a new-behaviour test arriving green is a
phase-4 failure. Here **green is the hypothesis, and green is a legitimate,
informative result** (§1.4: 15 consecutive green integration jobs put the
directly-created population at ~45 asserted deletes without a failure).
Demanding the assertion be observed failing against the *real* DELETE would mean
breaking the DELETE and calling it evidence.

**What actually discharges the obligation.** CLAUDE.md: *"ambiguity resolves to
red — that path fails loudly, the other passes quietly."* The obligation red
exists to satisfy is **"this assertion can fail"**, and the established way to
show that here is the counterfactual: construct the thing the assertion claims
to catch and watch it fire (§3's fifth scenario, §4 step 2). Phase 4a therefore
returns **two verbatim outputs** — the counterfactual red, and the reverted real
run — and the PR quotes the first.

**And the red that matters is not a mistake.** The real run may go red in CI for
a reason that is a *finding*, not an error in the change. Future phases must not
treat that as a broken build to be fixed. §5's table is the response; §5's last
paragraph is the boundary.

**US2 carries the other colour.** The fold is behaviour-preserving by
construction — after US1 the two helpers are byte-equivalent in behaviour, so
replacing one with the other changes shape and not outcome. Its colour is
**characterisation, observed green**: the four tests in
`KioskInheritedPrivilegeIntegrationTests` are the covering suite, captured green
by US1's own run, and they must pass **unmodified** after the fold. An assertion
that has to be edited there is evidence the fold moved behaviour — block, do not
adjust. This is the colour #2182 declared, and it is correct for the fold
**once US1 has made the two helpers equivalent**; it was wrong for the fold
before that, which is the whole reason spec 137 deferred it.

---

## 7. Locked tech choices

- **xUnit + Shouldly** (ADR-0052); `ShouldBeTrue(string customMessage)`,
  matching `RealmProbe.DeleteAsync` exactly rather than introducing a second
  spelling.
- **Aspire fixture, not Testcontainers** (ADR-0103). There is no other
  instrument: the question is about a real Keycloak's answer to a real DELETE.
- **Sentence-style test names** (ADR-0053) — unchanged; no new test is added.
- **`CancellationToken` last** (ADR-0049) — `DeleteClientAsync` currently takes
  none and passes `CancellationToken.None` internally; US1 **does not change its
  signature**. US2 removes the method entirely, so the signature question
  resolves itself.
- **No production code changes.** `src/` is untouched by US1 and US2.
- **No new ADR, no constitution amendment** (ADR-0144 blocked outcome).

---

## 8. Out of scope — "Do not"

- **Do not fold before the reading.** US2 is gated on US1's CI result. Folding
  first makes the observation vary two things at once — the assertion *and* the
  helper's admin-client construction, query and disposal — and a red would then
  be unattributable. This is the single most important constraint here.
- **Do not delete, soften, suppress or `[Skip]` the new assertion to reach
  green.** ADR-0144 names this. If it fires, §5 applies.
- **Do not change `RealmProbe.DeleteAsync`** except under Outcome B, where both
  helpers change together for one stated reason.
- **Do not close §1.5's zero-element blind spot here.** Separate change,
  separate evidence (ADR-0036).
- **Do not touch `TryDeleteClientAsync`.** #2166 is closed and its decision
  stands; this spec cites it, it does not revisit it.
- **Do not add a `Category` trait** to either class. Both must keep running in
  CI's integration job — that job *is* the instrument.
- **Do not offer a green local run as the observation.** One machine, one fresh
  realm, one pass. §4 step 4 is the observation.
- **Do not offer a green CI *job* as the reading.** Read `integration.trx` for
  the two tests by name (§4 step 5).
- **Link an issue on its first mention** in each document, as above — the
  convention in specs 186–188. Bare numbers thereafter are fine.
- **Do not claim the deletes "always" succeed** on one green run. The honest
  claim is the measured one: *N* asserted deletes, no failure observed.
