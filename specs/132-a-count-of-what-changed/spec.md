# Spec 132 — A count of what changed

**Issue:** #2169 — *The sweep reports 'stripped N of N' on every start, counting kiosks reached not accounts changed*
**Branch:** `fix/2169-a-count-of-what-changed`
**ADRs:** 0037 (phases), 0139 (red first), 0144 (autonomous lane), 0036 (smallest change),
0050 (`[LoggerMessage]`, structured fields), 0134 §Decision 1 (the privilege is taken back),
0105 (`Ensure.That`), 0049 (`CancellationToken` last)

## The defect, restated

`KioskPrivilegeSweep` logs

> `Stripped inherited realm privileges from {StrippedCount} of {KioskCount} enrolled kiosk accounts.`

and **neither number counts an account that lost anything**. `StrippedCount` is
`kiosks.Count - unreachable.Count` — kiosks *reached*. `KioskCount` is kiosks
*enumerated*. `HttpKeycloakAdminClient.StripInheritedRealmRolesAsync` returns
early on `assigned.Length == 0` and tells nobody, so the one fact the line
claims to report is computed and discarded one frame below the logger.

The consequence is that the line appears on **every** start of any realm that
holds a kiosk, saying "stripped" about a pass that stripped nothing. Found by
spec 092's phase 5, running that spec's own verification procedure against a real
boot: after the residue was stripped on one boot, restarting Identity logged the
identical `1 of 1`.

## Why it matters — the silence was bought and this spends it

Spec 092 deliberately silenced the empty case (`kiosks.Count > 0`), so that a
line appearing *means something happened*. That silence is load-bearing twice
over: it is why phase 5's verification has to plant a residue first, and it is
why an empty realm proves nothing.

But the guard is on the count of **kiosks**, not on the count of **strips**. An
operator reading `Stripped … from 12 of 12 enrolled kiosk accounts` on every
restart cannot tell it from a boot where twelve accounts genuinely lost
privileges. The signal is diluted exactly where it would be read.

**And it leaves a written contradiction behind.** `specs/092/spec.md` step 9 says
the line is not logged on the second restart; a phase-5 block directly under it
says step 9 is false, records why, and hands the fix here by name. Two adjacent
paragraphs that disagree is the shape this repository has had to correct before
(CLAUDE.md §Phase 3, constitution §IV). Whichever way this spec moves the
behaviour, one of those two paragraphs has to stop being true, so the correction
belongs in this pass and not a later one.

## The shape chosen, and the one not chosen

The issue offers two shapes and prefers the second. **Both are taken, because
they are not exclusive**, and taking only the second would breach the issue's own
"do not fix it by removing the count":

- **Report both** — `N of M`, `N` accounts changed, `M` accounts examined. The
  numerator's meaning is what was wrong; the denominator stays, and stays useful:
  `1 of 12` says one residue among twelve, which a bare `1` does not.
- **Stay silent at zero** — the guard moves from `kiosks.Count > 0` to
  `strippedCount > 0`. This is what makes the line mean exactly one thing:
  something was repaired.

What is **not** done: removing the count, and removing the line. A test asserting
only that the line is absent would be satisfied by deleting the logging
altogether, which would destroy phase 5's only evidence that the sweep ran at
boot. Both directions are asserted.

## Where the information comes from

`IKeycloakAdminClient.StripInheritedRealmRolesAsync` returns `Task`. It becomes
`Task<bool>` — **true when the account held directly-assigned realm privileges
and they were removed, false when it already held none**. That is exactly the
value `HttpKeycloakAdminClient` already computes at
`if (assigned.Length == 0) return;` and throws away.

`bool` and not a value object: §II binds *state on a domain model*, and this is
an Application port answering a question about a call it has just made — the same
category as the `IsRevoked` / `Contains` returns §II names as outside the rule,
not an exemption to it. The port's vocabulary is already `string clientId`, for
the reason `Shared.Contracts` is exempt: it translates a provider's wire shape.

## Scope

**In:** the port's return value and its implementations; `KioskPrivilegeSweep`'s
count and its guard; `KioskSweepOutcome`; `specs/092/spec.md` step 9 and the
block under it.

**Out, with reasons:**

- **The sweep's repair behaviour.** Nothing may stop being stripped, and nothing
  new starts being stripped. This spec changes what is *reported*.
- **The sweep's scope.** #2166 established that `GetEnrolledKioskClientIdsAsync`
  cannot tell a residue from a healthy kiosk — both carry `sse.kind=kiosk` — and
  that the sweep strips only, never deletes or flags. That is the mechanism
  behind the observation, and it is deliberately unchanged.
- **The unreachable warning.** `CouldNotSweepKiosk` already names each kiosk the
  pass could not read. An account that failed is neither stripped nor known to be
  clear, so it is absent from the numerator and present in that warning, which is
  where a reader already looks for it.

## User stories

### US1 — a pass that repairs nothing says nothing

```gherkin
Given the realm holds an enrolled kiosk whose account holds no direct realm role
When the sweep runs
Then no completion line is logged
```

*Today this logs `1 of 1`. This is the red.*

### US2 — a pass that repairs something says how much

```gherkin
Given the realm holds two enrolled kiosks, one of which still holds the privilege
When the sweep runs
Then exactly one completion line is logged
  And its StrippedCount field is 1 and its KioskCount field is 2
```

*Today this logs `2 of 2`. This is the second red, and it is what stops US1 from
being reachable by deleting the line.*

## Success criteria

1. A sweep over kiosks that hold nothing logs no `SweptKioskPrivileges` event,
   having enumerated the realm — asserted, so the silence cannot be for the wrong
   reason.
2. A sweep that strips one of two logs exactly one event carrying
   `StrippedCount=1`, `KioskCount=2` — asserted on the **structured fields**
   (ADR-0050), which is what an OTLP sink receives and what an operator queries
   by.
3. Every kiosk enumerated is still stripped, one call each, and the five
   pre-existing `KioskPrivilegeSweepTests` pass unmodified.
4. `specs/092/spec.md` step 9 reads true, and the block recording it as false is
   replaced by one recording when it became true and how.
5. `dotnet build -c Release` clean.

## Honesty block — what green proves, and what it does not

- **It proves the sweep counts what the port tells it**, not that the port tells
  the truth. The `assigned.Length == 0` branch is `HttpKeycloakAdminClient`'s and
  is covered by `EnrolledKioskQueryTests`'
  `A_second_removal_sends_nothing_when_the_account_holds_no_realm_role`, over a
  stub transport. Nothing at unit level can prove Keycloak agrees.
- **It does not prove the line reaches a sink.** `CapturingLogger` stands where
  the OTLP exporter would. The end-to-end claim is spec 092's phase 5 procedure,
  which this spec corrects rather than re-runs.
- **`A_pass_over_a_kiosk_that_holds_nothing_says_nothing` alone could not fail
  usefully.** It passes over a sweep with no logging at all. US2 is what makes it
  a real assertion, and the pair must be read together.

## Latency

**§IV is not touched.** The sweep is an `IHostedService` that runs once at
Identity start, against the identity provider's admin API. It is on no leg of
`event arrival → overlay rendered`: not camera→SFU, not SFU→kiosk decode, not the
presentation buffer, not event→overlay state, not composite+render. The change is
one `bool` returned and one comparison; it adds no call and removes none.
