# Spec 137 — One copy of the admin helpers, and the one that cannot be folded

**Issue:** #2182 — *`RealmProbe` and `KioskInheritedPrivilegeIntegrationTests` carry the
same admin credentials, role-expansion and delete helpers twice*
**Branch:** `refactor/2182-one-copy-of-the-admin-helpers`
**ADRs:** 0037 (phases and gates), 0144 (the autonomous lane; the two colours of phase
4a), 0036 (smallest change), 0103 (integration tests run against the Aspire fixture, no
Testcontainers), 0052 (xUnit + Shouldly), 0053 (sentence-style test naming), 0049
(`CancellationToken` mandatory last parameter), 0084 (≤ 300 LOC per file), 0109
(`[P]` disjoint-file parallelism), 0028 (GitFlow), 0086 (no `Co-Authored-By`)

**Spec number 137.** Derived from the highest number on any ref, not from the working
tree: `git log --all --name-only --pretty=format: -- specs/ | grep -oE 'specs/[0-9]{3}'`
tops out at **136**, `git ls-tree --name-only origin/develop specs/` confirms
`136-the-figure-is-from-ci` as the tip (it merged 2026-09-12), and no remote branch is
named `^[0-9]{3}-`. 136 + 1 = **137**.

---

## Phase 1 verified every claim in the issue. Seven held; the eighth did not, and two
## facts the issue does not state change the shape of the work

### The eight rows, checked line by line against `origin/develop`

Every line number in the issue's table is **correct**. Equivalence is a different
question and was checked separately.

| # | Shape | `RealmProbe` | `KioskInherited…` | Verdict |
|---|---|---|---|---|
| 1 | `Realm = "smart-sentinel-eye"` | `:30` public | `:34` private | **Identical** |
| 2 | `AdminClientId = "identity-admin"` | `:31` public | `:35` private | **Identical** |
| 3 | `AdminClientSecret = "dev-only-identity-admin-secret"` | `:32` public | `:36` private | **Identical** |
| 4 | `LongLivedCredentialPrivilege = "offline_access"` | `:35` | `:39` | **Identical**, doc comment included |
| 5 | `AuthorisedAdminClientAsync` | `:130` | `:183` | **Diverged, non-observable** — see below |
| 6 | `EffectiveRealmRolesAsync` | `:71` | `:208` | **Behaviourally identical** — see below |
| 7 | `ReadJsonAsync` | `:156` | `:243` | **Identical** but for cancellation plumbing |
| 8 | Client delete | `DeleteAsync :112` | `DeleteClientAsync :253` | **DIVERGED, ASSERTION-BEARING — cannot be folded here** |

### Row 6 — the one the brief asked about specifically: it has *not* drifted

The two `EffectiveRealmRolesAsync` implementations issue the **same three Admin API
reads, in the same order, against the same URLs, and project the result identically**:

1. `admin/realms/{Realm}/clients?clientId={Uri.EscapeDataString(clientId)}` → `[0].id`
2. `admin/realms/{Realm}/clients/{uuid}/service-account-user` → `.id`
3. `admin/realms/{Realm}/users/{id}/role-mappings/realm/composite`
4. `.Select(role => role.GetProperty("name").GetString() ?? string.Empty).ToArray()`

The only structural difference is that `KioskInherited…` factors step 3+4 into a private
`CompositeRealmRolesAsync(admin, userId)` (`:233`) so it can be shared with
`EffectiveRealmRolesOfUserAsync`; `RealmProbe` inlines it. **Same behaviour.** The
role-expansion read the issue is most worried about has *not* quietly diverged.

### Row 5 — diverged, but not observably

`RealmProbe.AuthorisedAdminClientAsync` (`:132`, with the comment at `:141–144`) hands
**one** `HttpClient` to both `FakeHttpClientFactory` and the caller.
`KioskInherited…AuthorisedAdminClientAsync` (`:185`, `:194`) calls
`aspire.CreateKeycloakClient()` **twice** and never disposes the second — the leak
`RealmProbe`'s comment says it fixed. Same HTTP outcome, different resource lifetime.
Folding removes the leak. **Nothing a test asserts changes**, which is why this is in
scope.

`RealmProbe`'s version also takes a `CancellationToken` (ADR-0049); the Kiosk version
hardcodes `CancellationToken.None`, which is what all four call sites pass anyway.

### Row 8 — **the loud finding. The two delete helpers are not the same helper.**

