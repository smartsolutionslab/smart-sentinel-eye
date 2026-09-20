# Tasks — Spec 191, one source of the admin credentials

**Issue:** #2275 · **Branch:** `2275-one-source-of-admin-credentials` ·
**Phase-4a colour:** characterisation / **green**

Format: `[ID] [P?] [Story]`. **No task carries `[P]`** — see `plan.md` § Parallelism: the
guards G2/G3/G4 are computed over both target files together, so a half-applied change
cannot be evaluated. One agent, one pass, one commit.

All commands assume the repo root as the working directory. `$D` is
`specs/191-one-source-of-the-admin-credentials`.

---

## Phase 4a — capture the characterisation green (agent: **test-writer**)

Nothing is written here. The four covering tests already exist; the obligation is to
capture them green **before** the change and return the output verbatim.

- [ ] **T001** [US-1] **Confirm the four Docker-free guards reproduce on the untouched
      branch tip.** Run before touching anything. If any differs from the recorded value,
      the baseline was taken against a different tree and the whole check is void —
      **stop and report; do not re-baseline.**
      ```sh
      F1=tests/Integration.Tests/Identity/KeycloakAdminTokenProviderTests.cs
      F2=tests/Integration.Tests/Identity/MqttAudienceIntegrationTests.cs
      D=specs/191-one-source-of-the-admin-credentials

      # G1 — RealmProbe values unchanged (comment-stripped code hash)
      sh $D/code.sh tests/Integration.Tests/Identity/RealmProbe.cs | sha256sum
      # 1dd598765d8b7aac0d0a202b82dd82e50a2bf031e36b4e2630b2f7afcf6ee7be

      # G2 — assertion invariance over the two target files
      sh specs/137-one-copy-of-the-admin-helpers/assertions.sh $F1 $F2 | sha256sum
      # 0ebec6e86ed5a9c0f926831995bbda43bc1833b01aa169801457ec862453ecd2

      # G3 — test-name inventory (4 names)
      grep -A3 '\[Fact\]' $F1 $F2 \
      | grep -oE 'public async Task [A-Za-z_]+' | sed 's/public async Task //' \
      | sort | sha256sum
      # 5f8c4d56a1f82e8279bd8b92dbb956aca18c098ac5b2bdcc033fe734c3e972c2

      # G4 — quoted-literal count, BEFORE (must be 3 and 3)
      grep -c '"identity-admin"\|"dev-only-identity-admin-secret"\|"smart-sentinel-eye"' $F1 $F2
      ```
      **Done when:** all four printed and matched, output pasted verbatim into the report.

- [ ] **T002** [US-1] **Re-capture the CI trx baseline if it has expired.** Artifact
      retention is 14 days; run `35506884306` (develop @ `f256aaf7`) expires **2026-10-04**.
      If `gh run download` fails, pick the newest green `develop` run
      (`gh run list --branch develop --workflow ci.yml --status success --limit 5`),
      re-run the extractor from `spec.md`, and **update the baseline table in `spec.md`**
      with the new run id, SHA and counters.
      **Done when:** four `Passed` rows and a `failed="0"` counters line are in hand, with
      the run id they came from.
      ```sh
      rm -rf /tmp/trx191 && mkdir -p /tmp/trx191
      gh run download 35506884306 -n integration-test-results -D /tmp/trx191
      T=/tmp/trx191/tests/Integration.Tests/TestResults/integration.trx
      grep -oE '<UnitTestResult[^>]*testName="[^"]*(KeycloakAdminTokenProvider|MqttAudience)[^"]*"[^>]*outcome="[^"]*"' "$T" \
      | sed -E 's/.*testName="([^"]*)".*outcome="([^"]*)".*/\2\t\1/'
      grep -o '<Counters[^>]*/>' "$T"
      ```

**Gate 4a:** both guards and trx captured green, output verbatim. Hand to 4b as its brief.
**4b may not edit the tests to pass, and may not weaken a guard to reach green.**

---

## Phase 4b — the fold, one commit (agent: **backend-engineer**)

