# Verification 189 — A cleanup that says when it failed (#2274)

**Latency: N/A.** Test-infrastructure investigation; no production assembly
changed, no leg of constitution §IV's event→overlay path touched.

## What this verifies

This spec is an investigation, not a bugfix: whether
`KioskInheritedPrivilegeIntegrationTests`'s DELETE calls (against
enrolment-created Keycloak clients — a population never checked before)
succeed the way `RealmProbe.DeleteAsync`'s identically-shaped, already-in-
production assertion has been checking for the directly-created population
since 2026-09-07. This note records the reading as observed, per this
repository's own standing practice — a measurement reported only in a PR
body or a conversation is invisible to every later grep.

## Phase 4a — the counterfactual, quoted verbatim (ADR-0139)

Not standard red-then-green: the new assertion is expected to be **green**
if the hypothesis holds, and demanding a real failure would mean breaking
production to manufacture evidence rather than observe it. So the
counterfactual instead proves the assertion is load-bearing — that it can
actually fire — by deliberately breaking the DELETE target against a real,
freshly-booted Aspire stack, then reverting before the real commit.

Full raw output (15,446 lines, `dotnet test` against the real stack,
2026-09-20T11:40:23–11:44:48) is preserved at this session's own scratchpad;
the two failures it produced, quoted exactly as they appear there:

```
  Failed SmartSentinelEye.Integration.Tests.Identity.KioskInheritedPrivilegeIntegrationTests.A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege [3 s]
  Error Message:
   Shouldly.ShouldAssertException : deleted.IsSuccessStatusCode
    should be
True
    but was
False

Additional Info:
    removing probe client 'kiosk-privilege-probe-01a0be33cbfe7d4a9530c62fd28c2fce' answered 404; it is still in the realm, stamped as this suite planted it, and the next pass over this realm will read it as residue it did not create
  Stack Trace:
     at SmartSentinelEye.Integration.Tests.Identity.KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync(String clientId) in D:\Github\sse-2274\tests\Integration.Tests\Identity\KioskInheritedPrivilegeIntegrationTests.cs:line 192
   at SmartSentinelEye.Integration.Tests.Identity.KioskInheritedPrivilegeIntegrationTests.A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege() in D:\Github\sse-2274\tests\Integration.Tests\Identity\KioskInheritedPrivilegeIntegrationTests.cs:line 55

  Failed SmartSentinelEye.Integration.Tests.Identity.KioskInheritedPrivilegeIntegrationTests.An_account_the_provider_creates_holds_it_until_something_removes_it [1 s]
  Error Message:
   Shouldly.ShouldAssertException : deleted.IsSuccessStatusCode
    should be
True
    but was
False

Additional Info:
    removing probe client 'control-probe-01a0be33da8070c49e6a4555b03d20ad' answered 404; it is still in the realm, stamped as this suite planted it, and the next pass over this realm will read it as residue it did not create
  Stack Trace:
     at SmartSentinelEye.Integration.Tests.Identity.KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync(String clientId) in D:\Github\sse-2274\tests\Integration.Tests\Identity\KioskInheritedPrivilegeIntegrationTests.cs:line 192
   at SmartSentinelEye.Integration.Tests.Identity.KioskInheritedPrivilegeIntegrationTests.An_account_the_provider_creates_holds_it_until_something_removes_it() in D:\Github\sse-2274\tests\Integration.Tests\Identity\KioskInheritedPrivilegeIntegrationTests.cs:line 94

Test Run Failed.
Total tests: 4
     Passed: 2
     Failed: 2
 Total time: 3,6180 Minutes
```

Exactly the two tests that call `DeleteClientAsync` failed — one for the
enrolment-created population (`kiosk-privilege-probe-...`), one for the
directly-created control (`control-probe-...`) — and the two tests in the
same file that never call it (`A_wall_display_account_does_hold_it`,
`An_operator_does_not_hold_the_long_lived_credential_privilege`) passed
unaffected. The break was reverted before the real commit (`8f396e9f`
contains only the assertion; `git diff` confirmed no altered client id or
URL remained).

