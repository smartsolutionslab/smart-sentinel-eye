# Tasks 137 — One copy of the admin helpers

**Spec:** [`spec.md`](spec.md) · **Plan:** [`plan.md`](plan.md) · **Issue:** #2182

Declared at phase 3: **behaviour-preserving** → phase 4a is **characterisation, observed
green**. Engineer: **backend**. No ADR is needed — see spec §Locked tech choices.

Everything below is **US-1**. US-2 (the checked delete) and US-3 (the other two
credential copies) are **not delivered**; T010 files them.

**Parallelism (ADR-0109) is nearly nil here and that is the point.** The two source
files are edited in one pass because the second's compile depends on the first's
surface. The only `[P]` markers are on the two independent evidence-gathering tasks.

---

## Foundational — blocks everything

- [ ] **T000** [US-1] **Do not boot the Aspire fixture on this machine.** C: is at 96%
      (9.9 GB free); the fixture boots six containers into the Docker vhdx on C: and the
      known failure mode is the Docker engine ceasing to answer until a GUI restart,
      which ends the whole run. The fixture's 8-minute startup timeout also sits against
      a 10-minute tool ceiling. Evidence for this spec comes from CI artifacts only.
      **Done when:** acknowledged; no `dotnet test` on
      `tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj` has been run
      locally at any point in this delivery.

- [ ] **T001** [P] [US-1] **Re-capture the "before" trx if the baseline has aged.**
      Baseline in hand: run `34682095657`, `develop`, SHA
      `e892b27c713d9d2c161bf14f08e7f985dfcce9a7`, 2026-09-12T07:58:57Z, all four jobs
      green, `<Counters total="488" executed="488" passed="488" failed="0" …/>`, and all
      six covering tests `outcome="Passed"`. Artifact retention is **14 days**, so
      re-capture if picked up after **2026-09-26**, using the newest green `develop`
      run.
      ```sh
      gh run list --branch develop --workflow ci.yml --limit 10 \
        --json databaseId,conclusion,headSha -q '.[] | select(.conclusion=="success") | .databaseId' | head -1
      rm -rf /tmp/trx && mkdir -p /tmp/trx
      gh run download <RUN_ID> -n integration-test-results -D /tmp/trx
      T=/tmp/trx/tests/Integration.Tests/TestResults/integration.trx
      grep -oE '<UnitTestResult[^>]*testName="[^"]*(KioskInheritedPrivilege|KioskPrivilegeSweepStartup)[^"]*"[^>]*outcome="[^"]*"' "$T" \
        | sed -E 's/.*testName="([^"]*)".*outcome="([^"]*)".*/\2\t\1/'
      grep -o '<Counters[^>]*/>' "$T"
      ```
      (`jq` is not installed — `gh --json … -q` only.)
      **Done when:** six `Passed` lines and one `<Counters …/>` line are written into the
      PR-body draft verbatim, with the run id and SHA.

- [ ] **T002** [P] [US-1] **Confirm the two assertion-invariance hashes reproduce on the
      unmodified branch tip**, before touching anything. If either differs from the
      recorded value, the baseline was taken against a different tree and the whole
      check is void — stop and report.
      ```sh
      sh specs/137-one-copy-of-the-admin-helpers/assertions.sh \
        tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs \
        tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs | sha256sum
      # 66ed302493ddc309016f7f00095bae361e891398f815ae4c4822b19028056661
      sh specs/137-one-copy-of-the-admin-helpers/assertions.sh \
        tests/Integration.Tests/Identity/RealmProbe.cs | sha256sum
      # 974fe47efb32fc964169b766b044e65b0581cb53c3d68b8f1fb32fc7f81ec9fd
      ```
      **Done when:** both hashes printed and matched.

---

## The change — one pass, one commit