- [ ] **T003** [US-1] **`KeycloakAdminTokenProviderTests.cs` — delete lines 19–21 and
      point `CreateProvider()` at `RealmProbe`.**
      `Realm` → `RealmProbe.Realm`, `AdminClientId` → `RealmProbe.AdminClientId`,
      `AdminClientSecret` → `RealmProbe.AdminClientSecret`.
      **Constants only.** Do **not** route `CreateProvider()` through
      `RealmProbe.AuthorisedAdminClientAsync`: that helper constructs a
      `KeycloakAdminTokenProvider`, which is the code this file tests, and a test that both
      acts and observes through its subject proves nothing (`plan.md` R5).
      No `using` change is needed — same namespace, `public sealed class`.
      **Done when:** the file compiles and contains no quoted realm literal.

- [ ] **T004** [US-1] **`MqttAudienceIntegrationTests.cs` — delete lines 61 and 66–67 (with
      the two-line `//` comment that explains 66–67) and point all six uses at
      `RealmProbe`.**
      `Realm` appears in four interpolated URLs (`admin/realms/{Realm}/clients`, the
      `?clientId=` lookup, the `DELETE`, and the token endpoint).
      `AdminClientId`/`AdminClientSecret` appear at two call sites, both
      `MintClientCredentialsTokenAsync(...)` — in `DisposeAsync` and in
      `CreateAudiencelessClientAsync`.
      **Leave `ApiAudience` and `PublishScope` alone** — realm values, but not admin
      credentials, not hosted by `RealmProbe`, not in scope.
      **Do not fold `MintClientCredentialsTokenAsync` or `CreateAdminApiClient`** — a
      different shape from `AuthorisedAdminClientAsync`, serving three clients
      (`spec.md` US-3).
      **Done when:** the file compiles and contains no quoted realm literal.

- [ ] **T005** [US-1] **Update `RealmProbe.cs`'s consumer list — comment only.**
      Its class doc names `KioskInheritedPrivilegeIntegrationTests` and
      `KioskPrivilegeSweepStartupIntegrationTests` as the classes that read its credentials
      rather than keeping copies. Add one sentence naming the two constant-only consumers
      this spec adds, and spec 191.
      **Hard constraint:** G1 must be **unchanged** afterwards. If it moves, the edit was
      not comment-only — revert it; do not adjust the baseline.
      Optional: skip this task entirely if the doc reads correctly without it. It is
      preferred, not required.

- [ ] **T006** [US-1] **Build and format.**
      ```sh
      dotnet build tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release
      dotnet format --verify-no-changes
      ```
      Release treats warnings as errors (code metrics excepted, ADR-0084).
      **Note:** stop any running AppHost first — a live stack holds the service binaries and
      MSB3027 reads exactly like a broken build.
      **Done when:** both clean.

- [ ] **T007** [US-1] **Commit — one commit, Conventional Commits (ADR-0030), no
      `Co-Authored-By` (ADR-0086).**
      Suggested subject:
      `refactor(identity): one source for the admin credentials in the integration suite`
      Body: what moved, that `RealmProbe` needed no new member, and that the token-minting
      was deliberately not folded.

---

## Phase 4b verification — the guards, re-run (same agent, before handing off)

- [ ] **T008** [US-1] **All four guards, after the change.** Paste the output verbatim; a
      guard reported in prose and not in output is invisible to every later reader.
      ```sh
      F1=tests/Integration.Tests/Identity/KeycloakAdminTokenProviderTests.cs
      F2=tests/Integration.Tests/Identity/MqttAudienceIntegrationTests.cs
      D=specs/191-one-source-of-the-admin-credentials

      sh $D/code.sh tests/Integration.Tests/Identity/RealmProbe.cs | sha256sum
      # MUST still be 1dd598765d8b7aac0d0a202b82dd82e50a2bf031e36b4e2630b2f7afcf6ee7be

      sh specs/137-one-copy-of-the-admin-helpers/assertions.sh $F1 $F2 | sha256sum
      # MUST still be 0ebec6e86ed5a9c0f926831995bbda43bc1833b01aa169801457ec862453ecd2

      grep -A3 '\[Fact\]' $F1 $F2 \
      | grep -oE 'public async Task [A-Za-z_]+' | sed 's/public async Task //' \
      | sort | sha256sum
      # MUST still be 5f8c4d56a1f82e8279bd8b92dbb956aca18c098ac5b2bdcc033fe734c3e972c2

      grep -c '"identity-admin"\|"dev-only-identity-admin-secret"\|"smart-sentinel-eye"' $F1 $F2
      # MUST now be 0 and 0
      ```
      **Any mismatch blocks the change. Do not adjust a baseline to make it match.**

