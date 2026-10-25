# Spec 460: A disabled client is never logged as rotated

**Issue:** #3139 (found by the #3125 backend review, finding S5) · **Branch:** `fix/3139-rotate-race-log`
**ADRs:** ADR-0050 (`ILogger<T>` + `[LoggerMessage]`, structured fields; the log line is the subject),
ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane). ADR-0040/0185 read and **not engaged**: no event,
outbox or contract changes.
**Specs built on:** 426 (create-branch race-disable), 443 (a disabled client is never *announced* rotated),
451 (the rotate-branch race-disable), 454 (replay refuses a rotated client; same handler, untouched).
**Lane:** autonomous · **Phase-4a colour:** **red** (behaviour-changing). One characterisation literal that pins the
old line on the create branch changes on purpose — see plan §Phase 4a.
**Security-sensitive:** no (log content only; no secret, scope or auth path touched). **New ADR needed:** no.
**Contract change:** none (`Shared.Contracts`, HTTP shape, migrations, AppHost untouched).
**Latency budget:** N/A — Identity admin path, not on the event→overlay path.
**Written 2026-10-25 against `develop` at `aa32b550`.**

---

## Premise, checked at `aa32b550` — holds

1. `RotateWebhookClientCommandHandler.HandleAsync` gates the publish and commit on `!disabledDuringRace`
   (`RotateWebhookClientCommandHandler.cs:92-115`) but calls `logger.RotatedWebhookIntegration(...)` **unconditionally**
   at `:117`, after the gate.
2. So every exit on which `DisableIfRevokedSinceCommitAsync` returns `true` writes, in order, a race line
   (`DisabledClient{Created,Rotated}DuringRevokeRace`, `AlreadyDisabledConcurrently...`, or
   `CouldNotDisable...` followed by the Keycloak-already-disabled commit) **and then** "Rotated webhook integration
   'qa' to clientId 'webhook-qa'." — the incoherent pair spec 443/451 removed from the event stream.
3. Pinned today on the create branch only:
   `RotateWebhookClientOrderCharacterisationTests.A_revoke_landing_during_the_create_disables_the_client_and_neither_publishes_nor_commits`
   asserts the line `ShouldHaveSingleItem()` (`:313-314`). No rotate-branch test asserts it either way.
4. The HTTP answer is unaffected and stays 200 with the credentials (the create/rotation itself succeeded); only the
   log line moves.

## User story

### US1 (P1) — An operator reading the logs never sees "rotated" for a client the same call disabled

As an operator investigating a revoke/rotate race, I read the Identity logs for one rotate request and see a single
coherent outcome: either the client was rotated (and announced), or it was disabled during the revoke race — never
both.

**Independent test:** run `RotateWebhookClientOrderCharacterisationTests` (unit, in-memory harness with
`CapturingLogger`). Each disabled exit asserts `Logger.Named("RotatedWebhookIntegration")` is empty; each
not-disabled exit (happy path, Unverifiable-on-rotate, failed-disable-Keycloak-never-disabled) still asserts exactly
one entry with the unchanged message text.

### Acceptance scenarios

```gherkin
Scenario: happy path — rotation with no race still logs rotated
  Given an existing webhook client "webhook-qa" at version 3
  And the integration status reads Active before and after the save
  When the rotation runs
  Then exactly one "Rotated webhook integration 'qa' to clientId 'webhook-qa'." Information entry is logged

Scenario: rotate branch, revoke lands during the rotation (this call disables the client)
  Given an existing client and statuses Active then Revoked
  When the rotation runs
  Then "DisabledClientRotatedDuringRevokeRace" is logged once
  And no "RotatedWebhookIntegration" entry is logged
  And the result is still success carrying the rotated secret

Scenario: create branch, revoke lands during the create
  Given no existing client and statuses Active then Revoked
  When the create runs
  Then "DisabledClientCreatedDuringRevokeRace" is logged once
  And no "RotatedWebhookIntegration" entry is logged

Scenario: conflict — the async revoke handler already disabled the client (NotFound or DbUpdateConcurrencyException)
  Given the inner disable answers WebhookClientNotFound, or throws DbUpdateConcurrencyException
  When the rotation (or create) runs
  Then "AlreadyDisabledConcurrently..." is logged once
  And no "RotatedWebhookIntegration" entry is logged

Scenario: Keycloak disabled the client but the row's save failed
  Given the inner disable reaches Keycloak and the second save throws a non-concurrency DbUpdateException
  When the rotation runs
  Then "CouldNotDisableClientRotatedDuringRevokeRace" is logged once
  And no "RotatedWebhookIntegration" entry is logged

Scenario: failed disable where Keycloak never disabled the client — still rotated
  Given the inner Keycloak disable throws (client stays enabled)
  When the rotation runs
  Then WebhookIntegrationRotatedV1 is published once
  And exactly one "RotatedWebhookIntegration" entry is logged

Scenario: bad request — invalid integration name, revoked or stale precondition
  When the rotation is refused before any write
  Then no "RotatedWebhookIntegration" entry is logged (unchanged: those exits return before the log call)

Scenario: auth — unchanged
  The endpoint's RequireScope / fab authorization is not touched by this spec; no new auth scenario.
```

## Functional requirements

- **FR-001** The "Rotated webhook integration" log line is written **iff** `WebhookIntegrationRotatedV1` is
  published for that call — i.e. only when `disabledDuringRace` is `false`.
- **FR-002** The message template, level, and event name of `Log.RotatedWebhookIntegration` are unchanged.
- **FR-003** No other observable changes: call order (`harness.Log`), published messages, commits and the HTTP result
  are identical on every exit.

## Out of scope

- Adding a replacement "rotation completed but client disabled" line — the race lines already state the outcome.
- Any change to the race-disable policy, compensation bound, or idempotency replay (specs 434/451/454).
