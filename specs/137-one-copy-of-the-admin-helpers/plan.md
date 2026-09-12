# Plan 137 — One copy of the admin helpers

**Spec:** [`spec.md`](spec.md) · **Issue:** #2182 · **Branch:**
`refactor/2182-one-copy-of-the-admin-helpers`

## Bounded context and layers

**None.** This is `tests/Integration.Tests/Identity/` only — a test project. No
`Domain`, `Application`, `Infrastructure` or `Api` assembly is touched; no bounded
context gains or loses anything; no `Shared.Contracts` message changes. The
no-cross-context rule (NetArchTest) is unaffected because no production project
reference moves. There are no entities, value objects, invariants, domain events or
integration events in scope, and consequently no constitution §II primitive-boundary
surface: `PrimitiveBoundaryTests` binds domain models, and a test helper is not one.

## Design: `RealmProbe` becomes the single home, additively

`RealmProbe` is chosen over a new file because it is the better-documented of the two,
it already has a second consumer, and moving it would break that consumer for nothing.

### Hard constraint — `RealmProbe`'s existing surface is frozen

`KioskPrivilegeSweepStartupIntegrationTests` depends on `Realm`, `AdminClientId`,
`AdminClientSecret`, `LongLivedCredentialPrivilege`, `PlantAsync(clientId, attributes,
ct)`, `EffectiveRealmRolesAsync(clientId, ct)` and `DeleteAsync(clientId, ct)`. **No
existing member may change name, signature, visibility or behaviour.** That file is not
edited by US-1 at all — it is the control that proves the fold was additive.

### `RealmProbe.cs` — three changes, all additive