**Note on the failure mode observed**: both counterfactual failures are
404s — the one status this spec's own §1.6 pre-identifies as ambiguous
(`HttpKeycloakAdminClient.TryDeleteClientAsync`, #2166, treats a 404 as an
accepted, non-failure outcome for the production twin of this code). The
counterfactual still discharges what it needs to — proving the assertion
*can* fire, not proving every possible failure mode is equally severe — but
a break that produced a 409 or 5xx would have been a stronger demonstration
of the sharper end of this spec's decision table. Recorded rather than
silently generalized past.

**T006 (a preliminary local run of the real, unbroken assertion) was not
executed**, and neither was a local run of the post-fold suite. Two
consecutive local `AspireFixture` boots on this machine encountered real OS
memory pressure from a separate, unrelated ~2-day-old orphaned Docker stack
left by another session — not this repository's own code being flaky. Per
this spec's own design (§4 step 4), the real verdict for both the pre-fold
assertion and the post-fold fold was always meant to come from CI's
blocking `integration` job reading the actual trx artifact, not from a
local run — this is a disclosed, anticipated deviation, not a silent gap.

## Phase 5 — the observation itself: CI run 35503359310

PR #2463 was opened with only the new assertion in place (commit
`8f396e9f`, head SHA of this CI run), deliberately before knowing the
outcome — the PR is the instrument, not a claim already settled.

Downloaded and parsed directly (not read from the job's pass/fail colour
alone): `tests/Integration.Tests/TestResults/integration.trx` from run
`35503359310`. The `integration tests (Docker)` job itself: `success`, 14
steps executed (not a zero-step allocation). The two tests this spec exists
to check:

```
testName="...KioskInheritedPrivilegeIntegrationTests.A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege" ... outcome="Passed"
testName="...KioskInheritedPrivilegeIntegrationTests.An_account_the_provider_creates_holds_it_until_something_removes_it" ... outcome="Passed"
```

Both `Passed`, for real, against a genuinely enrolment-created Keycloak
client (the population that had never been checked). The other two tests in
the file (which never call the delete helper) also passed, as expected.

**Classification: Outcome A (green)**, per spec §5's table. On this one
run, both asserted deletes — one per population — answered success; no
failure was observed. This is a measured result for run `35503359310`, not
a claim that the deletes "always" succeed (spec §8 and plan §5 both
explicitly forbid that overclaim from a single green run) — it is one
sample of each population, joining the ~45 prior samples of the
directly-created population that this same assertion shape has already
accumulated in production since 2026-09-07.

## Phase 4b — the fold, and its own confirmation

Given Outcome A, `KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync`
was deleted entirely and both call sites now call `RealmProbe.DeleteAsync`
directly (commit `fcd2908d`). Verified line-by-line against the deleted
method (`git show origin/develop:tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs`):
identical admin-client construction, identical lookup URL and escaping,
identical delete-by-internal-`id` loop, identical `CancellationToken.None`
usage. The only behavioural delta — the assertion — was already proven to
hold by the CI run above, before the fold existed. This is confirmed a
true no-op substitution, not merely asserted so.

`RealmProbe.ReadJsonAsync` remains `public static`, still consumed
elsewhere (`CrossFabDisableIntegrationTests.cs`,
`CrossFabWebhookRotationIntegrationTests.cs`); unaffected by this change.

This PR's own next CI run (post-fold, tip `fcd2908d`) is the confirming
run for the fold itself — watched before merge, all four buckets read by
hand per this repository's own "`develop` has no required status checks"
convention.

## What was corrected in this note, and why

An earlier draft of this PR's body paraphrased the counterfactual's client
ids and elided the second failure's message, labelling the result
"verbatim" when it was not, and in a separate section stated that "the
original counterfactual" run was never locally obtained — directly
contradicting the paraphrased block above it. Both defects are fixed here:
the text above is copied from the real log file, and the "not obtained"
disclaimer is now scoped correctly to what was actually not obtained (T006
and the post-fold local run), not the counterfactual itself, which was
run and is preserved. Recorded plainly rather than quietly re-issued,
since a spec about "nobody had looked" should not itself ship an
unlooked-at contradiction.
