# Plan — Spec 191, one source of the admin credentials

**Phase 2 artifact.** Read `spec.md` first; it carries the premise correction, the
realm-import decision and the measured baselines this plan assumes.

---

## Bounded context and layers

**None are touched.** This change lives entirely in `tests/Integration.Tests/Identity/`,
the Identity context's integration-test suite. No `Domain`, `Application`,
`Infrastructure` or `Api` assembly is edited; no entity, value object, invariant, domain
event or integration event is added, removed or changed.

Consequently the usual phase-2 sections have no content and are recorded as such rather
than omitted:

| Section | Applies? |
|---|---|
| Entities / value objects / invariants | **No.** Nothing in `Domain` is touched. |
| Domain → integration event mapping | **No.** No message is added or changed. |
| Persistence / EF / migrations | **No.** |
| Boundary rules (no cross-context project references; `Shared.Contracts` only) | **Unchanged and untested by this change** — both files were already in the same assembly and namespace as `RealmProbe`. NetArchTest's existing rules are unaffected. |
| Latency budget leg | **N/A** (constitution §IV). |
| New authorisation surface | **None.** See `spec.md`'s auth note. |

---

## The shape of the change

### What moves

`RealmProbe` (`tests/Integration.Tests/Identity/RealmProbe.cs`) is already the single
source. It needs **no code change**:

```csharp
public const string Realm = "smart-sentinel-eye";
public const string AdminClientId = "identity-admin";
public const string AdminClientSecret = "dev-only-identity-admin-secret";
```

Both target files sit in `namespace SmartSentinelEye.Integration.Tests.Identity`, and
`RealmProbe` is `public sealed`. So the references compile with no `using` change, and the
constants resolve statically — no instance of `RealmProbe`, and therefore no
`AspireFixture`, is required by either consumer.

### File 1 — `KeycloakAdminTokenProviderTests.cs`

Delete lines 19–21. In `CreateProvider()`, the `KeycloakAdminOptions` initialiser becomes:

```
Realm             -> RealmProbe.Realm
AdminClientId     -> RealmProbe.AdminClientId
AdminClientSecret -> RealmProbe.AdminClientSecret
```

Three declarations out, three references in. Nothing else in the file changes.

**A boundary rule specific to this file, and the reason it must be constants-only.**
`KeycloakAdminTokenProviderTests` is the test **of** `KeycloakAdminTokenProvider`.
`RealmProbe.AuthorisedAdminClientAsync` **constructs a `KeycloakAdminTokenProvider`
internally**. Routing this file's `CreateProvider()` through `RealmProbe` would make the
test both act and observe through the code under test, which is precisely the failure mode
`RealmProbe`'s own doc comment warns about ("a test that both acts and observes through the
code under test cannot tell a working sweep from a lying client"). **Take the constants;
never take the helper.** This is the single most important constraint in this plan and the
reviewer should check it explicitly.

### File 2 — `MqttAudienceIntegrationTests.cs`

Delete lines 61 and 66–67 (and the two-line `//` comment above 66, which explains the
constants being deleted — it goes with them; leaving it orphaned above an unrelated
constant is worse than removing it).

Replace every use:

- `Realm` — used in four interpolated Admin-API / token URLs
  (`admin/realms/{Realm}/clients`, the `?clientId=` lookup, the `DELETE`, and
  `/realms/{Realm}/protocol/openid-connect/token`) → `RealmProbe.Realm`.
- `AdminClientId` / `AdminClientSecret` — used at **two** call sites, both
  `MintClientCredentialsTokenAsync(AdminClientId, AdminClientSecret)`: one in
  `DisposeAsync`, one in `CreateAudiencelessClientAsync` →
  `RealmProbe.AdminClientId` / `RealmProbe.AdminClientSecret`.

**`ApiAudience` and `PublishScope` stay exactly as they are.** They are realm values, but
they are not admin credentials, `RealmProbe` does not host them, and #2275 does not ask
for them. Hoisting them would widen a behaviour-preserving change into files whose tests it
has no claim on — the same reasoning spec 137 used to leave *these* two files alone.

**The token-minting is not folded.** `MintClientCredentialsTokenAsync` and
`CreateAdminApiClient` are a different shape from `RealmProbe.AuthorisedAdminClientAsync`
(raw `client_credentials` form POST vs the production `KeycloakAdminTokenProvider`), and
the generic minter serves three clients of which the admin is one. See `spec.md` US-3.

### File 3 — `RealmProbe.cs` (documentation only, optional but preferred)

The class doc currently enumerates its consumers:

> `KioskInheritedPrivilegeIntegrationTests` and `KioskPrivilegeSweepStartupIntegrationTests`
> both call this class rather than keeping their own copies of the admin credentials, …