- [ ] **T009** [US-1] **Independent value check against the realm import.** G1 proves the
      values did not *move*; this proves they are *right*, from a source that is not the
      test suite (`an assertion must not check its own input`).
      ```sh
      grep -E '"realm":' src/AppHost/Realms/smart-sentinel-eye-realm.json | head -1
      sed -n '254,265p' src/AppHost/Realms/smart-sentinel-eye-realm.json | grep -E '"clientId"|"secret"'
      grep -E 'public const string (Realm|AdminClientId|AdminClientSecret)' \
        tests/Integration.Tests/Identity/RealmProbe.cs
      ```
      **Done when:** the three `RealmProbe` constants equal `smart-sentinel-eye`,
      `identity-admin` and `dev-only-identity-admin-secret` as the realm import defines
      them. **Do not boot the Aspire fixture to check this** (`plan.md` R7).

---

## Phase 5 — verify (`/verify`)

- [ ] **T010** [US-1] **Read this PR's own integration trx.** Not the job's colour — the
      trx. `develop` has no required status checks, so a skipped or cancelled integration
      bucket must not be read as a pass.
      **Done when:** all four tests report `outcome="Passed"` and the counters line reads
      `failed="0"`, quoted in the verification note alongside the run id.
      **Latency:** N/A, stated explicitly (test-only; no leg of §IV's path touched).

---

## Phase 6 — review (agent: **backend-reviewer**)

- [ ] **T011** [US-1] **Review.** Beyond the usual conventions, check specifically:
      1. **R5** — `KeycloakAdminTokenProviderTests` takes `RealmProbe`'s *constants* and
         not its *helper*. Routing through `AuthorisedAdminClientAsync` would make the test
         observe through its own subject; that is a blocker, not a nit.
      2. `MintClientCredentialsTokenAsync` / `CreateAdminApiClient` are still present and
         unfolded, and `ApiAudience` / `PublishScope` are untouched.
      3. **The guard output is actually pasted**, before and after, not asserted in prose.
      4. Any `RealmProbe.cs` edit is comment-only, evidenced by G1 being unchanged.
      5. No test was deleted, renamed, skipped or weakened to reach green (ADR-0144).

**Security review: not required.** Dev-only realm-import secret, already committed in five
places, no production assembly, no trust boundary crossed, copy count strictly reduced.
Recorded rather than silently skipped.

---

## Phase 7 — PR

- [ ] **T012** [US-1] `gh pr create --base develop` with the template. Body must carry the
      before/after trx figures, the four guard hashes, `Phase 4a: characterisation — green,
      observed before the change`, and a closing keyword for **#2275**.
      Check the issue's state after the merge; a mention alone rarely closes it.

- [ ] **T013** [US-1] **File the US-2 follow-up issue** — the realm-import drift guard
      (`spec.md` US-2), red colour, covering `RealmProbe`'s three constants and, if
      practical, `AppHost.cs`'s `IdentityAdminClientSecret` default and
      `AppHostParameterOverrideTests.cs:50`. Reference #2275 and this spec, and add it to
      Project #13:
      ```sh
      gh project item-add 13 --owner smartsolutionslab --url <issue-url>
      ```
      **This is the task that actually answers the open question #2275 raised.** US-1 only
      shrinks the surface the drift can strike; US-2 is what makes drift fail the build
      instead of arriving as a 401 in an unrelated test.

---

## Dependencies

```
T001 ─┬─> T003 ─> T004 ─> T005 ─> T006 ─> T007 ─> T008 ─> T009 ─> T010 ─> T011 ─> T012 ─> T013
T002 ─┘
```

T001 and T002 are the phase-4a gate and both must pass before any edit. T003–T007 are one
commit. T008 and T009 gate the handoff out of phase 4b. Everything is sequential by
design (`plan.md` § Parallelism).

## Board

#2275 is already on Project #13 (Todo, `agent:ready`, `tech-debt`), verified with
`--limit 2000`. **The phase-3 gate is satisfied; nothing to add.** Per ADR-0037 as this
repo actually practises it since spec 028, no per-task issues are created — `tasks.md` is
the artifact this work is tracked against.
