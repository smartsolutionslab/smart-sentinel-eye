# Tasks 394: A camera URL with '@', '?' or '#' inside the IPv6 brackets is refused, and one the parser cannot read answers 400, not 500

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3023 (feature-level, on Project #13
and picked up by the autonomous lane — no per-task issues, per CLAUDE.md Phase 3)

**Phase 4a colour: red** (behaviour-changing — the accepted input narrows, `ToOrigin` withholds more shapes, and a
500 becomes a 400). Characterisation tasks (T010-T016) are observed green on `fbd4176b` before any production edit,
then unmodified. Red tasks (T030-T038) are observed failing, the failure quoted verbatim in the PR. Ambiguity
resolves to red. The engineer may not edit any test from T010-T038. Item 2 is a bug fix whose 500 -> 400 changes
**no** pinned test (plan §7).

**Roles.** Tests: `test-writer` (T001, T010-T038, T060), then `test-adversary` (T070). Production code:
`backend-engineer` only (T040-T043). Review: `backend-reviewer` + `security-reviewer` (T080). No frontend, infra,
migration or simulator work.

Format: `[ID] [P?] [Story] description — files`. `[P]` = owns disjoint files from every other `[P]` task in the
same phase (ADR-0109). One branch, one index: `[P]` means disjoint *files*, committed by the orchestrator in
sequence.

---

## Phase 0 — Setup (blocks everything)

- [ ] **T001 [US1, US2]** Re-scan on the branch tip: `grep -rn "RtspUrl\.From(\|StreamSourceUrl\.From(" src` lists
  exactly W1-W4 (spec §1.4); the two greps of plan §7 read hit by hit — any new row whose outcome this spec changes
  is added to plan §7 and the PR body. Re-run the spec §1.3 consumer fuzz as a scratch check (not committed) and
  quote: `Host throws` count, `ToOrigin throws 0`, `Audit throws 0`, `From accepts, RtspPath throws 0`. No file
  changes unless a new row is found.

## Phase 1 — Characterisation, captured green first (US2a, unchanged rows)

All on unmodified production code, all green. Each `[P]` owns its own new file.

- [ ] **T010 [P] [US1, US2]** C1 baseline: run every plan §5.4 command; quote pass counts. No file change.
- [ ] **T011 [P] [US1, US2]** C2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneDelimiterCharacterisationTests.cs`
  — spec §5 accept set (`From`, `FromStored` round-trip); §5 older-message set refused by `From` with today's exact
  message.
- [ ] **T012 [P] [US1, US2]** C3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneDelimiterCharacterisationTests.cs` — same.
- [ ] **T013 [P] [US2a]** C4 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredZoneDelimiterTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredZoneDelimiterTests.cs` — `FromStored` reads
  back every FR-001 and FR-002 row.
- [ ] **T014 [P] [US2a]** C5 `tests/Architecture.Tests/Persistence/CameraUrlZoneDelimiterHydrationCharacterisationTests.cs`
  — both EF converters return the stored value for every FR-001 and FR-002 row.
- [ ] **T015 [P] [US1, US2]** C6 `tests/Shared.Kernel.Tests/UrlRedactionZoneDelimiterCharacterisationTests.cs` — plan
  §5.1 C6 rows.
- [ ] **T016 [P] [US1]** C7 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneDelimiterCharacterisationTests.cs`
  — `DetailChanged` null both directions for the FR-001 pairs; no audit write throws for an FR-002 / FR-003 row.

**Gate:** T010-T016 green on `fbd4176b`, counts quoted. Commit 1 (plan §6).

## Phase 2 — Red (US1, US2)

Each `[P]` owns its file. No red test calls `DelimiterInsideTheBrackets`, `TryReadHost` or `TryReadUserInfo`
directly (they do not exist yet; a compile error is not a red) — G1/G2 reach them by source scan only.