```
RealmProbe.DeleteAsync :123        deleted.IsSuccessStatusCode.ShouldBeTrue("…");
KioskInherited….DeleteClientAsync  await admin.DeleteAsync(…);   // response discarded
```

`RealmProbe.DeleteAsync` **asserts the DELETE succeeded**, with a 9-line doc comment
(`:93–111`) arguing that the unchecked form it replaced is the same defect shape as
#2166, and accepting explicitly that a throw from a `finally` block masks the body's
failure. `KioskInherited….DeleteClientAsync` discards the response. Both call sites are
`finally` blocks.

**Folding row 8 would add an assertion to four currently-green tests.** If that
assertion fires — and nobody can know today whether those DELETEs always succeed,
because nothing has ever looked — a characterisation test that must pass *unmodified*
goes red, and the cheapest way out for an engineer under pressure is to delete the
check. That is a gate weakening (ADR-0144), reached by a route the lane invites.

**So row 8 is out of scope for this spec** and stays duplicated, reduced to its
irreducible core (the loop). A refactor that is also a strengthening is two issues.
**US-2 below is the second issue, and it is not delivered here.** This spec therefore
does **not** fully close #2182's table; the delivery must say so.

### Two facts the issue does not state

**(a) `RealmProbe` has a second consumer.**
`tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs` uses it
at `:73`, `:77`, `:80`, `:82`, `:89`, `:91`, `:99`, `:119`, `:124`, `:125`, `:128`,
`:134`, `:136`, `:141`, `:151`, `:152`, `:177`, `:178`, `:179`. Concretely it depends on
`RealmProbe.Realm`, `.AdminClientId`, `.AdminClientSecret`, `.LongLivedCredentialPrivilege`,
`PlantAsync(clientId, attributes, ct)`, `EffectiveRealmRolesAsync(clientId, ct)` and
`DeleteAsync(clientId, ct)`. **Every one of these is additive-safe**: this spec widens
`RealmProbe`'s surface and never narrows or renames it. The constraint is a hard one —
**no existing `RealmProbe` member may change name, signature, visibility or behaviour.**

The covering suite is therefore **six tests, not four**.

**(b) The credentials are written in *four* files, not two.** The issue says "twice".
`tests/Integration.Tests/Identity/KeycloakAdminTokenProviderTests.cs:19–20` and
`tests/Integration.Tests/Identity/MqttAudienceIntegrationTests.cs:66–67` carry the same
`identity-admin` / `dev-only-identity-admin-secret` pair. They duplicate only the
*constants*, not the helpers, and they are not in the covering suite. **Out of scope**,
recorded here so the next reader does not believe "one copy" once this merges. US-3.

---

## User stories

### US-1 (P1) — Seven shapes, one copy. *This is the whole delivery.*

**As** whoever next edits the Keycloak realm import,
**I want** the admin realm name, client id, client secret, the `offline_access`
constant, the admin-token client, the composite-role read and the JSON read to exist
**once** in `tests/Integration.Tests/Identity/`,
**so that** a realm-import change cannot update one copy and leave the other, surfacing
as a 401 from the Admin API inside a test that is about privileges.

Independently shippable and observable: the six covering tests run end to end in CI's
integration job and their outcomes are readable in the uploaded trx.

### US-2 (P3) — *Not delivered.* Adopt the checked delete at the Kiosk call sites.
Behaviour-**changing** (silent cleanup → asserted cleanup). Needs its own issue, its own
red/green reasoning and its own risk budget. See row 8.

### US-3 (P3) — *Not delivered.* Point `KeycloakAdminTokenProviderTests` and
`MqttAudienceIntegrationTests` at `RealmProbe`'s constants. See fact (b).

---

## Acceptance scenarios (US-1)

Gherkin, against the compiled suite and the CI integration job.

```gherkin
Scenario: Happy path — the covering suite is unchanged in what it asserts
  Given the six tests in KioskInheritedPrivilegeIntegrationTests and
        KioskPrivilegeSweepStartupIntegrationTests passed on a green develop run
  When the shared helpers are folded into RealmProbe
  Then all six report outcome="Passed" in this PR's integration.trx
  And the assertion-invariance hash (below) is unchanged

Scenario: Conflict — the second consumer must not be broken
  Given KioskPrivilegeSweepStartupIntegrationTests depends on seven RealmProbe members
  When RealmProbe's surface is widened
  Then no existing member changes name, signature, visibility or behaviour
  And that file is not edited at all by US-1

Scenario: Bad request — an asserted value moves
  Given the assertion-invariance baseline captured at phase 3
  When the extractor is re-run after the change
  Then it is byte-identical; if it is not, the change is blocked, not adjusted

Scenario: The credentials really are single-copy afterwards
  Given the fold has landed
  When `grep -c 'dev-only-identity-admin-secret'` is run over
       tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs
  Then the count is 0

Scenario: Auth — the folded helper still mints a working admin token
  Given the four tests that call the Admin API through the folded helper
  Then none of them fails with 401; a 401 here is the exact failure this issue exists
       to prevent, arriving from the other direction
```