- [ ] **T003** [US-1] **Widen `RealmProbe`, additively.** In
      `tests/Integration.Tests/Identity/RealmProbe.cs`:
      `AuthorisedAdminClientAsync` `private` → `public` (`:130`, body unchanged,
      one-client comment at `:141–144` kept);
      `ReadJsonAsync` `private static` → `public static` (`:156`, body unchanged);
      add `public Task<IReadOnlyList<string>> EffectiveRealmRolesOfUserAsync(string
      username, CancellationToken cancellationToken)`, lifted from
      `KioskInheritedPrivilegeIntegrationTests.cs:222–231` with cancellation threaded;
      factor the composite read out of `EffectiveRealmRolesAsync` into a private
      `CompositeRealmRolesAsync(HttpClient admin, string userId, CancellationToken
      cancellationToken)` — same URL, same projection.
      **Constraint:** no existing member changes name, signature or behaviour —
      `KioskPrivilegeSweepStartupIntegrationTests` depends on seven of them.
      **Done when:** `RealmProbe.cs` compiles, is under 300 lines (ADR-0084), and its
      assertion hash is still `974fe47e…` — the two `ShouldBeTrue` calls in
      `PlantAsync` and `DeleteAsync` must not move.

- [ ] **T004** [US-1] **Rewrite the class doc's third paragraph** (`RealmProbe.cs:21–26`).
      It currently says the helpers *"mirror the private ones in
      `KioskInheritedPrivilegeIntegrationTests`"* and that folding them is a tidy-up —
      false the moment T003/T005 land. Replace with what stays true: the **delete**
      helper is the one shape still duplicated, it is deliberately not folded because
      `DeleteAsync` asserts and `DeleteClientAsync` does not, and the follow-up issue
      from T010 carries it.
      **Done when:** no sentence in the file claims a duplication that no longer exists,
      and the one that does exist is named with its reason.

- [ ] **T005** [US-1] **Fold the call sites and delete the duplicates** in
      `tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs`.
      Delete `:34–36` (the three constants), `:38–39`
      (`LongLivedCredentialPrivilege` + doc), `:183–202`
      (`AuthorisedAdminClientAsync`), `:204–220` (`EffectiveRealmRolesAsync`),
      `:222–231` (`EffectiveRealmRolesOfUserAsync`), `:233–241`
      (`CompositeRealmRolesAsync`), `:243–251` (`ReadJsonAsync`).
      Add `private readonly RealmProbe realm = new(aspire);` (no leading underscore).
      Rewrite the call sites exactly as plan.md §"Call-site rewrites" lists them.
      **Keep** `KioskRepresentation`, `CreateAdminClient` (constants now read
      `RealmProbe.*`) and **`DeleteClientAsync` with its unchecked `DeleteAsync` loop
      intact** — rebuilt on `realm.AuthorisedAdminClientAsync` and
      `RealmProbe.ReadJsonAsync`.
      **`KioskPrivilegeSweepStartupIntegrationTests.cs` is not edited.**
      **Done when:** the Release build is clean (analyzers included),
      `grep -c 'dev-only-identity-admin-secret'
      tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs`
      prints `0`, and `git diff --name-only` lists exactly two `.cs` files.

---

## Gate — the mechanical checks

- [ ] **T006** [US-1] **Assertion invariance and test-name inventory.** Both must hold.
      ```sh
      sh specs/137-one-copy-of-the-admin-helpers/assertions.sh \
        tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs \
        tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs | sha256sum
      # must still be 66ed302493ddc309016f7f00095bae361e891398f815ae4c4822b19028056661

      sh specs/137-one-copy-of-the-admin-helpers/assertions.sh \
        tests/Integration.Tests/Identity/RealmProbe.cs | sha256sum
      # must still be 974fe47efb32fc964169b766b044e65b0581cb53c3d68b8f1fb32fc7f81ec9fd

      grep -A2 '\[Fact\]' \
        tests/Integration.Tests/Identity/KioskInheritedPrivilegeIntegrationTests.cs \
        tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs \
      | grep -oE 'public async Task [A-Za-z_]+' | sed 's/public async Task //' | sort | sha256sum
      # must be 2c47131718949a525e45e39dd144d5a8b5939e0ee2cba9c36e9f6a6bcf62d971  (6 names)
      ```
      The name inventory closes the hole the per-file assertion hash leaves: a test
      renamed or moved between the two files would keep the combined assertion text
      identical. The six names are listed in plan.md.
      **If any hash differs, stop and report.** Adjusting an assertion to restore a
      hash, or editing `assertions.sh`, is a gate weakening under ADR-0144 and is a
      blocked outcome, not a judgement call.
      **Done when:** all three hashes match and are quoted in the PR body.

