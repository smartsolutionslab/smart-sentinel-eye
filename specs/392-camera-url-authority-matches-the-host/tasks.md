# Tasks 392: A camera URL whose written authority is longer, or shorter, than the host the parser read is refused on write and redacted on read

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issues**: #3012, #3017 (feature-level, both on
Project #13 already and picked up by the autonomous lane — no per-task issues, per CLAUDE.md Phase 3)

**Phase 4a colour.** Two, by task, in separate commits (plan §6): **characterisation** (T010-T016, observed green
on `de9802b1` before any production edit, then unmodified) and **red** (T020-T021 for US3's structural guard;
T030-T037 for US1/US2; observed failing, failure quoted verbatim in the PR). Ambiguity resolves to red. The
engineer may not edit any test from T010-T037.

**Roles.** Tests: `test-writer` (T001, T010-T037, T060-T061), then `test-adversary` (T070). Production code:
`backend-engineer` only (T025, T040-T043). Review: `backend-reviewer` + `security-reviewer` (T080). No frontend,
infra or migration work.

Format: `[ID] [P?] [Story] description — files`. `[P]` = owns disjoint files from every other `[P]` task in the
same phase (ADR-0109). One branch, one index: `[P]` means disjoint *files*, committed by the orchestrator in
sequence.

---

## Phase 0 — Setup (blocks everything)

- [ ] **T001 [US1-3]** Re-scan on the branch tip: `grep -rn "RtspUrl\.From(\|StreamSourceUrl\.From(" src` lists
  exactly W1-W4 (spec §1.4); the two greps of plan §7 read hit by hit — any new row whose outcome this spec changes
  is added to plan §7 and the PR body. Re-run the spec §1.3 locator fuzz as a scratch check (not committed) and
  quote `reachable=… diff=0`. No file changes unless a new row is found.

## Phase 1 — Characterisation, captured green first (US2a, US3, unchanged rows)

All on unmodified production code, all green. Each `[P]` owns its own new file.

- [ ] **T010 [P] [US3]** C1 + C2 baseline: run `dotnet test tests/Shared.Kernel.Tests` and the `AuditPayload`
  filter of `tests/AuditObservability.Application.Tests`; quote pass counts. No file change.
- [ ] **T011 [P] [US3]** C3 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedLocatorCharacterisationTests.cs`
  — plan §5.1 C3 rows, both directions, through `IntegrationEventAuditHandler` (harness of
  `AuditPayloadDetailChangedUserinfoBracketTests`). Run the trim counterfactual once locally (drop `Trim()` -> the
  first row goes `null`), revert, record it in the class summary.
- [ ] **T012 [P] [US1, US2]** C4 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostCharacterisationTests.cs`
  — spec §5 accept set (`From` and `FromStored` round-trip); §5 older-message set refused by both with today's
  exact message.
- [ ] **T013 [P] [US1, US2]** C5 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostCharacterisationTests.cs` — same.
- [ ] **T014 [P] [US2a]** C6 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredAuthorityHostTests.cs` and
  `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlFromStoredAuthorityHostTests.cs` — `FromStored` reads
  back every FR-002 and FR-003 row.
- [ ] **T015 [P] [US2a]** C7 `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostHydrationCharacterisationTests.cs`
  — both EF converters' `ConvertFromProvider` return the stored value for every FR-002/FR-003 row (patterns:
  `CameraUrlHydrationCharacterisationTests`, `StreamSourceUrlHydrationCharacterisationTests`).
- [ ] **T016 [P] [US1, US2]** C8 `tests/Shared.Kernel.Tests/UrlRedactionAuthorityHostCharacterisationTests.cs` —
  plan §5.1 C8 rows.

**Gate:** T010-T016 green on `de9802b1`, counts quoted. Commit 1 (plan §6).

## Phase 2 — US3: the locator in one place (refactor)

