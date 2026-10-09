# Spec 325 — The guess space the lockout outlasts

**Issue:** #2510 — "The dev realm's password policy is exhaustible within #2285's own lockout math"
**Branch:** `fix/2510-strengthen-dev-password-policy` · **Worktree:** `D:\Github\sse-2510`
**Lane:** autonomous (ADR-0144). **The remedy was decided by a human** — issue comment,
2026-10-08: *"strengthen the dev realm's password policy so #2285's own lockout math actually
holds, rather than accepting it as a dev-only risk."* This spec computes the values that decision
needs; it does not re-open the decision.
**Predecessors:** spec 207 (#2285, the lockout), spec 219 (#2510 Half A/B — measured the gap and
deliberately changed nothing), spec 324 (#2508, master-realm lockout).
**ADRs:** ADR-0007 / ADR-0008 (`docs/adr/0000-initial-decisions.md` rows 007–008, Keycloak
realm), ADR-0036 (smallest change, surface assumptions), ADR-0037 (phases), ADR-0052/0053
(xUnit + Shouldly, sentence names), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0109
(`[P]` = disjoint files), ADR-0139 (rules that fail the build), ADR-0144 (lane limits: no gate
weakening, 4a colours).
**Constitution:** §VIII (safe by default at trust boundaries), §Testing (new behaviour starts red),
NFR §Security.
**New ADR needed:** no — see plan §1.
**Latency budget:** N/A. Login / realm configuration only; no §IV leg is touched.

---

## Problem (re-verified at `dc88224c`)

`src/AppHost/Realms/smart-sentinel-eye-realm.json:20`:

```
"passwordPolicy": "length(8) and upperCase(1) and lowerCase(1) and digits(1)"
```

Spec 219 measured what the lockout (`:11-18`) actually admits on the running stack
(`specs/219-the-guess-a-username-already-made/verification.md`, three stable runs):
burst locks at **2** attempts, paced at **10**, first lock lasts **≈61 s**; with
`waitIncrementSeconds: 60` growing toward `maxFailureWaitSeconds: 900`, a sustained attacker gets
**≈40 attempts/hour per account**, and `maxDeltaTimeSeconds: 43200` means the counter never
resets while the attack continues. The detector is **per account**; nothing limits an attacker
across accounts.

Against that budget the realm fails on two independent counts (plan §2 has the arithmetic):

1. **The policy admits a guess space a year of sustained guessing covers.** Its cheapest
   human-compliant shape (`Capitalised word + digits`, 8 chars) is on the order of 10^5–10^6
   candidates, heavily head-weighted; ≈346 000 guesses/account/year exhaust it.
2. **The seeded credentials are not in that space at all — they are in a space of ~1.** Seven of
   twelve are `Capitalise(local-part(username)) + {1234,-1234,_1234}`; one value
   (`Operator1234`) opens six accounts across four fab groups, which multiplies the attacker's
   per-secret budget by six (six independent lockout counters against one secret).

## Decision encoded by this spec

| Item | Value |
|---|---|
| New `passwordPolicy` | `length(15) and upperCase(1) and lowerCase(1) and digits(1) and specialChars(1) and notContainsUsername and notEmail` |
| Seeded human credentials | **twelve distinct values**, none derived from any username, fab or role, each 21–25 chars, alphabet `[A-Za-z0-9-]` (plan §3 table) |
| Lockout block (`:11-18`) | **unchanged** |

Every clause is verified to exist in the pinned Keycloak 26.6.4 image
(`org.keycloak.keycloak-server-spi-private-26.6.4.jar`, `META-INF/services/...PasswordPolicyProviderFactory`):
`notContainsUsername` lower-cases both sides then `contains`; `notEmail` is `equalsIgnoreCase`;
`specialChars` counts `!Character.isLetterOrDigit`.

**Gate question D1 (recommendation taken, reviewer may overrule):** de-duplicating the seeded
values (twelve distinct, not one rotated `Operator…` shared by six) is part of "the lockout math
holds", because reuse divides the lockout's protection by the number of accounts sharing the
secret and lets one guess cross four fabs. A rotation that keeps the reuse is the exact repair
spec 219's phase-6 review warned would re-certify the defect with a green test. Fallback if
overruled: rotate in place (six distinct values) — cost and test inversions listed in plan §7.

## Out of scope

- `passwordBlacklist` (needs a file mounted under `/opt/keycloak/data`, which the persistent
  `keycloak-data` volume shadows) — follow-up issue to be filed at phase 7.
- The master realm (`master-realm.json` declares no `passwordPolicy`; its single bootstrap admin
  is an Aspire parameter, not a seeded literal) — follow-up issue.
- `management-web`'s open ROPC (the other #2285 follow-up).
- Historical spec artefacts (`specs/0xx–324/{spec,plan,tasks,verification}.md`) that quote the
  old values — they are records of what was true, not runbooks. Same for the three comments in
  `scripts/` that quote a since-deleted `e2e/wall-authority.spec.ts` line.
- Re-measuring spec 219's Half B throughput — the lockout block does not change.

---

## User stories

### US1 (P1, the whole slice) — a guess space the lockout outlasts

**As** whoever stands up the first fab from this realm,
**I want** the realm to refuse any password shorter than 15 characters, lacking a character
class, or containing the account's username or equal to its email — and to seed no credential
that a username implies or that another account shares,
**so that** the lockout's ≈40 guesses/account/hour cannot cover the space in any realistic window.

Independently shippable: one realm file plus its consumers, observable end to end by signing in.

## Acceptance scenarios

File-level facts run in `Architecture.Tests` (no stack); live facts against the Aspire stack
(ADR-0103).

**AS-1 — the running realm refuses a password the old policy admitted (happy path of the fix).**
```gherkin
Given a throwaway account created in the running realm through the master-realm admin client
 When its password is reset to "Probe1234Abcd" (13 chars, upper+lower+digit, no special)
 Then Keycloak answers 400 and the account's password is unchanged
  And the correct, compliant probe password still yields a token
```

**AS-2 — each new clause refuses on its own (bad request).**
```gherkin
Given the same throwaway account, username "policy-probe-<guid>",
      email "Probe-Mail-<n>@policy.test" (itself 15+ chars and class-compliant)
 When its password is reset to a 15+ char value missing only a special character
  Or to "Xy-9" + its own username upper-cased (compliant except notContainsUsername)
  Or to its own email address, verbatim (compliant except notEmail)
 Then each reset answers 400 and the probe's compliant password still yields a token
```

**AS-3 — the running realm declares exactly what the file declares (drift control).**
```gherkin
Given the stack has imported the realm
 When the realm representation is read through master-realm admin / admin-cli
 Then its passwordPolicy equals the import file's string verbatim
```
(Spec 219's SC-6 checked this only in the CI-excluded `Measurement` category; this one runs in CI.)

**AS-4 — every seeded account still signs in (no regression).**
```gherkin
Given a fresh keycloak-data volume
 When the full Integration.Tests suite and the e2e suite run
 Then every seeded sign-in succeeds with its new password
```

**AS-5 — the file proves the seeded set is out of reach (build-enforced).**
```gherkin
Given the realm import's declared policy, parsed (never hard-coded)
 Then every seeded password satisfies it
  And zero seeded passwords are Capitalise(local-part(username)) + {1234,-1234,_1234}
  And no seeded password is held by more than one account
  And every one of the six retired values is refused by the declared policy
```

**AS-6 — the sweep is complete (consistency guard).**
```gherkin
Given README.md, specs/*/quickstart.md, tests/, e2e/ and src/
 When they are scanned for the six retired seeded passwords
 Then none contains one
```

**Auth.** Resetting a password through the Admin API without a realm-management role is refused
— Keycloak's own guarantee, unchanged; the live facts use master-realm `admin` / `admin-cli`
(spec 207 phase 6: `identity-admin` returns a partial realm representation).

**Conflict.** No write-conflict surface (no endpoint, aggregate, `If-Match`, `Idempotency-Key`).
Probe accounts are `policy-probe-{Guid:N}`, created and deleted per fact, inside
`[Collection(AspireCollection.Name)]`, so they never contend with each other or a seeded account.

## Independent end-to-end test procedure

1. `docker volume rm` the `keycloak-data` volume (realm edits are not re-imported otherwise —
   `AppHost.cs:165-169`), boot the AppHost.
2. Sign in to `management-web` as `operator` with the **old** password → refused; with the new
   value from `README.md` → signed in.
3. In the Keycloak admin console, set `operator`'s password to `Operator1234` → refused with the
   policy's length message.
4. Run `dotnet test tests/Architecture.Tests --filter SeededCredentialStrengthTests` and the
   new live class (plan §5); quote output in the PR.

## Assumptions (marked, to be checked at phase 5)

- **A1 — UNVERIFIED:** whether Keycloak 26.6.4 enforces `passwordPolicy` on credentials in a
  realm *import*. Either way is safe: AS-5's first clause guarantees compliance in the file, so a
  validating import cannot fail and a non-validating one changes nothing. Phase 5 records which.
- **A2:** breach-corpus proportions in plan §2 are order-of-magnitude estimates; plan §2 gives a
  reproduction method rather than a citation.