**Auth note.** There is no new authorisation surface: this is test-only code, the
credential is a dev-only realm-import secret already committed in three other places,
and no production assembly is touched. No `sse.*` scope, fab check or
`Idempotency-Key` is involved.

---

## Independent end-to-end test procedure

**This cannot be run on the machine holding this session.** C: is at 96% (9.9 GB free);
the Aspire fixture boots six containers into the Docker vhdx on C:, and the known
failure mode is the engine ceasing to answer until a GUI restart — which ends the run,
not merely this issue. The fixture's startup timeout is 8 minutes against a 10-minute
tool ceiling. **Do not boot the fixture locally for this spec.**

**The route is CI's own trx, and phase 1 confirmed it by downloading one.**
`ci.yml:180` passes `--logger "trx;LogFileName=integration.trx"` and `:182–188` uploads
`**/TestResults/*.trx` as artifact `integration-test-results` with `if: always()`.

**Baseline captured at phase 1 — run `34682095657`, `develop`, SHA
`e892b27c713d9d2c161bf14f08e7f985dfcce9a7`, 2026-09-12T07:58:57Z, all four jobs green,
`<Counters total="488" executed="488" passed="488" failed="0" …/>`:**

| Test | outcome |
|---|---|
| `KioskInheritedPrivilegeIntegrationTests.A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege` | `Passed` |
| `KioskInheritedPrivilegeIntegrationTests.An_account_the_provider_creates_holds_it_until_something_removes_it` | `Passed` |
| `KioskInheritedPrivilegeIntegrationTests.An_operator_does_not_hold_the_long_lived_credential_privilege` | `Passed` |
| `KioskInheritedPrivilegeIntegrationTests.A_wall_display_account_does_hold_it` | `Passed` |
| `KioskPrivilegeSweepStartupIntegrationTests.A_kiosk_a_failed_enrolment_left_behind_loses_the_privilege_when_identity_starts` | `Passed` |
| `KioskPrivilegeSweepStartupIntegrationTests.An_account_this_system_did_not_enrol_keeps_every_realm_role_it_held` | `Passed` |

The command that produced it, re-runnable verbatim (artifact retention is **14 days** —
re-capture if this spec is picked up after 2026-09-26):

```sh
rm -rf /tmp/trx && mkdir -p /tmp/trx
gh run download <RUN_ID> -n integration-test-results -D /tmp/trx
grep -oE '<UnitTestResult[^>]*testName="[^"]*(KioskInheritedPrivilege|KioskPrivilegeSweepStartup)[^"]*"[^>]*outcome="[^"]*"' \
  /tmp/trx/tests/Integration.Tests/TestResults/integration.trx \
| sed -E 's/.*testName="([^"]*)".*outcome="([^"]*)".*/\2\t\1/'
grep -o '<Counters[^>]*/>' /tmp/trx/tests/Integration.Tests/TestResults/integration.trx
```

The **"after"** is the same two commands against this PR's own `integration tests
(Docker)` job. Six `Passed`, and `failed="0"`.

**What this route does not prove.** It proves the six tests passed on a runner, not on a
developer machine, and not that the realm secret is right anywhere but CI. That is
acceptable and is the same evidence every other integration change in this repo stands
on (ADR-0103). No better route exists here: the alternative is a local fixture boot that
this machine cannot survive.

---

## Locked tech choices

xUnit + Shouldly (ADR-0052), sentence-style test names (ADR-0053), the Aspire fixture
and no Testcontainers (ADR-0103), `CancellationToken` as mandatory last parameter
(ADR-0049), ≤ 300 LOC per file (ADR-0084). Nothing new is introduced; this spec deletes
code and widens one existing test helper's visibility.

**No ADR is needed.** Every decision this spec makes is already covered: it introduces
no architecture, no dependency, no boundary, no runtime resource. Row 8's deferral is a
scope decision, not an architectural one, and is recorded as US-2 plus a follow-up
issue.

## Latency-budget impact

**N/A.** Test-only. No production assembly, no code on the event-to-overlay path, no leg
of constitution §IV touched.
