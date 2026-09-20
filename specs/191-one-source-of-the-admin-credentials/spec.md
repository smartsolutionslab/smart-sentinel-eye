# Spec 191 — One source of the admin credentials

**Issue:** #2275 — *The Keycloak admin credentials are written literally in four test files*
**Branch:** `2275-one-source-of-admin-credentials`
**Phase-4a colour:** **characterisation / green** (ADR-0144). Confirmed at phase 1 — see
"Phase-4a colour" below; this is not a default.
**ADRs:** ADR-0037 (phased workflow), ADR-0144 (lane, 4a colours), ADR-0103 (integration
tests boot the Aspire fixture; no Testcontainers), ADR-0052 (xUnit + Shouldly),
ADR-0053 (sentence-style test names), ADR-0084 (code metrics), ADR-0100 (the
`identity-admin` service account and the Mosquitto plugin), ADR-0109 (`[P]` markers),
ADR-0139 (a rule that fails the build, not the review).

---

## The premise, re-checked against the current tree

**The issue was filed before #2274 (spec 189) landed, and one row of its table is now
stale.** Read on `origin/develop` @ `f256aaf7`:

| file | issue says | **actually, today** |
|---|---|---|
| `RealmProbe.cs` | the fold's new home | **correct** — `public const Realm / AdminClientId / AdminClientSecret` at lines 44–46 |
| `KioskInheritedPrivilegeIntegrationTests.cs` | de-duplicated by #2182 | **already fully done** — holds **no** literal; reads `RealmProbe.Realm` / `.AdminClientId` / `.AdminClientSecret` at lines 147–149 |
| `KeycloakAdminTokenProviderTests.cs:19–21` | not touched | **correct, still duplicated** |
| `MqttAudienceIntegrationTests.cs:61,66–67` | not touched | **correct, still duplicated** |

So the work is **two files, not four**, and `RealmProbe` needs no new member — it already
exposes all three constants publicly. The measured duplication is exactly six lines:

```
KeycloakAdminTokenProviderTests.cs:19:    private const string AdminClientId = "identity-admin";
KeycloakAdminTokenProviderTests.cs:20:    private const string AdminClientSecret = "dev-only-identity-admin-secret";
KeycloakAdminTokenProviderTests.cs:21:    private const string Realm = "smart-sentinel-eye";
MqttAudienceIntegrationTests.cs:61:       private const string Realm = "smart-sentinel-eye";
MqttAudienceIntegrationTests.cs:66:       private const string AdminClientId = "identity-admin";
MqttAudienceIntegrationTests.cs:67:       private const string AdminClientSecret = "dev-only-identity-admin-secret";
```

A count of the quoted literals returns **3 for each file**, and every hit is one of those
`const` declarations. Nothing else in either file spells a literal.

**This is precisely spec 137's US-3**, which that spec recorded as *not delivered*:

> US-3 (P3) — *Not delivered.* Point `KeycloakAdminTokenProviderTests` and
> `MqttAudienceIntegrationTests` at `RealmProbe`'s constants. See fact (b).

Spec 189 then delivered 137's US-2 (the checked delete). This spec closes US-3. Nothing
else of #2275's stated scope remains after it.

---

## Scope decision: should the tests read the secret **from the realm import**?

The issue asks this explicitly and asks for a call rather than a deferral.

**What the realm import actually says** (`src/AppHost/Realms/smart-sentinel-eye-realm.json`):

```
  2:  "realm": "smart-sentinel-eye",
255:      "clientId": "identity-admin",
263:      "secret": "dev-only-identity-admin-secret",
```

All three of `RealmProbe`'s constants match it exactly, today. So the file **is** the
defining source, and the issue's instinct about where truth lives is right.

**Decision: reading the value from the realm import is OUT OF SCOPE for #2275.** Four
reasons, in descending weight.