- [ ] **T030 [P] [US1, US2]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlZoneDelimiterAndUnreadableHostTests.cs` (plan §5.2).
- [ ] **T031 [P] [US1, US2]** R2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlZoneDelimiterAndUnreadableHostTests.cs`.
- [ ] **T032 [P] [US1, US2]** R3 `tests/Architecture.Tests/Persistence/CameraUrlZoneDelimiterRefusalTests.cs` — FR-007 agreement.
- [ ] **T033 [P] [US1]** R4 `tests/Shared.Kernel.Tests/UrlRedactionZoneDelimiterTests.cs`.
- [ ] **T034 [P] [US1]** R5 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneDelimiterTests.cs`.
- [ ] **T035 [P] [US1, US2]** R6 `tests/Architecture.Tests/CameraUrlZoneDelimiterEndpointTests.cs`.
- [ ] **T036 [P] [US1, US2]** R7 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyZoneDelimiterReplayTests.cs`.
- [ ] **T037 [P] [US1, US2]** G2 `tests/Architecture.Tests/CameraUrlGuardedUriReadTests.cs`.
- [ ] **T038 [US1]** G1 in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` (new theory + new fact only;
  no existing line changes). Then run every plan §5.4 command and return the **verbatim** output: Phase 1 still
  green; T030-T038 red for the reasons in plan §5.2/§5.3. Any of them arriving green is a phase-4 failure — stop
  and report. Commit 2.

## Phase 3 — Implementation (`backend-engineer`)

T040 blocks T041/T042 (they call the new members). T041 and T042 are disjoint contexts: `[P]`.

- [ ] **T040 [US1, US2]** `src/Shared.Kernel/UrlAuthority.cs` — `DelimiterInsideTheBrackets`, `TryReadHost`,
  `TryReadUserInfo` and the `BracketDelimiters` constant (plan §3.1; `Ensure.That(url).IsNotNull()`; catch
  `IndexOutOfRangeException` only); `src/Shared.Kernel/UrlRedaction.cs` — the one `||` (plan §3.2). Nothing else
  in either file.
- [ ] **T041 [P] [US1, US2]** `src/CameraCatalog/Domain/Camera/RtspUrl.cs` — the two new `Satisfies` at the plan
  §3.3 positions, the two message literals, `HasHost` / `HasNoUserInfo` through the readers, one doc-comment
  sentence. `FromStored` and the rule list of `EnsureStoredInvariants` untouched.
- [ ] **T042 [P] [US1, US2]** `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` — same.
- [ ] **T043 [US1, US2]** Full verification: `dotnet build -c Release` (analyzers clean, IDE0005/IDE0007/IDE1006),
  `dotnet format --verify-no-changes`, every plan §5.4 command green, `git diff --stat -- '**/Migrations/**'`
  empty, `PrimitiveBoundaryTests` + NetArchTest boundary suite green unmodified, coverage gates (Domain >= 90%,
  Application >= 80%, Shared >= 90%). Commit 3.

## Phase 4 — Post-green tests

- [ ] **T060 [P] [US1, US2]** `test-writer`: direct rows in a new `tests/Shared.Kernel.Tests/UrlAuthorityZoneDelimiterDirectTests.cs`
  — null guards for the three members; `DelimiterInsideTheBrackets` true/false table (spec §5 FR-001 rows true;
  accept-set rows, `rtsp://us[er@[fe80::1]/x`, a bracket with no `]`, non-bracket hosts and `rtsp://@cam/s` false;
  never throws on the `UrlRedactionAuthorityHostAdversarialTests` degenerate inputs); `TryReadHost` /
  `TryReadUserInfo` true with the value, false for an unparseable input, false (not a throw) for an FR-002 /
  FR-003 row, and `TryReadUserInfo` **true** on `rtsp://[1:2:3:4:5:6:7:8:/64]` (the reason they are two members,
  plan §3.1). Green on arrival is expected: this covers code that now exists, not the new-behaviour evidence.
  Commit 4.

## Phase 5 — Adversarial, verify, review, PR

- [ ] **T070 [US1, US2]** `test-adversary`: (a) delimiters at every position inside and around the brackets, with
  empty, non-empty and `[]` userinfo, raw `%` and `%25` zones, prefix lengths before and after the zone, upper- and
  lower-case scheme; (b) hunt an input on which `Uri.Host`, `UserInfo`, `Port`, `DnsSafeHost` or `AbsolutePath`
  throws and that reaches `ToOrigin`'s later reads, `AuditPayloadRedaction.CarriesZone` or (accepted by `From`)
  `RtspPath.TryExtract` — a hit is a finding to file, not to fix here (spec §7); (c) any exception type other than
  `IndexOutOfRangeException` from `Uri` on these inputs (spec A-4). Findings -> tests or written acceptance.
- [ ] **T071 [US1, US2]** Phase 5 `/verify`: spec §8 on a live stack. Latency: N/A.
- [ ] **T080 [US1, US2]** Phase 6: `backend-reviewer` and `security-reviewer` (host confusion through a
  last-`@` parser; `ToOrigin` fail-closed on untrusted text; error-text leakage; the `FromStored` accept set
  unchanged and the A-3 exception-type change; the catch scoped to `IndexOutOfRangeException`). Resolve or accept
  in writing.
- [ ] **T090** Phase 7: PR to `develop` (`--base develop`). Body: quotes T010 counts and the T038 red verbatim;
  states plan §7 (no assertion changed); flags A-1..A-6 for the owner, especially A-1 (`\` left out) and A-3
  (`FromStored` exception type); re-checks spec number 394 against `origin/develop` and PR #3026; `Closes`
  is **not** used — #3023 keeps items 3, 4, 6, 7 open, so the PR says `Part of #3023 (items 1, 2, 5)` and the
  orchestrator comments the delivered items on the issue after the merge.

## Dependencies

```
T001 → T010..T016 [P] (gate: green, commit 1)
     → T030..T037 [P] → T038 (red quoted, commit 2)
     → T040 → (T041 [P] + T042 [P]) → T043 (green, commit 3)
     → T060 → T070 → T071 → T080 → T090
```

Foundational: **T040** (`Shared.Kernel`) blocks T041/T042. Fan-out points: Phase 1 (six disjoint new files plus a
baseline run), Phase 2 (eight disjoint new files; T038 alone edits the shared guard file), and T041/T042 (two
contexts).