- [ ] **T007** [US-1] **Re-assert the constant the normalisation hides.** The extractor
      strips the `RealmProbe.` qualifier, so `LongLivedCredentialPrivilege` →
      `RealmProbe.LongLivedCredentialPrivilege` passes silently. That is only safe while
      the surviving constant holds the same literal.
      ```sh
      grep -n 'LongLivedCredentialPrivilege = ' tests/Integration.Tests/Identity/RealmProbe.cs
      # must print exactly one line, value "offline_access"
      grep -nE 'const string (Realm|AdminClientId|AdminClientSecret) =' tests/Integration.Tests/Identity/RealmProbe.cs
      # smart-sentinel-eye / identity-admin / dev-only-identity-admin-secret, unchanged
      ```
      **Done when:** the four literals are shown unchanged from
      `git show origin/develop:tests/Integration.Tests/Identity/RealmProbe.cs`.

---

## Phase 5 — the only behavioural evidence

- [ ] **T008** [US-1] **Read this PR's own integration trx.** Open the PR (phase 7 opens
      it; the lane does not wait for CI), then when the `integration tests (Docker)` job
      concludes run T001's commands against **this PR's** run id.
      **Done when:** the same six test names report `outcome="Passed"` and the run's
      `<Counters …/>` shows `failed="0"`, quoted verbatim in the PR body **beside** the
      before-figures. A characterisation test that went red is a **stop**, not something
      to adjust — report and take `agent:blocked`.
      **What this does not prove, and must be written down as such:** that the tests pass
      on a developer machine, and that the realm secret is correct anywhere but CI. It is
      the same evidence every integration change in this repo stands on (ADR-0103), and
      no better route exists — a local fixture boot is what T000 forbids.

---

## Closing out

- [ ] **T009** [US-1] **PR body.** Must carry: the before trx table (T001), the after trx
      table (T008), the three hashes (T006), `Phase 4a: characterisation, observed
      green`, and an explicit statement that **#2182's row 8 is not closed** — the two
      delete helpers had diverged, one asserts and one does not, and folding them would
      have added an assertion to four green tests. Link the follow-up issue from T010.
      Conventional Commits, no `Co-Authored-By` (ADR-0086), base `develop` (ADR-0028).

- [ ] **T010** [US-1] **File the two follow-ups and put the feature issue on the board.**
      - US-2: *"`KioskInheritedPrivilegeIntegrationTests.DeleteClientAsync` discards the
        DELETE response; `RealmProbe.DeleteAsync` asserts it"* — behaviour-changing,
        references #2166 and this spec's row 8.
      - US-3: *"`identity-admin` / `dev-only-identity-admin-secret` are still written in
        three files"* — `KeycloakAdminTokenProviderTests:19–20`,
        `MqttAudienceIntegrationTests:66–67`, `RealmProbe:31–32`.
      - Board (phase 3 gate): the **feature** issue #2182 on Project #13 —
        `gh project item-add 13 --owner smartsolutionslab --url
        https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2182`. No per-task
        issues (the practice ended after spec 028). `item-add` prints nothing on success
        and `item-list` defaults to 30 — verify with `--limit 2000`. Needs the `project`
        scope.
      **Done when:** both follow-ups exist with numbers, and #2182 is confirmed on the
      board by `content.url`, not by number filter.

---

## Dependencies

```
T000 ──┬─> T001 [P] ─────────────────────────────> T008 ─> T009 ─> T010
       └─> T002 [P] ─> T003 ─> T004 ─> T005 ─> T006 ─> T007 ─┘
```

T003 before T005: the Kiosk file will not compile until `RealmProbe`'s surface exists.
T002 strictly before T003: a baseline confirmed after an edit confirms nothing.