- [ ] **T020 [US3]** G-US3a: edit `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` exactly as plan §5.3
  (two expected lists to `[Declaration]`, `AuditCopy` and the summary's recorded-exception paragraph removed, two
  method names). No other line of the file changes. The only authorised assertion edit in this spec.
- [ ] **T021 [US3]** G-US3b: same file, new fact `The_audit_redaction_asks_the_shared_locator`. Run plan §5.4's
  Architecture.Tests command; quote the red (`should be ["src/Shared.Kernel/UrlAuthority.cs"] but was [… AuditPayloadRedaction.cs, …]`
  and the missing call). Commit 2.
- [ ] **T025 [US3]** `backend-engineer`: `src/Shared.Kernel/UrlAuthority.cs` (public `TextFromTheAuthority`,
  `IndexOfTheTerminator`; `AtSignFollowsTheAuthority` rebuilt on them; FR-007 comment) and
  `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` (plan §3.3, comment updated). No value
  object, no `ToOrigin` change. Gate: T010 suites, T011, C9 unmodified green; T020/T021 green;
  `dotnet build -c Release` clean. Commit 3.

## Phase 3 — US1/US2 red

Depends on Phase 2 (the shared locator exists, but no red test may call a member that does not exist at the
commit it lands in — red tests reach behaviour through `From`, `ToOrigin`, `Serialize`, the endpoint host and the
handlers only). Each `[P]` owns its file.

- [ ] **T030 [P] [US1, US2]** R1 `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlAuthorityHostTests.cs` (plan §5.2).
- [ ] **T031 [P] [US1, US2]** R2 `tests/StreamDistribution.Domain.Tests/Stream/StreamSourceUrlAuthorityHostTests.cs`.
- [ ] **T032 [P] [US1, US2]** R3 `tests/Architecture.Tests/Persistence/CameraUrlAuthorityHostRefusalTests.cs` —
  (a) #3017's test verbatim: `An_empty_authority_followed_by_userinfo_looking_text_is_refused`,
  rows `rtsp:///admin:4242@cam/s`, `rtsp:////admin:4242@cam/s`; (b) FR-009 agreement.
- [ ] **T033 [P] [US2]** R4 `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedBracketTailTests.cs`
  — #3012's test `Text_written_after_the_closing_bracket_is_not_claimed_as_the_same_origin` (its three rows) plus
  the two added rows; asserts `JsonValueKind.Null` both directions and the placeholder per tailed side.
- [ ] **T034 [P] [US2]** R5 `tests/Shared.Kernel.Tests/UrlRedactionBracketTailTests.cs`.
- [ ] **T035 [P] [US1, US2]** R6 `tests/Architecture.Tests/CameraUrlAuthorityHostEndpointTests.cs`.
- [ ] **T036 [P] [US1]** R7 `tests/StreamDistribution.Application.Tests/EventHandlers/LegacyAuthorityHostReplayTests.cs`.
- [ ] **T037 [US1, US2]** G-US3c in `tests/Architecture.Tests/UrlAuthoritySingleSourceTests.cs` (new theory + new
  fact only; after T020/T021 so the file has one writer at a time). Then run every command in plan §5.4 and return
  the **verbatim** output: Phase 1 and Phase 2 still green; T030-T037 red for the reasons in plan §5.2. Any of
  them arriving green is a phase-4 failure — stop and report. Commit 4.

## Phase 4 — US1/US2 implementation (`backend-engineer`)

T040 blocks T041/T042 (they call the new member). T041 and T042 are disjoint contexts: `[P]`.

- [ ] **T040 [US2]** `src/Shared.Kernel/UrlAuthority.cs` — `TextFollowsTheClosingBracket` (plan §3.1, steps 1-5;
  `Ensure.That(url).IsNotNull()`; spans only, never throws); `src/Shared.Kernel/UrlRedaction.cs` — the one `||`
  (plan §3.2).
- [ ] **T041 [P] [US1, US2]** `src/CameraCatalog/Domain/Camera/RtspUrl.cs` — `HasHost`, the two `Satisfies` after
  the raw-`@` one, the FR-002/FR-003 literals, doc comment (plan §3.4). `FromStored` untouched.
