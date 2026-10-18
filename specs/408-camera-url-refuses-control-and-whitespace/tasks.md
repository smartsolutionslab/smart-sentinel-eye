# Tasks 408: A camera URL holding a control character or whitespace is refused, and the simulator drops an address the parser cannot read

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #3031 (feature-level, on Project #13 and
picked up by the autonomous lane — no per-task issues, per CLAUDE.md Phase 3)

**Phase 4a colour: red** (behaviour-changing — the accepted input narrows, `ToOrigin` withholds more shapes, an audit
`DetailChanged` moves from `true` to `null`, and a simulator retry becomes a log-and-drop). Characterisation tasks
(T010-T017) are observed green on `c8c3773c` before any production edit, then unmodified. Red tasks (T030-T039) are
observed failing, the failure quoted verbatim in the PR. Ambiguity resolves to red. The engineer may not edit any
test from T010-T039, including the six moved rows.

**Roles.** Tests: `test-writer` (T001, T010-T039, T060), then `test-adversary` (T070). Production code:
`backend-engineer` only (T040-T043, T050). Review: `backend-reviewer` + `security-reviewer` (T080). No frontend,
infra or migration work.

Format: `[ID] [P?] [Story] description — files`. `[P]` = owns disjoint files from every other `[P]` task in the same
phase (ADR-0109). One branch, one index: `[P]` means disjoint *files*, committed by the orchestrator in sequence.

---

## Phase 0 — Setup (blocks everything)