After this change that list is **incomplete**, and a record that silently stops matching
reality is the exact defect this repository keeps having to correct. One sentence may be
added naming the two new constant-only consumers and spec 191.

**Hard constraint:** the edit must be **comment-only**, and G1 proves it. G1 was
counterfactually shown to ignore a rewritten doc line and to catch a moved value, so
"comment-only" here is a verified claim, not an assertion about prose. If G1 moves, the
edit was not comment-only — block, do not adjust.

---

## Why this is the smallest shippable slice

One user story, one commit, six deleted lines, no new member. It is independently
observable end to end: the four covering tests exercise the real Keycloak in CI's
integration job and their outcomes are individually readable in the uploaded trx. There is
no smaller slice that still closes the issue, and no part of it can ship separately —
deleting one file's constants and not the other's would leave the issue open.

---

## Risk register

| # | Risk | Mitigation |
|---|---|---|
| R1 | A constant's **value** silently moves during the edit | G1 (`RealmProbe.cs` code hash). Counterfactually proved to catch exactly this. |
| R2 | An **assertion** is edited to make something pass | G2, plus ADR-0144: an assertion that has to be edited is evidence behaviour moved — block, don't adjust. |
| R3 | A test is renamed, dropped or moved between the two files | G3 (name inventory, 4 names). |
| R4 | The fold is claimed but a literal survives | G4 (quoted-literal count must be 0 and 0). |
| R5 | The engineer folds the **helper** into `KeycloakAdminTokenProviderTests`, not just the constants | Called out above as a boundary rule; an explicit reviewer check. |
| R6 | The trx baseline artifact expires (14-day retention) before delivery | Re-capture against a newer green `develop` run; command is in `spec.md`. |
| R7 | The Aspire fixture is booted locally and takes the machine down | Explicitly forbidden in `spec.md`; CI is the only route (ADR-0103). |
| R8 | CI is green but the integration job was skipped or cancelled | `develop` has no required status checks — read the four buckets manually, and read the **trx**, not the job colour. |

---

## Phase-4a colour and the two-agent split

**Characterisation / green** (ADR-0144), confirmed in `spec.md` against the tree.

Phase 4a has nothing to *write*: the four covering tests already exist and are green. Its
obligation is to **capture them green before the change** and return the verbatim output.
That capture is:

1. the four `Passed` rows and the `<Counters/>` line from run `35506884306`'s trx, and
2. the four Docker-free guards G1–G4 reproducing their recorded baselines on the untouched
   branch tip.

If any guard does **not** reproduce on the untouched tip, the baseline was taken against a
different tree and the whole check is void — stop and report, do not re-baseline.

---

## Role assignments (ADR-0144 phase↔role binding)

| Phase | Agent | Why |
|---|---|---|
| 4a — baseline capture | **test-writer** | The work is test evidence, not implementation: run the covering suite's recorded outcomes and the four guards, return verbatim output. It writes no new test (there is none to write for a characterisation colour where the cover already exists), which is exactly the boundary this role holds. |
| 4b — the fold | **backend-engineer** | C# source edits in a .NET test project inside the Identity context's suite — the same reasoning spec 189 (#2274) used for the identical kind of change. **Not infra-engineer:** no Aspire wiring, CI workflow, Docker or Keycloak realm file is edited; the realm JSON is only *read*, at phase 1, as a verification source. |
| 5 — verify | **the lane's `/verify`** | Evidence is the PR's own integration trx (four `Passed`, `failed="0"`) plus G1–G4 re-run. Latency: N/A, stated. |
| 6 — review | **backend-reviewer** | C#/.NET review: conventions, the R5 boundary rule, and — critically — that the guards were **actually run and their output pasted**, not merely asserted. A guard reported only in prose is invisible to every later reader. |
| 6 — security | **not required** | See `spec.md`'s auth note: dev-only realm-import secret, already committed in five places, no production assembly, no trust boundary crossed, copy count strictly reduced. Recorded rather than silently skipped. |

Phase 4b **may not** edit the tests to pass, and **may not** weaken any guard to reach
green (ADR-0144).

---

## Parallelism (ADR-0109)

**There is none, and that is a finding rather than an omission.** The two target files are
disjoint, so they would ordinarily earn `[P]`. They do not here, because the whole change is
a single commit of six deletions that must be verified as one unit — splitting it across two
agents costs more coordination than the edit itself contains, and G2/G3/G4 are computed over
**both files together**, so a half-applied change cannot be evaluated. One agent, one pass,
one commit.

The orchestrator can still run this issue concurrently with others: nothing outside
`tests/Integration.Tests/Identity/` is touched, and the contention sweep in `spec.md` found
no open work on these files.