1. **`AuthorisedAdminClientAsync` becomes `public`** (from `private`, `:130`).
   Signature, body and the one-client comment (`:141–144`) are unchanged. Needed
   because `An_account_the_provider_creates_holds_it_until_something_removes_it` plants
   *without* attributes (`KioskInherited…:76–87`) and `PlantAsync` requires them.
   Deliberately **not** solved by routing that test through `PlantAsync` with an empty
   dictionary: that would move an assertion (`created.IsSuccessStatusCode.ShouldBeTrue()`
   → `PlantAsync`'s message-carrying one) and break the invariance check for a cosmetic
   gain. The inline POST body stays where it is; it is not one of the eight rows.

2. **`ReadJsonAsync` becomes `public`** (from `private static`, `:156`). Signature and
   body unchanged. Needed by the one helper that stays behind in the Kiosk file
   (`DeleteClientAsync`, row 8, out of scope).

3. **`EffectiveRealmRolesOfUserAsync(string username, CancellationToken)` is added**,
   lifted verbatim from `KioskInherited…:222–231` with cancellation threaded, and the
   composite read it shares with `EffectiveRealmRolesAsync` factored into a private
   `CompositeRealmRolesAsync(HttpClient admin, string userId, CancellationToken)` —
   the exact structure the Kiosk file already uses (`:233–241`). This refactors
   `RealmProbe`'s **own** body: its `EffectiveRealmRolesAsync` loses the inlined third
   read and calls the new private helper instead. Same three URLs, same projection, same
   return type. Verified equivalent at phase 1 (spec §row 6).

   Cost check (ADR-0084): `RealmProbe.cs` is 166 lines; +~25, −~8 → ~183. Under 300.

4. The class doc's third paragraph (`:21–26`) — *"The helpers mirror the private ones in
   `KioskInheritedPrivilegeIntegrationTests` … folding the two together is a tidy-up for
   whoever touches it next"* — must be rewritten. It will be false the moment this lands,
   and a stale comment claiming duplication is the same defect class as a stale record.
   Replace it with what is **still** true: the delete helper is the one shape left
   duplicated, and why (spec row 8 / US-2).

### `KioskInheritedPrivilegeIntegrationTests.cs` — six deletions, one addition

| Action | Member | Lines |
|---|---|---|
| Delete | `Realm`, `AdminClientId`, `AdminClientSecret` | `:34–36` |
| Delete | `LongLivedCredentialPrivilege` + its doc | `:38–39` |
| Delete | `AuthorisedAdminClientAsync` | `:183–202` |
| Delete | `EffectiveRealmRolesAsync` | `:204–220` |
| Delete | `EffectiveRealmRolesOfUserAsync` | `:222–231` |
| Delete | `CompositeRealmRolesAsync` | `:233–241` |
| Delete | `ReadJsonAsync` | `:243–251` |
| **Keep** | `DeleteClientAsync` — row 8, out of scope; rebuilt on `realm.AuthorisedAdminClientAsync` + `realm.ReadJsonAsync`, its unchecked `DeleteAsync` loop **unchanged** | `:253–265` |
| **Keep** | `CreateAdminClient` — Kiosk-only, not duplicated; its four `Realm`/`AdminClientId`/`AdminClientSecret` reads become `RealmProbe.*` | `:147–181` |
| **Keep** | `KioskRepresentation` — Kiosk-only | `:131–145` |
| Add | `private readonly RealmProbe realm = new(aspire);` — no leading underscore (house rule); initialiser captures the primary-constructor parameter, the shape `CreateAdminClient` already relies on | after `:32` |

Call-site rewrites, all mechanical:

- `LongLivedCredentialPrivilege` → `RealmProbe.LongLivedCredentialPrivilege` (5 sites:
  `:55`, `:94`, `:113`, `:124`, `:127`)
- `Realm` → `RealmProbe.Realm` (`:77`, `:152`), `AdminClientId` → `RealmProbe.AdminClientId`
  (`:154`), `AdminClientSecret` → `RealmProbe.AdminClientSecret` (`:155`)
- `await EffectiveRealmRolesAsync(clientId)` → `await realm.EffectiveRealmRolesAsync(clientId, CancellationToken.None)` (`:52`, `:91`)
- `await EffectiveRealmRolesOfUserAsync(x)` → `await realm.EffectiveRealmRolesOfUserAsync(x, CancellationToken.None)` (`:111`, `:124`)
- `await AuthorisedAdminClientAsync()` → `await realm.AuthorisedAdminClientAsync(CancellationToken.None)` (`:72`, and inside the retained `DeleteClientAsync`)

Expected file size: 266 → ~180 lines.

### What is deliberately not done

- **Row 8, the checked delete.** Spec §row 8. Folding it adds an assertion inside four
  `finally` blocks and can turn characterisation red — a refactor that is also a
  strengthening is two issues (ADR-0144). File US-2 as a follow-up issue referencing
  #2166.
- **The other two credential copies** (`KeycloakAdminTokenProviderTests:19–20`,
  `MqttAudienceIntegrationTests:66–67`). Out of the covering suite, out of scope. US-3.
- **Any production-code change.** None is required and none is permitted here.

## Messaging

N/A — no domain event, no integration event, no Wolverine handler, no queue.

## Boundary rules

Unchanged and unaffected. `Integration.Tests` already references
`Identity.Application` and `Identity.Infrastructure`; no new reference is added. The
NetArchTest suite is untouched.

## Phase 4a — colour, procedure and the mechanical check

**Declared colour: behaviour-preserving → characterisation, observed green.**
No production behaviour changes; no test's assertion changes. Ambiguity resolved: rows
5 and 8 were the two candidates for "this is actually a change", row 5 is
non-observable and stays in, row 8 is observable and is out.

### The characterisation run — CI trx, both ends

**Before:** run `34682095657`, SHA `e892b27c713d9d2c161bf14f08e7f985dfcce9a7`, all six
tests `Passed`, `passed="488" failed="0"`. Captured and quoted in
[`spec.md`](spec.md#independent-end-to-end-test-procedure); the commands are there
verbatim. If this spec is picked up after **2026-09-26**, re-capture — artifact
retention is 14 days.

**After:** the same two commands against this PR's own `integration tests (Docker)`
job. Both ends go in the PR body. **Do not boot the Aspire fixture on this machine** —
spec §Independent end-to-end test procedure states why.

### The assertion-invariance check

The refactor *must* edit a covering test file, so "the tests pass unmodified" needs a
mechanical meaning. Extract every statement containing `.Should`, byte-compare.

Extractor — versioned at [`assertions.sh`](assertions.sh) in this spec directory, reproduced here so the plan is self-contained:

```sh
#!/bin/sh
for f in "$@"; do
  sed -E '/^[[:space:]]*\/\//d' "$f" \
  | sed -E 's/;[[:space:]]*$/;\x01/' \
  | tr '\n' ' ' \
  | tr '\001' '\n' \
  | grep -F '.Should' \
  | sed -E 's/RealmProbe\.//g; s/[[:space:]]+/ /g; s/^ //; s/ $//'
done
```

Run, from the repo root:

```sh
sh specs/137-one-copy-of-the-admin-helpers/assertions.sh \
  tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs \
  tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs \
| sha256sum
# must print: 66ed302493ddc309016f7f00095bae361e891398f815ae4c4822b19028056661

sh specs/137-one-copy-of-the-admin-helpers/assertions.sh tests/Integration.Tests/Identity/RealmProbe.cs | sha256sum
# must print: 974fe47efb32fc964169b766b044e65b0581cb53c3d68b8f1fb32fc7f81ec9fd
```

Both hashes are captured from `origin/develop` at phase 3 and **must be reproduced
byte-for-byte after the change**. 11 assertion statements in the first set, 2 in the
second; the full text of all 13 is in [`baseline-assertions.txt`](baseline-assertions.txt).

**What the check catches.** Any edit to an asserted value, an asserted expression, an
assertion's Shouldly method, a failure message (including a single character of one), or
the order of assertions within a file. Any assertion added to or removed from either
file — which is precisely what folding row 8 would do, so the check is also what keeps
row 8 out.

**What it does not catch, stated honestly.**

- It normalises `RealmProbe.` away, so `LongLivedCredentialPrivilege` →
  `RealmProbe.LongLivedCredentialPrivilege` passes silently. That is the **one**
  permitted mechanical edit and it is only safe because both constants are literally
  `"offline_access"` (`RealmProbe.cs:35`, `KioskInherited…:39`, both verified at phase
  1). **Task T007 re-asserts that constant's value independently**, because this
  normalisation is the single hole the engineer could drive a changed value through.
- It is blind to everything outside an assertion statement: a changed URL, a changed
  planted attribute, a swapped `clientId`, a `try`/`finally` restructure, a deleted
  `[Fact]`. A test method deleted wholesale takes its assertions with it and the hash
  *would* change — but a test **renamed** or moved between files would not be caught by
  the per-file hash alone. T006 guards this with a test-name inventory.
- It splits statements at a `;` that ends a physical source line. Two statements on one
  line would merge. None exist in these files today; if the engineer writes one, the
  hash changes and the check fires conservatively — a false alarm, not a miss.
- It removes only whole-line comments (`^\s*//`), so a trailing `//` comment added
  inside an assertion statement changes the hash. Again conservative.
- **It proves nothing about runtime behaviour.** It is a source-text guard. The trx
  comparison is the behavioural evidence; this is the guard on the thing the trx cannot
  see, namely whether the green was earned by the same assertions.

An engineer who finds either hash changed **stops and reports**. Adjusting an assertion
to restore the hash, or editing the extractor, are both gate weakenings under ADR-0144.

## Risks

| Risk | Mitigation |
|---|---|
| The second consumer breaks | `KioskPrivilegeSweepStartupIntegrationTests.cs` is **not edited**; the build plus its two trx outcomes prove it |
| A 401 in CI from a mis-threaded token | The folded `AuthorisedAdminClientAsync` body is `RealmProbe`'s existing one, already green in run `34682095657` via `PlantAsync`/`DeleteAsync` |
| Row 8 quietly folded anyway | The assertion-invariance hash fires — an added assertion changes it |
| Local fixture boot kills the machine | Stated prohibition in spec and in T000; the route is CI |
| Baseline artifact expired | 14-day retention; T001 re-captures from the newest green `develop` run |

## Engineer

**Backend.** C# test code in `tests/Integration.Tests/`, xUnit/Shouldly, primary
constructors, `CancellationToken` plumbing, the Keycloak Admin API shapes. No frontend
file and no `AppHost`, `ci.yml`, Docker, Helm or realm-import file is touched, so
neither frontend nor infra is exercised — though the *evidence* is a CI artifact, which
the engineer must be fluent with (`gh run download`; note `jq` is not installed, use
`gh --json … -q`).