1. **It is new behaviour, and this issue is behaviour-preserving.** A realm-file reader is
   a new mechanism with new failure modes — file not found, client absent, JSON shape
   changed — none of which exist today. ADR-0144 forbids a behaviour-preserving change
   from carrying behaviour-changing work, and the repo's rule is that a refactor which is
   also something else is two issues. Folding six constants and introducing a file reader
   cannot share one colour, so they cannot share one change.

2. **Nothing in the test project can see the file today.**
   `tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj` has **no**
   `Content`, `None` or `Link` item — verified, the grep returns nothing. The realm JSON
   lives under `src/AppHost/Realms/`, outside the test project, and is not copied to the
   test output. Reading it needs either a repo-root walk (which differs between a local run
   and CI's checkout layout) or a new csproj content item. That is project plumbing, not a
   constant swap.

3. **It would relocate the drift, not remove it.** A realm edit does not reach a running
   Keycloak until the volume is deleted — the documented drill, and the reason this issue
   exists. So the file can be *ahead* of the realm the fixture actually authenticates
   against. A test that read the file would then present a secret the live realm does not
   hold and fail with **the same 401** this issue exists to prevent, arriving from the
   other direction. Reading the file removes drift between copies and adds drift between
   the file and the volume.

4. **The house pattern is a constant on a test-infra type, and there is precedent in this
   very namespace.** `tests/Integration.Tests/Fixtures/AspireFixture.Auth.cs:10–11` holds
   `AdminUsername = "admin"` and `AdminPassword = "Admin1234"` as public consts — also
   realm-import values, also not read from the file. `RealmProbe`'s three consts are the
   same pattern. Reusing it costs nothing new (constitution: prefer existing patterns).

**The better answer to the underlying worry is a guard, not a reader** — see US-2. A test
that *asserts* `RealmProbe`'s three constants equal the realm import's values turns silent
drift into a red build without putting the file on the runtime path, and needs no
availability or ordering guarantee. That is genuinely new behaviour (a test that can fail),
so it is a red-coloured change and gets its own issue: **filed as
[#2466](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2466)**, on Project
#13, covering `RealmProbe.cs` and the three further copies below. This spec records it
rather than smuggling it in.

**Out of scope but worth recording:** the credential has **three further copies** outside
`tests/Integration.Tests/Identity/` — `src/AppHost/AppHost.cs:35`
(`AddOverridableParameter("IdentityAdminClientSecret", "dev-only-identity-admin-secret", secret: true)`),
`tests/Integration.Tests/AppHostParameterOverrideTests.cs:50`, and
`src/Identity/Infrastructure/KeycloakAdmin/KeycloakAdminOptions.cs:13,26`
(`Realm`/`AdminClientId` property defaults — found during phase 6 review, not the original
audit). The AppHost pair is a *different* axis from the realm copy: the AppHost copy is what
**Identity presents**, the realm copy is what **Keycloak holds**, and an override moves the
first without the second. `KeycloakAdminOptions`'s defaults are a third axis again — they are
the production object `KeycloakAdminTokenProviderTests` itself populates explicitly, so the
test cannot notice if these particular defaults drift; only a guard reading the realm import
directly would. None of the three is reachable from a test constant and none is inside
#2275's stated scope. US-2's guard should cover all three; this spec does not.

---

## User stories

### US-1 (P1) — The two remaining files read the credentials from `RealmProbe`.

**Delivered by this spec. Behaviour-preserving.**

Six `const` declarations are deleted and their references become `RealmProbe.Realm`,
`RealmProbe.AdminClientId`, `RealmProbe.AdminClientSecret`. Both files already sit in
namespace `SmartSentinelEye.Integration.Tests.Identity`, where `RealmProbe` is a
`public sealed class`, so there is no `using` change and no new member.

Independently shippable and observable: the four covering tests run end to end in CI's
integration job and their outcomes are readable in the uploaded trx.

### US-2 (P2) — *Not delivered.* A guard that fails the build when a constant drifts from the realm import.

**Behaviour-changing → red colour. Filed as
[#2466](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2466).** A test
asserting that `RealmProbe.Realm` / `.AdminClientId` / `.AdminClientSecret` equal the realm
import's `realm`, the `identity-admin` client's `clientId` and its `secret` — and also that
`AppHost.cs`'s `IdentityAdminClientSecret` default and `KeycloakAdminOptions`'s `Realm`/
`AdminClientId` property defaults match too (three further copies found at phase 6, all in
#2466's scope). It must be observed red first (mutate one constant, watch it fail) per
ADR-0139, and it needs the csproj plumbing named in reason (2). **This is the real fix for
the failure mode #2275 describes**; US-1 only shrinks the surface that failure can strike.

### US-3 (P3) — *Not delivered, and deliberately refused.* Fold the token-minting.

`MqttAudienceIntegrationTests.MintClientCredentialsTokenAsync` and `CreateAdminApiClient`
are **not** duplicates of `RealmProbe.AuthorisedAdminClientAsync`. The probe mints through
the production `KeycloakAdminTokenProvider`; the Mqtt test mints through a raw
`client_credentials` form POST that it uses for **three** different clients (the admin, the
throwaway, the registered device), only one of which is the admin. Folding the admin call
alone would change that one token's code path — a behaviour change — and would leave the
generic minter in place regardless. `RealmProbe`'s own doc comment already records this
exact reasoning for the Kiosk test's `CreateAdminClient` ("a different shape from
`AuthorisedAdminClientAsync` below, not a duplicate of it"); this is the same case.
**US-1 is constants only.**

---

## Acceptance scenarios (US-1)

```gherkin
Scenario: Happy path — the four covering tests still pass
  Given KeycloakAdminTokenProviderTests' two tests and MqttAudienceIntegrationTests'
        two tests reported outcome="Passed" on green develop run 35506884306
  When the six constants are replaced by references to RealmProbe
  Then all four report outcome="Passed" in this PR's integration.trx
  And the assertion-invariance hash over the two files is unchanged
  And the test-name inventory hash over the two files is unchanged

Scenario: Conflict — RealmProbe's values must not move
  Given RealmProbe already exposes the three constants and other test classes read them
  When this change lands
  Then RealmProbe's comment-stripped code hash is unchanged
  And no member of RealmProbe changes name, signature, visibility or value
  And any edit to RealmProbe.cs is comment-only

Scenario: Bad request — an asserted value moves
  Given the baselines captured at phase 1 below
  When any of the four guards is re-run after the change
  Then it matches its recorded baseline; if it does not, the change is blocked,
       not adjusted

Scenario: The credentials really are single-copy afterwards
  Given the fold has landed
  When the quoted-literal count is run over the two target files
  Then it is 0 for both

Scenario: Auth — the folded constants still mint a working admin token
  Given MqttAudienceIntegrationTests' DisposeAsync and CreateAudiencelessClientAsync
        both call the Keycloak Admin API with these credentials
  Then neither fails with 401; a 401 here is the exact failure this issue exists to
       prevent, arriving from the other direction
```

**Auth note.** No new authorisation surface. Test-only code; the credential is a dev-only
realm-import secret already committed in six places before this change, four after; no
production assembly is touched; the change strictly reduces the number of copies. No `sse.*` scope, fab check or
`Idempotency-Key` is involved. **A dedicated `/security-review` is not warranted** — the
change moves no secret across a trust boundary and introduces no new one. Recorded
explicitly because the word "credentials" in the title invites the opposite reflex.

---

## Phase-4a colour — confirmed, not defaulted

**Characterisation / green.** The issue asserts it; phase 1 checked it against the tree and
agrees, on three grounds:

- **No production assembly is touched.** Only two files under
  `tests/Integration.Tests/Identity/`.
- **The covering tests already exist and are green.** A refactor with no covering test is a
  rewrite; here there are four, all `Passed` on the exact branch point. There is therefore
  nothing to observe red — a red here would be a regression, not a gate.
- **The values are provably unchanged by a local, Docker-free check.** `RealmProbe`'s
  comment-stripped code hash is that proof, and it was shown counterfactually sound in both
  directions (below). A swap that preserved the constant *names* but moved a *value* would
  move that hash.

Ambiguity resolves to red (ADR-0144). There is none here.

---

## Independent end-to-end test procedure

**Do not boot the Aspire fixture on the session machine.** Same constraint spec 137
recorded: the fixture boots six containers into the Docker vhdx on C:, the known failure
mode is the engine ceasing to answer until a GUI restart, and the 8-minute startup runs
against a 10-minute tool ceiling. **The route is CI's own trx**, per ADR-0103.

### Baseline, captured at phase 1 (measured, not assumed)

Run **`35506884306`**, branch `develop`, SHA **`f256aaf7`** — which is this branch's exact
cut point — 2026-09-20T11:05:53Z, conclusion `success`,
`<Counters total="577" executed="577" passed="577" failed="0" .../>`:

| Test | outcome |
|---|---|
| `KeycloakAdminTokenProviderTests.Client_credentials_grant_against_identity_admin_returns_a_usable_token` | `Passed` |
| `KeycloakAdminTokenProviderTests.Second_call_within_token_lifetime_returns_the_cached_token` | `Passed` |
| `MqttAudienceIntegrationTests.A_token_minted_without_the_api_audience_is_refused_by_the_broker` | `Passed` |
| `MqttAudienceIntegrationTests.A_token_minted_with_the_api_audience_still_connects` | `Passed` |

Re-runnable verbatim (artifact retention is **14 days** — re-capture against a newer green
`develop` run if this spec is picked up after **2026-10-04**):

```sh
rm -rf /tmp/trx191 && mkdir -p /tmp/trx191
gh run download 35506884306 -n integration-test-results -D /tmp/trx191
T=/tmp/trx191/tests/Integration.Tests/TestResults/integration.trx
grep -oE '<UnitTestResult[^>]*testName="[^"]*(KeycloakAdminTokenProvider|MqttAudience)[^"]*"[^>]*outcome="[^"]*"' "$T" \
| sed -E 's/.*testName="([^"]*)".*outcome="([^"]*)".*/\2\t\1/'
grep -o '<Counters[^>]*/>' "$T"
```

The **"after"** is the same commands against this PR's own `integration tests (Docker)`
job. Four `Passed`, and `failed="0"`.

**What this route does not prove.** It proves the four tests passed on a runner, not on a
developer machine, and not that the realm secret is right anywhere but CI. That is
acceptable and is the evidence every integration change in this repo stands on (ADR-0103).

### Docker-free guards, with baselines and counterfactuals

All four run locally in under a second. Every one was **proved by counterfactual** at phase
1 — constructed the thing it claims to catch and watched it catch it — because a guard that
cannot be shown to fail is not evidence.

| # | Guard | Baseline |
|---|---|---|
| G1 | `RealmProbe.cs` comment-stripped code hash | `1dd598765d8b7aac0d0a202b82dd82e50a2bf031e36b4e2630b2f7afcf6ee7be` |
| G2 | assertion-invariance hash, the two target files | `0ebec6e86ed5a9c0f926831995bbda43bc1833b01aa169801457ec862453ecd2` |
| G3 | test-name inventory hash, the two target files | `5f8c4d56a1f82e8279bd8b92dbb956aca18c098ac5b2bdcc033fe734c3e972c2` (4 names) |
| G4 | quoted-literal count, the two target files | `3` and `3` **before**; **`0` and `0` after** |

**Counterfactual results, run at phase 1:**

- **G1 catches a moved value.** Mutating `dev-only-identity-admin-secret` to
  `dev-only-CHANGED-secret` in `RealmProbe.cs` moves the hash to
  `2bdc50c50f19b8bf4e94aff79c47ad5ee43f0b78cef7cbafcb1977f207ebd9ef`.
- **G1 ignores a comment rewrite.** Rewriting a `///` doc line leaves it at `1dd59876...`.
  So a doc-comment update to `RealmProbe.cs` is permitted *and provable*.
- **G3 catches a rename.** Renaming one `[Fact]` moves the hash to
  `cee9d2562efc55a2a4c479a59aec3dbd01d62df10b0ddf47355fe952ae179ab1`.
- **G2's extractor is known to detect assertion changes** — hashed across `RealmProbe.cs`'s
  own history it reads `57a1874f...` before the checked-delete assertion existed and
  `974fe47e...` after.

**What G2 deliberately does not catch.** `assertions.sh` normalises `RealmProbe.` away
(`s/RealmProbe\.//g`). That is exactly the invariance US-1 needs, and it means **G2 is
blind to the swap by design**. G2 is not a value check.

**What none of G1-G4 catch, corrected from an earlier overclaim in this file: a
transposition.** G1 checks `RealmProbe.cs`'s *own* constant values, not which constant
landed at which of the 13 call sites in the two target files. Verified by phase-6 review:
setting `Realm = RealmProbe.AdminClientId` in `CreateProvider()` leaves all four guards at
their exact pass baselines — G2 never looks at the initialiser (it's the wrong-token-for-the-
wrong-field kind of bug it normalises away), G3 and G4 never look at it either, and G1 never
opens the target files at all. **No Docker-free mechanism in this spec catches a
transposed constant.** The only thing that would is a real Keycloak 401 — which means this
spec's phase-5 trx read (below) is not confirmatory, it is the mandatory gate. Do not treat
a green Docker-free run as sufficient to merge; wait for the real integration job.

`code.sh` is committed alongside this spec; `assertions.sh` is reused verbatim from
`specs/137-one-copy-of-the-admin-helpers/`.

---

## Locked tech choices

xUnit + Shouldly (ADR-0052), sentence-style test names (ADR-0053), the Aspire fixture and
no Testcontainers (ADR-0103), `CancellationToken` as mandatory last parameter (ADR-0049),
≤ 300 LOC per file advisory (ADR-0084). **Nothing new is introduced.** This spec deletes
six lines and adds no member, no type, no package and no project reference.

`MqttAudienceIntegrationTests.cs` is **360 LOC**, already over ADR-0084's advisory 300.
This change takes it to **354** (measured, not estimated). It reduces the overage slightly
but does not fix it; splitting the file is a separate concern and is explicitly not
attempted here.

## Latency budget impact

**N/A.** Test-only change; no leg of the event-to-overlay path is touched
(constitution §IV).

## File contention

Swept at phase 1 across every remote branch and every open PR.

- **One PR is open: #2464** (issue #2257, spec 190) — `tests/Architecture.Tests/*` and
  `specs/190-*`. **Disjoint.**
- **`origin/2274-client-delete-assertion` does touch `RealmProbe.cs`** and is *not* an
  ancestor of `develop` — but its PR **#2463 is MERGED** (2026-09-20T11:05:50Z) and
  `git diff origin/develop origin/2274-client-delete-assertion -- tests/Integration.Tests/Identity/`
  is **empty**. It is a stale post-rebase-merge branch: rebase-merge renamed the SHAs
  (ADR-0087), so ancestry says no while content says identical. **Not contention.**
- No other remote branch touches any of the three files.

## Spec number

Highest **merged** spec on `origin/develop` is **189**. **190** is claimed by the open PR
#2464 (`origin/2257-shared-route-scan-reader`). Every remote branch was swept for
`specs/19x-` and `specs/2xx-` directories; that was the only claim. **191 is free.**

Per the standing caution, re-check 191 is still unclaimed if a parked PR merges before this
one is opened.

## Board

#2275 is on Project #13, status **Todo**, labels `agent:ready` + `tech-debt` — verified
with `gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered on
`content.number`. **The phase-3 gate is already satisfied; nothing to add.**