- [ ] **T042 [P] [US1, US2]** `src/StreamDistribution/Domain/Stream/StreamSourceUrl.cs` — same.
- [ ] **T043 [US1-3]** Full verification: `dotnet build -c Release` (analyzers clean, IDE0005/IDE0007),
  `dotnet format --verify-no-changes`, every plan §5.4 command green, `git diff --stat -- '**/Migrations/**'` empty,
  `PrimitiveBoundaryTests` + NetArchTest boundary suite green unmodified, coverage gates (Domain >= 90%,
  Application >= 80%, Shared >= 90%). Commit 5.

## Phase 5 — Post-green tests

- [ ] **T060 [P] [US2, US3]** `test-writer`: direct rows in `tests/Shared.Kernel.Tests/UrlAuthorityTests.cs`
  (append) or a new `UrlAuthorityLocatorTests.cs` — null guard for `TextFollowsTheClosingBracket`; its true/false
  table (spec §5 FR-003 rows true; accept-set bracket rows, `[]@[…]` rows and non-bracket hosts false; must not
  throw on the 200,000-input seeded fuzz pieces); `TextFromTheAuthority` / `IndexOfTheTerminator` rows (scheme,
  no scheme, `\\`, `///`, empty). Green on arrival is expected: these cover code that now exists, not the
  new-behaviour evidence. Commit 6.
- [ ] **T061 [US1, US2]** Confirm `CameraUrlRefusalAgreementTests` (spec 389) still passes unmodified and that its
  corpus rows `rtsp:///admin:4242#@cam` still meet the raw-`@` message. No file change.

## Phase 6 — Adversarial, verify, review, PR

- [ ] **T070 [US1-3]** `test-adversary`: attack the bracket scan against the parser — a seeded fuzz over bracket,
  zone, port, userinfo, `\`, `%`, whitespace and terminator pieces comparing, for every input `From` accepts, the
  written authority after the userinfo with `Uri.Host` (+ `:Port` when not default); `]` at 2048 characters;
  multiple `]`; `[` inside the userinfo with a tail on the host; uppercase/lowercase hex; IPv4-mapped; `%5D`
  (encoded `]`). Also try to get an empty host past `HasHost` (`rtsp://%20/`, `rtsp://./`, control characters). And
  re-try the US3 refactor: any input where the audit `DetailChanged` differs between `de9802b1` and the tip.
  Findings -> tests or written acceptance.
- [ ] **T071 [US1-2]** Phase 5 `/verify`: spec §8 on a live stack. Latency: N/A.
- [ ] **T080 [US1-3]** Phase 6: `backend-reviewer` and `security-reviewer` (credentials after an empty host;
  `ToOrigin` fail-closed on untrusted text; error-text leakage; `FromStored` bypass unchanged; the refactor's
  equivalence argument in spec §1.3). Resolve or accept in writing.
- [ ] **T090** Phase 7: PR to `develop` (`--base develop`). Body: quotes T021 and T037 outputs verbatim; lists the
  G-US3a edit (plan §7) as the only assertion change; states the refactor and the behaviour change are separate
  commits (plan §6); flags A-1..A-5 for the owner; re-checks spec number 392; `Closes #3012` and `Closes #3017`.
  After merge: confirm both issues closed (a mention rarely auto-closes).

## Dependencies

```
T001 → T010..T016 [P] (gate: green, commit 1)
     → T020 → T021 (red quoted, commit 2) → T025 (green, commit 3)
     → T030..T036 [P] → T037 (red quoted, commit 4)
     → T040 → (T041 [P] + T042 [P]) → T043 (green, commit 5)
     → T060 [P], T061 → T070 → T071 → T080 → T090
```

Foundational: **T025** and **T040** (both `Shared.Kernel`) block everything after them. Fan-out points: Phase 1
(seven disjoint new files), Phase 3 (seven disjoint new files), and T041/T042 (two contexts).
