# Spec 361: One client wire row in Identity, and a citation that names its member

**Feature Branch**: `refactor/2939-identity-duplicate-rows` (cut from `origin/develop` at `6f26a43c`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[JUDGEMENT]** (§5).

**Input**: Issue [#2939](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2939).

**Lane.** Autonomous (ADR-0144). **Behaviour-preserving** throughout. Phase 4a is
**CHARACTERISATION, observed green**: the covering tests exist, were observed passing on the
untouched branch (§3), and must pass **unmodified** after the change.

**Spec number.** 361. Checked 2026-10-14 after `git fetch`: `origin/develop` and every remote head
highest 359; local branch `fix/2938-event-cursor` (worktree `sse-2938`, not yet pushed) holds 360.
Re-check before the PR is opened.

**ADRs.** ADR-0036 (smallest change; a refactor changes shape, not behaviour), ADR-0139 / ADR-0144
(characterisation colour, lane), ADR-0091 (no aliases: one name for one thing). **No new ADR is
needed.**

**Latency budget.** N/A — Identity's Keycloak admin client; nothing on the event-to-overlay path
(constitution §IV).

---

## 1. Premise verified (Phase 1 investigation, `6f26a43c`)

### 1.1 Duplicate records — confirmed, and *not* forced by layering

`src/Identity/Infrastructure/KeycloakAdmin/HttpKeycloakAdminClient.WireRows.cs:5,7`:

```csharp
private sealed record ClientRow(string Id, string ClientId, bool Enabled, Dictionary<string, string>? Attributes);
private sealed record ClientDetailRow(string Id, string ClientId, bool Enabled, Dictionary<string, string>? Attributes);
```

- **Identical** in shape, parameter order, nullability and visibility. Neither carries a JSON
  attribute; both deserialise through the same `JsonOptions` (`HttpKeycloakAdminClient.cs:47`,
  `JsonSerializerDefaults.Web`).
- **Same layer, same type.** Both are `private` nested types of the one `partial class
  HttpKeycloakAdminClient` in Infrastructure. Neither is in Application; nothing outside the class
  can see either. The issue's suspected cause — Application unable to reference Infrastructure —
  does not apply, and no NetArchTest rule is involved. No type moves to Shared.
- **Why two exist.** History, not design: `ClientDetailRow` arrived in `214d91ff` (spec 092,
  kiosk-privilege sweep) when `ClientRow` (from `0cf132e6`) still lacked `Enabled`/`Attributes`;
  `ClientDetailRow` gained `Enabled` in `7361bd24` (spec 320) and `ClientRow` gained `Enabled`/`Attributes` in `def5620b` (#2728), converging by accident; `e506c6ea` then moved both into
  `WireRows.cs` side by side unchanged.
- **Users.** `ClientRow`: `TryGetClientRowAsync` (`HttpKeycloakAdminClient.cs:206,213`),
  `IsReplaceableDisabledClient` (`:237`). `ClientDetailRow`: `GetEnrolledKioskClientIdsAsync` and
  `GetStampedClientsAsync` (`HttpKeycloakAdminClient.Sweeps.cs:20,63`). No test refers to either
  name.

### 1.2 Stale citation — confirmed in two places, and its *claim* is stale too

The line number is stale (`HttpKeycloakAdminClient.cs:363` is inside `TryDeleteClientAsync`'s body;
the method is declared at `:316`), and the claim it supports no longer holds:

- `tests/Architecture.Tests/KioskPrivilegeSweepRegistrationTests.cs:136-138` — an assertion
  **message** (a string literal, not a comment): "the best-effort comment in TryDeleteClientAsync
  (HttpKeycloakAdminClient.cs:363) delegates that case here by name."
- `tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs:21-23` — XML doc:
  "The best-effort comment in `TryDeleteClientAsync` (`HttpKeycloakAdminClient.cs:363`) says the
  startup sweep is the backstop for exactly that."

`TryDeleteClientAsync`'s comment (`HttpKeycloakAdminClient.cs:302-314`, rewritten in `83723c49`)
now says the opposite: "**The kiosk-privilege startup sweep is not the backstop this used to
name.**" The sweep still takes the surviving client's *privilege* (realm roles) — so both tests'
substance stands — but nothing in that comment delegates to the sweep by name any more.

The same false attribution ("whose comment delegates the case here by name") appears, without a
line number, in two production doc comments:
`src/Identity/Application/KeycloakAdmin/KioskPrivilegeSweep.cs:20-21` and
`src/Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs:19-21`.

## 2. User story

**US1 (P1) — A maintainer reading Identity's Keycloak client meets one wire row for one Keycloak
client representation, and every pointer to `TryDeleteClientAsync` names the member and says what
that member's comment actually says.**

```gherkin
Scenario: one row type deserialises every client listing (happy)
  Given HttpKeycloakAdminClient.WireRows.cs declares ClientRow and no ClientDetailRow
  When GetEnrolledKioskClientIdsAsync, GetStampedClientsAsync and the create-replace guard run
       against the stubbed Keycloak responses
  Then every characterisation test in §3 passes without modification

Scenario: the replace guard still refuses a conflict (conflict)
  Given an active client already holds the clientId
  When CreateClientAsync runs
  Then it still conflicts (CreateClientReplacementGuardTests, unmodified)

Scenario: a failed listing still throws (bad request)
  Given Keycloak answers the client listing with a failure status
  Then GetStampedClientsAsync throws rather than reporting an empty realm (unmodified test)

Scenario: no citation pins a line number or a claim the code no longer makes
  When the repository is searched for "HttpKeycloakAdminClient.cs:" and for
       "delegates the case here by name" / "delegates that case here by name"
  Then there are no matches
```

Auth: N/A — no endpoint, scope or token path changes.

**Independent test procedure.** `dotnet test tests/Identity.Infrastructure.Tests` with the §3
filter and `dotnet test tests/Architecture.Tests --filter KioskPrivilegeSweepRegistrationTests`,
green before and after with no test file under `tests/Identity.Infrastructure.Tests` touched; the
two greps above return nothing; `dotnet build -c Release` clean.

## 3. Characterisation (observed green on `6f26a43c`, 2026-10-14)

`tests/Identity.Infrastructure.Tests/KeycloakAdmin/` — filter
`EnrolledKioskQueryTests|StampedClientQueryTests|CreateClientReplacementGuardTests|DisabledClientReregistrationTests|HalfEnrolledClientCompensationTests|KeycloakRequestSequenceCharacterisationTests`:
**Passed 53, Failed 0.** These drive the real `HttpKeycloakAdminClient` against
`StubKeycloakHandler` JSON, so each deserialises through the row types:

- `ClientDetailRow` path: `EnrolledKioskQueryTests.Only_a_client_this_system_stamped_as_a_kiosk_is_enrolled`;
  `StampedClientQueryTests.Only_clients_carrying_an_sse_kind_attribute_are_reported_with_their_kind_and_enabled_flag`,
  `…A_failed_listing_throws_rather_than_reporting_an_empty_realm`.
- `ClientRow` path: all five `CreateClientReplacementGuardTests`, both
  `DisabledClientReregistrationTests` (they read `Enabled` and `Attributes` through
  `IsReplaceableDisabledClient`), plus the `TryGetClientUuidAsync` callers in
  `HalfEnrolledClientCompensationTests` / `KeycloakRequestSequenceCharacterisationTests`.

`tests/Architecture.Tests` filter `KioskPrivilegeSweepRegistrationTests`: **Passed 3, Failed 0.**

## 4. Out of scope

Other `File.cs:NNN` citations in Identity tests (e.g. `AspireFixture.cs:279`, `AppHost.cs:30,144`,
`RotateWebhookClientCommandHandler.cs:150-155`) — not named by #2939; a general sweep is its own
issue. `Log.cs:22-34`'s comment already describes the history correctly and is left alone.

## 5. Judgement calls

- **[JUDGEMENT] Keep `ClientRow`, delete `ClientDetailRow`.** `ClientRow` is the shorter name for
  the same thing and has more call sites; the choice is cosmetic.
- **[JUDGEMENT] Correct the claim, not only the line number.** Naming the member while keeping
  "delegates … here by name" would replace a stale pointer with a false one. The two production
  doc comments (§1.2) carry the same false attribution and are fixed in the same comment-only
  commit; they are the pointer #2939 names, without the line number.
- **[JUDGEMENT] The Architecture.Tests edit is not comment-only.** It changes a Shouldly
  `customMessage` string, which is shown only on failure; the assertion (`ShouldContain(typeof(
  KioskPrivilegeSweepHostedService))`) is untouched. The other three edits are comment-only and
  are proven by hash (plan §3).
