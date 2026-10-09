# Spec 320 — The client a cancel left behind

**Issue:** [#2181](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2181)
— *An aborted `POST /devices/register` leaves a live Keycloak client with no
`RegisteredClient` row, and re-registering that device then fails forever.* Filed as the
residue of spec 093 (§"The residue", T007).

**Branch:** `fix/2181-orphaned-keycloak-client-on-aborted-register` (cut from `origin/develop`).
**Lane:** autonomous (ADR-0144). Feature-level issue: see tasks.md header.

**Spec number.** 320. On 2026-10-09 the highest number on `origin/develop` and on every
remote branch was 319 (`319-the-gray-that-forgot-the-video`); 317 and 318 are each claimed
twice. **Re-check before opening the PR**, and again after every parked-PR merge.

**Decision taken before this spec (user, 2026-10-08 and 2026-10-09, issue comments).** A
**reconciliation sweep**, mirroring `KioskPrivilegeSweep` (spec 092). The register and enrol
handlers keep their create-then-save order, for the reason
`RotateWebhookClientCommandHandler.cs:167-171` records. A compensating delete and
reserve-then-create were **declined**. This spec implements the decision and does not reopen
it.

**ADRs and constitution sections referenced:**

- **ADR-0134 Decision 1 / spec 092**: the startup-sweep precedent. Its shape (pass in
  Application, hosted wrapper in Infrastructure, per-client failure isolation, silent when it
  repaired nothing, a failure never stops the host) is reused here.
- **ADR-0142** (`Idempotency-Key`). It does **not** help. The orphan exists before any key
  outcome is recorded, and a keyed retry hits the same `KeycloakClientAlreadyExistsException`.
- **ADR-0143** (only idempotent methods are retried). It removed the *retried* cause (spec 093).
  The *cancellation* cause remains, and that is this spec's population.
- **ADR-0041** (`IKeycloakAdminClient` seam). Two reads are added to it (plan §3).
- **ADR-0016 / constitution §III**: Identity only. No other context changes.
- **Constitution §VIII** (safe by default): the sweep only touches clients **this system
  stamped** as device or kiosk, never a realm or person client (§4).
- **ADR-0103** (integration tests on the Aspire fixture), **ADR-0139 / ADR-0144** (red first),
  **ADR-0036** (smallest change), **ADR-0105** (`Ensure.That`), **ADR-0141** (`Option<T>` in
  Application), **ADR-0049** (`CancellationToken` last), **ADR-0050** (`[LoggerMessage]`),
  **ADR-0109** (`[P]`).

**No new ADR.** See §6.

**Latency budget (§IV): N/A.** See §7.

---

## 1. The issue's premise, re-checked on this tree (2026-10-09)

| Claim in #2181 | On this tree |
|---|---|
| `RegisterDeviceCommandHandler` creates the Keycloak client, then saves the row, as two steps | **True.** `CreateClientAsync` at `:73-77`, `clients.SaveAsync` at `:90`. |
| A cancellation between them leaves a client with no row | **True.** The catch is `when (ex is not OperationCanceledException)` (`:83`), so a cancelled `SaveAsync` propagates with the client already created. |
| `EnrollKioskCommandHandler` has the same hole | **True.** `:51-66`, `ClientKind.Kiosk`, same filter. |
| Re-registration collides forever | **Partly stale, and worse in one way.** `GetByClientIdAsync` finds no row, then `HttpKeycloakAdminClient.CreateClientAsync`'s probe finds the **enabled** orphan. `IsReplaceableDisabledClient` (#2728) replaces only *disabled* clients, so an enabled orphan still throws `KeycloakClientAlreadyExistsException` → `DEVICE_ALREADY_REGISTERED` / `KIOSK_ALREADY_ENROLLED`. **True as filed.** |
| Nothing else cleans it up | **True, and the code says so.** `HttpKeycloakAdminClient.TryDeleteClientAsync`'s doc ends *"that kiosk cannot be enrolled again until someone deletes the client by hand. Nothing else will."* `KioskPrivilegeSweep` strips roles but leaves the client. |
| A population the issue does not name | **Found while reading.** #2728's replace path deletes a *disabled* client and creates a fresh enabled one, then the handler saves a new row. A cancellation there leaves an **enabled** client next to only a **disabled** row. `GetByClientIdAsync` ignores disabled rows, so this is the same orphan in every respect that matters. The orphan definition below covers it (§3). |
| A second population the issue does not name | **Found while reading.** A `CreateClientAsync` that fails after the `POST /clients` (group assign, strip or secret read) calls `TryDeleteClientAsync`. When that delete also fails, an enabled, stamped client survives with no row, because the handler returned a failure without saving. Same orphan, same cure. |

## 2. User stories

### US1 (P1): An orphaned device or kiosk client is neutralised, and its identifier can be registered again

A sweep runs when the Identity API starts and then once an hour. It finds every **enabled**
Keycloak client stamped `sse.kind=device` or `sse.kind=kiosk` that has **no active
`RegisteredClient` row** with the same client id, and whose service account is **older than
the grace window (10 minutes)**. It **disables** each one. The orphan's credential stops
working. Because the client is now disabled and keeps its `sse.kind` and `sse.fab`, the next
registration of that identifier in the same fab takes #2728's existing replace path and
succeeds.

**Why P1:** it is the defect, and it is observable end to end on its own. Plant an orphan,
show re-registration fails, run one pass, show it succeeds.

```gherkin
Background:
  Given the running Aspire stack
  And an operator holding /fabs/munich with sse.identity.devices.write and sse.identity.kiosks.write

Scenario: the defect — an orphaned device client is disabled and the device can register again
  Given a Keycloak client "plc-orphan-<n>" stamped sse.kind=device, sse.fab=munich, enabled,
        with a service account, and no registered_clients row       # what an aborted register leaves
  And POST /devices/register {deviceType: plc, deviceIdentifier: orphan-<n>, fabId: munich}
      answers 409 DEVICE_ALREADY_REGISTERED                         # control: the defect is present
  When one sweep pass runs with the clock 11 minutes past the client's creation
  Then Keycloak reports "plc-orphan-<n>" enabled=false
  And a client_credentials grant with the orphan's secret is refused
  And Identity logs a Warning naming "plc-orphan-<n>", kind device, and its age
  And POST /devices/register for the same device then answers 201 with a fresh secret
  And a client_credentials grant with that fresh secret succeeds

Scenario: the same for a kiosk
  Given a Keycloak client "kiosk-orphan-<n>" stamped sse.kind=kiosk, sse.fab=munich, enabled, no row
  When one sweep pass runs past the grace window
  Then it is disabled, and POST /kiosks/enroll for it then answers 201

Scenario: the re-registration population — an enabled client next to only a disabled row
  Given a device registered, then disabled (row DisabledAt set, client disabled)
  And its client re-created enabled by #2728's replace path, with no new row saved   # aborted re-register
  When one sweep pass runs past the grace window
  Then the client is disabled

Scenario: conflict — a registration still in flight is not touched (grace window)
  Given an enabled, stamped device client with no row, whose service account was created 2 minutes ago
  When one sweep pass runs
  Then the client stays enabled, and no Warning is logged for it
  # the in-flight case: CreateClientAsync returned, SaveAsync has not committed yet

Scenario: conflict — a registered client is never touched
  Given a device registered through POST /devices/register (row active, client enabled)
  When one sweep pass runs with the clock a day later
  Then the client stays enabled

Scenario: conflict — clients this system did not stamp as device or kiosk are never touched
  Given enabled clients with no sse.kind (management-web, identity-admin, a person-shaped client),
        and an enabled client stamped sse.kind=webhook with no row
  When one sweep pass runs past the grace window
  Then every one of them stays enabled

Scenario: conflict — an already-disabled orphan is left alone and not reported again
  Given a stamped device client with no row that is already disabled
  When one sweep pass runs
  Then DisableClientAsync is not called for it, and no Warning is logged

Scenario: conflict — a pass that would disable most of the fleet refuses
  Given 4 enabled stamped clients, 3 of which have no active row and are past the grace window
  When one sweep pass runs
  Then nothing is disabled
  And Identity logs an Error naming the candidate count and the stamped-client count
  # the wrong-database / restored-backup guard (§4)

Scenario: bad request — one client failing does not stop the rest
  Given two orphans, and the service-account read for the first throws
  When one sweep pass runs
  Then the second orphan is disabled
  And the first is reported unreachable, with a Warning naming it

Scenario: bad request — Keycloak down does not stop Identity
  Given Keycloak does not answer
  When the Identity API starts
  Then the host starts and serves requests without waiting on the sweep
  And the failed pass is logged, and the next tick tries again

Scenario: auth — the sweep acts with Identity's own Keycloak admin credential only
  Given the sweep runs in the Identity process
  Then it uses the existing identity-admin service account through KeycloakAdminAuthorizationHandler
  And it exposes no endpoint, so there is no caller to authorise
```

## 3. Definitions

- **Stamped client.** A Keycloak client whose `sse.kind` attribute is `device` or `kiosk`
  (the values `RegisterDeviceCommandHandler:65` and `EnrollKioskCommandHandler:43` write).
  `webhook` is excluded (§5).
- **Active row.** A `registered_clients` row with `DisabledAt == null`, the same filter as
  `GetByClientIdAsync`. **Any kind**: a stamped client whose id matches an active row of a
  different kind is not an orphan. That combination cannot arise through the handlers, and
  the conservative answer is to leave it.
- **Orphan.** A stamped client that is **enabled**, has **no active row** with the same client
  id, and whose service-account user's `createdTimestamp` is **older than the grace window**.
- **Grace window: 10 minutes.** The register path makes at most seven Keycloak calls inside
  `CreateClientAsync`, each bounded at 30 s by the standard resilience handler, then one
  `SaveAsync`. A registration still running after 10 minutes is dead. The figure matches
  `IdempotencyReclamation.StaleAfter` (10 min) for the same reason: an attempt alive past it
  is not alive. It is a separate constant, because the two windows bound different things.
- **Action: disable, not delete.** See §4.

## 4. Decisions inside the user's decision

The user chose *a sweep*. Three further choices belong to this spec. Each is stated so the
gate reviewer can overturn it without reopening the mechanism.

**4.1 Disable, not delete.** *[For the gate reviewer: the brief expected delete.]*

| | Disable | Delete |
|---|---|---|
| Orphan's credential stops working | yes | yes |
| Re-registration in the same fab unblocked | yes, through #2728's replace path, already shipped and tested | yes |
| Re-registration in a **different** fab | still refused (`IsReplaceableDisabledClient` requires the same `sse.fab`) | yes |
| Reversible if the sweep was wrong (a restored Postgres backup makes recent, legitimate registrations look orphaned) | yes: an admin re-enables the client and the device's secret works again | no: the device must be re-registered and re-provisioned on site |
| New Keycloak write surface | none: `DisableClientAsync` exists, is idempotent, and is a no-op on a missing client | a public delete, which spec 092 §"Do not" records as carrying an unchecked-response defect in its only existing form |
| Residue | disabled clients accumulate, as revoked devices already do | none |

Disable is chosen. The cross-fab case is a device moving fabs *after* an aborted register, and
it gets the same answer as today: a Keycloak admin deletes the client. That is recorded as
accepted residue (§5).

**4.2 The in-flight race: a grace window measured by Keycloak's own clock.** Keycloak client
representations carry no creation time. The client's **service-account user** does
(`createdTimestamp`, epoch milliseconds), and every device and kiosk client has one, since
both handlers set `ServiceAccountsEnabled: true`. #2728's replace path deletes and recreates,
so the timestamp is fresh for a replaced client as well. The handlers are **not changed**: no
new attribute is stamped. Reading the age costs one `GET` per *candidate*, and candidates are
normally zero. A candidate with no service-account user is skipped and reported, not
disabled.

The pass also reads Keycloak **before** Postgres. A client created after the Keycloak read is
not in the pass. A row committed before the Postgres read protects its client. The only
window left is a client created before the Keycloak read whose row commits after the Postgres
read. That is exactly an in-flight registration, and the grace window covers it. Clock skew
between Keycloak and Identity is seconds inside one cluster, against a 10-minute window.

**4.3 A mass-disable guard.** If the candidates are **more than half** of the enabled stamped
clients **and at least two**, the pass disables nothing and logs an Error. Orphans come from
rare cancellations: a pass that finds most of the fleet orphaned is almost certainly reading
the wrong database, an empty one or a restored one. Disabling 250 kiosks at once is a
fab-wide outage, even if it is reversible. The floor of two keeps a one-device deployment
able to heal its single orphan. *[For the gate reviewer: this guard is not in the
precedent.]*

**4.4 Trigger: at startup, then hourly.** `KioskPrivilegeSweep` runs once at startup, because
it repairs a residue that a boot is a natural point to fix. An orphan is different. It
**blocks an operator now**, and a 24/7 Identity API may not restart for weeks. So the pass
runs at startup and then on a `PeriodicTimer`, which is `IdempotencyReservationSweepHostedService`'s
shape: also in this context, also a reconciliation of state that a dead attempt left behind.
An operator who hits `DEVICE_ALREADY_REGISTERED` for an orphan waits at most
grace + tick, 70 minutes. The interval is a constant, not configuration (ADR-0036: no knob
without a need).

**4.5 One sweep, both kinds.** `KioskPrivilegeSweep` is kiosk-only because its *repair* is
kiosk-specific: spec 052's `offline_access` concern. The orphan question is kind-agnostic.
Both handlers have the same shape, write the same table, and stamp the same attribute key,
and one `GET /clients` answers for both. Two sweeps would list every client twice per tick
and would double the wiring, the logs and the tests for one rule. The kind is a filter set,
`{device, kiosk}`, in one place.

## 5. Scope

### In this PR

- Identity Application: `OrphanedClientSweep` (the pass) and `OrphanedClientSweepOutcome`.
  Two reads on `IKeycloakAdminClient`. One read on `IRegisteredClientRepository`. Their
  `[LoggerMessage]`s.
- Identity Infrastructure: the two reads in `HttpKeycloakAdminClient`, the repository read,
  and `OrphanedClientSweepHostedService` (`BackgroundService`, startup + hourly), registered
  in `IdentityInfrastructureModule`.
- Doc comments that now say the wrong thing: `TryDeleteClientAsync` ("Nothing else will") and
  `KioskPrivilegeSweep` (where it names what it does not cover).
- Tests per plan §8, and the new integration class added to `ci-shards/shard-1.filter`.

### Not in this PR, and why

| Item | Where | Why not here |
|---|---|---|
| **Webhook clients** (`RotateWebhookClientCommandHandler`'s register branch has the same create-then-save hole) | Follow-up issue, **filed at the gate** (tasks T015) | The issue names device and kiosk. Webhook rows have a different lifecycle: spec 264's asynchronous revoke-then-disable and spec 318's EventIngestion lookup. "No active row" may legitimately describe an integration mid-revocation. It needs its own reading, not a third value in the filter set. |
| Changing the handlers' order, or a compensating delete | Nowhere | Declined by the user (issue comments). |
| Deleting disabled orphans; the cross-fab re-registration case (§4.1) | Accepted residue | A disabled client holds no working credential. Moving a device to another fab after an aborted register is rare, and a Keycloak admin can resolve it by hand, as today. |
| An audit-trail record of each disable | Nowhere now | Identity's other Keycloak writes (`DisableClientAsync` on revoke) are logged, not audited. The per-client Warning is the record. |
| Consolidating `GetEnrolledKioskClientIdsAsync` with the new client listing | Follow-up refactor, if wanted | Behaviour-preserving work does not belong in a behaviour-changing PR (ADR-0036). |
| UI | None | Nothing is exposed. |

## 6. Why no new ADR

The user chose the mechanism, and the mechanism has two in-context precedents: spec 092's
`KioskPrivilegeSweep` (ADR-0134 Decision 1) and #2290's `IdempotencyReservationSweepHostedService`.
This spec adds no new consistency model, no new store, no new message, no new credential and
no new cross-context call. It is the second reconciliation of Keycloak against Identity's own
rows, using an admin client Identity already holds. The choices in §4 are parameters of the
pattern, recorded here for the gate. The lane may not write ADRs (ADR-0144). None is needed.

## 7. Latency budget (§IV)

**N/A.** No leg of event arrival → overlay rendered is touched. The sweep is a background
Identity task. Disabling an orphan affects only a credential nobody holds. §VII's dashboard
rule: N/A.

## 8. Phase-4a colour: **red**

This is new behaviour: a new background pass that disables clients. The load-bearing red is
the **integration fact against the real stack** (plan §8.1, I1). A planted orphan device
blocks `POST /devices/register` with 409. After one pass the same POST answers 201. On
`develop` the pass type does not exist, so I1's red is a compile failure. The test-writer
therefore also quotes **I0**, the HTTP-only control, which proves the defect is live on
`develop` (409 for the orphan's id). Unit reds are compile reds and count, but on their own
they would be a weak red.

**Existing tests.** `FakeKeycloakAdminClient` and every `IRegisteredClientRepository` fake gain
the new members. **No existing assertion changes.** If one has to, stop and escalate.

## 9. Independent end-to-end test procedure

1. `aspire run` (one stack per machine). Get an operator token for `admin` (`/fabs/munich`).
2. **Plant an orphan.** Using the Keycloak admin API with `identity-admin`, create the client
   `plc-e2e-2181` with `serviceAccountsEnabled:true`, `enabled:true` and attributes
   `sse.kind=device`, `sse.deviceType=plc`, `sse.deviceIdentifier=e2e-2181`, `sse.fab=munich`.
   Write no row.
3. **Defect present.** `POST {identity}/devices/register {"deviceType":"plc","deviceIdentifier":"e2e-2181","fabId":"munich"}`
   returns **409 `DEVICE_ALREADY_REGISTERED`**, and `GET /devices` does not list it.
4. **Grace holds.** Restart the `identity` resource from the dashboard. The startup pass runs.
   The client is **still enabled**, because it is younger than 10 minutes. No Warning is logged
   for it.
5. **Wait out the grace window** (≥ 10 min after step 2), then restart `identity` again.
   Expect a Warning naming `plc-e2e-2181`, kind `device`, and an age > 10 min. Expect an
   Information summary `disabled 1 of N`. Keycloak shows `enabled:false`.
6. **Healed.** Repeat step 3. Expect **201** with a secret. A `client_credentials` grant for
   `plc-e2e-2181` with that secret succeeds. `GET /devices` lists it.
7. **Steady state.** Restart `identity` once more. No sweep line is logged (silent when nothing
   was repaired), and the device from step 6 is still enabled.
8. *Traces*: the startup pass shows `GET /admin/realms/.../clients`, the Postgres read,
   `GET .../service-account-user` for the candidate only, and the disable `PUT`.

## 10. Success criteria

- **SC1:** a planted orphan device past the grace window is disabled by one pass, and
  `POST /devices/register` for its identifier answers 201 afterwards (integration test, red
  on `develop`). The same holds for a kiosk.
- **SC2:** a stamped client younger than the grace window, a client with an active row, an
  unstamped client, a `webhook`-stamped client, and an already-disabled client are never
  disabled (unit for every case; integration for the young orphan, the registered device and
  an unstamped client).
- **SC3:** the mass-disable guard refuses a pass whose candidates are more than half the
  enabled stamped clients (and at least two), and logs an Error (unit).
- **SC4:** a Keycloak that does not answer neither delays `StartAsync` beyond a bound nor
  stops the host. A per-client failure does not stop the pass (unit; `BackgroundService`
  startup test mirroring `KioskPrivilegeSweepBoundTests`).
- **SC5:** the hosted service is registered by `AddIdentityInfrastructure` (architecture test,
  declaration only, labelled as such like `KioskPrivilegeSweepRegistrationTests`).
- **SC6:** Release build (`TreatWarningsAsErrors`), `dotnet format --verify-no-changes`,
  `Architecture.Tests`, and the Identity coverage gates (Application ≥ 80%) hold.