- [ ] **T001 [US1, US2]** Re-scan on the branch tip: `grep -rn "RtspUrl\.From(\|StreamSourceUrl\.From(" src` lists
  exactly W1-W4 of spec 394 §1.4 (`RegisterCameraRequest.cs:19`, `CameraEndpoints.cs:312`,
  `ProvisionStreamCommandHandler.cs:33`, `RepointStreamCommandHandler.cs:126`); `grep -rn "RtspPath\.TryExtract" src`
  lists only `CameraRegisteredSimHandler.cs:38`. Re-run the spec §1.4 counterfactual if anything under `tests/` that
  carries an `rtsp://` literal changed since `c8c3773c` (`git diff --stat c8c3773c -- tests`), and list any new
  contradicted row in plan §7 and the PR. Record whether spec 406 (#3025) is merged into the tip (plan §3.4). No
  file changes unless a new row is found.

## Phase 1 — Characterisation, captured green first (unchanged rows)

All on unmodified production code, all green. Each `[P]` owns its own new file.

- [ ] **T010 [P] [US1, US2]** C1 baseline: run every plan §5.4 command; quote pass counts (the six rows of spec §1.4
  are green here and quoted by name). No file change.
- [ ] **T011 [P] [US1]** C2 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlControlCharacterCharacterisationTests.cs`.
- [ ] **T012 [P] [US1]** C3 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlControlCharacterCharacterisationTests.cs`.
- [ ] **T013 [P] [US1]** C4 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredControlCharacterTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredControlCharacterTests.cs`.
- [ ] **T014 [P] [US1]** C5 `tests/Architecture.Tests/Persistence/CameraUrlControlCharacterHydrationCharacterisationTests.cs`.
- [ ] **T015 [P] [US1a]** C6 `tests/Shared.Kernel.Tests/UrlRedactionControlCharacterCharacterisationTests.cs`.
- [ ] **T016 [P] [US1a]** C7 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadControlCharacterCharacterisationTests.cs`.
- [ ] **T017 [P] [US2]** C8 `tests/ScenarioSimulator.Tests/RtspPathUnreadableAddressCharacterisationTests.cs` (not
  `RtspPathTests.cs`, which spec 406 creates).

**Gate:** T010-T017 green on `c8c3773c`, counts quoted. Commit 1 (plan §6).

## Phase 2 — Red (US1, US1a, US2)

Each `[P]` owns its file(s). No red test calls `ContainsControlOrWhiteSpace` or
`ContainsControlOrWhiteSpaceWithinTheText` directly (they do not exist yet; a compile error is not a red) — G1
reaches them by source scan only. The row moves of spec §1.4 belong to the task that owns the receiving file, and
each deletes exactly the named line(s) from the old file, nothing else.

- [ ] **T030 [P] [US1]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlControlCharacterTests.cs`; move row 1 out
  of `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostAdversarialTests.cs:151`.
- [ ] **T031 [P] [US1]** R2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlControlCharacterTests.cs`;
  move row 2 out of `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostAdversarialTests.cs:151`.
- [ ] **T032 [P] [US1, US1a]** R3 `tests/Architecture.Tests/Persistence/CameraUrlControlCharacterRefusalTests.cs`.
- [ ] **T033 [P] [US1a]** R4 `tests/Shared.Kernel.Tests/UrlRedactionControlCharacterTests.cs`; move rows 4-6 out of
  `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAdversarialTests.cs:162-163` and
  `tests/Shared.Kernel.Tests/UrlRedactionUserInfoAsHostTests.cs:147`.
- [ ] **T034 [P] [US1a]** R5 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadControlCharacterTests.cs`;
  move row 3 (the whole `A_zone_holding_a_line_break_is_dropped_from_the_origin_with_the_rest_of_the_zone` fact) out
  of `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadZoneDelimiterAdversarialTests.cs:59-69`.
- [ ] **T035 [P] [US1]** R6 `tests/Architecture.Tests/CameraUrlControlCharacterEndpointTests.cs`.
- [ ] **T036 [P] [US1]** R7 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyControlCharacterReplayTests.cs`.
- [ ] **T037 [P] [US2]** R8 `tests/ScenarioSimulator.Tests/RtspPathUnreadableAddressTests.cs`.
- [ ] **T038 [US1, US1a]** G1 in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` (new theory + two new
  facts only; no existing line changes).
- [ ] **T039 [US1, US1a, US2]** Run every plan §5.4 command and return the **verbatim** output: Phase 1 still green;
  T030-T038 red for the reasons in plan §5.2/§5.3. Any of them arriving green is a phase-4 failure — stop and report.
  Commit 2.

## Phase 3 — Implementation (`backend-engineer`)

T040 blocks T041/T042 (they call the new members). T041, T042 and T050 own disjoint files: `[P]`. T050 is
independent of T040.

- [ ] **T040 [US1, US1a]** `src/Shared.Kernel/UrlAuthority.cs` — `ContainsControlOrWhiteSpace` and
  `ContainsControlOrWhiteSpaceWithinTheText` (plan §3.1; reuse `IsTrimmedByTheParser`; `Ensure.That(url).IsNotNull()`);
  `src/Shared.Kernel/UrlRedaction.cs` — the one `||` (plan §3.2). Nothing else in either file.
- [ ] **T041 [P] [US1]** `src/CameraCatalog/Domain/Camera/RtspUrl.cs` — the last `Satisfies`, the message literal,
  one doc-comment sentence. `FromStored` and `EnsureStoredInvariants` untouched.
- [ ] **T042 [P] [US1]** `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` — same.
- [ ] **T043 [US1, US1a]** Verification for T040-T042: `dotnet build -c Release` of the touched projects (analyzers
  clean, IDE0005/IDE0007/IDE1006), `dotnet format --verify-no-changes`, every plan §5.4 command except the
  ScenarioSimulator one green, `PrimitiveBoundaryTests` + NetArchTest boundary suite + `CameraUrlGuardedUriReadTests`
  green unmodified. Commit 3.
- [ ] **T050 [P] [US2]** `src/ScenarioSimulator/CameraSim/RtspPath.cs` — the guard of plan §3.4 around every `Uri`
  call in `TryExtract`, `catch (IndexOutOfRangeException)` only. **Before editing**: if spec 406 has merged, rebase
  first and put its `uri.Host` read inside the guard (and confirm R8/C8's `/live` and `/s` rows reflect plan §3.4);
  if not, guard what is there and note in the PR that 406's rebase must move its read inside. `CameraRegisteredSimHandler.cs`
  and `Log.cs` untouched. ScenarioSimulator §5.4 command green; Release build of `ScenarioSimulator` clean. Commit 4.

## Phase 4 — Post-green tests

- [ ] **T060 [P] [US1, US1a]** `test-writer`: direct rows in a new
  `tests/Shared.Kernel.Tests/UrlAuthorityControlCharacterDirectTests.cs` — null guards for both members;
  `ContainsControlOrWhiteSpace` true for exactly the 84 code units of spec §1.1 and false for every other BMP code
  unit (one enumerating fact, with the Cf members named as false); `ContainsControlOrWhiteSpaceWithinTheText` false
  for leading-only, trailing-only and both-edges-only inputs, true with any non-member on both sides, false for the
  empty string and an all-member string. Green on arrival is expected. Commit 5.

## Phase 5 — Adversarial, verify, review, PR

- [ ] **T070 [US1, US1a, US2]** `test-adversary`: (a) every member at every position of every spec 389/392/394
  corpus address, both `From`s and `ToOrigin`, asserting `From` refuses and that `ToOrigin` withholds unless the
  member is in an edge run; (b) surrogate pairs and lone surrogates adjacent to members; a member at index 2047 of a
  2048-character address; (c) mixed edge and interior runs (`" rtsp://cam/a b "`); (d) `RtspPath`: hunt any input
  on which a `Uri` call it makes throws something other than `IndexOutOfRangeException`, or on which `Handle` still
  throws; (e) the 400 body and the Warning never echo the address. Findings -> tests or written acceptance.
- [ ] **T071 [US1, US1a, US2]** Phase 5 `/verify`: spec §8 on a live stack. Latency: N/A.
- [ ] **T080 [US1, US1a, US2]** Phase 6: `backend-reviewer` and `security-reviewer` (request-line injection via a
  stored raw CR/LF; the exact set and the Cf exclusion; the edge exemption in `ToOrigin` (A-1) and that it never
  renders a host carrying a member; error-text leakage; `FromStored` accept set unchanged; the `RtspPath` catch
  scoped to `IndexOutOfRangeException`; the six moved rows). Resolve or accept in writing.
- [ ] **T090** Phase 7: PR to `develop` (`--base develop`). Body: quotes T010 counts and the T039 red verbatim; lists
  the six moved rows (plan §7) with their justification; flags A-1 (edge exemption), A-2 (rows 4-6), A-4 (reused log
  line), A-6 (UI does not trim) and A-8 (#3023 item 4) for the owner; carries the **replay note** of spec FR-004;
  records the merge order with spec 406 (plan §3.4); re-checks spec number 408. `Closes` is **not** used — #3031
  keeps items 3-8 open — so the PR says `Part of #3031 (items 1, 2)` and the orchestrator comments the delivered items
  on #3031 (and A-8 on #3023) after the merge.

## Dependencies

```
T001 → T010..T017 [P] (gate: green, commit 1)
     → T030..T038 [P] → T039 (red quoted, commit 2)
     → T040 → (T041 [P] + T042 [P]) → T043 (green, commit 3)
     → T050 [P with T040-T043; committed after T043] (green, commit 4)
     → T060 → T070 → T071 → T080 → T090
```

Foundational: **T040** (`Shared.Kernel`) blocks T041/T042. Fan-out points: Phase 1 (seven disjoint new files plus a
baseline run), Phase 2 (eight disjoint new files, four of which also delete named lines from one existing file each;
T038 alone edits the shared guard file), and Phase 3 (T041, T042, T050 in three contexts). External: T050 is ordered
against spec 406 (#3025) on `RtspPath.cs`.
