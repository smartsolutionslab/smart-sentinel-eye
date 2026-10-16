# Tasks 387: `DetailChanged` is unknown when either camera address is an IPv6 literal with a zone

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3006 (feature-level; no per-task
issues).

**Phase 4a colour: RED** (behaviour-changing: the stored `DetailChanged` value for a zoned side changes from
`true`/`false` to `null`), **plus an adversarial pass**. Z1-Z5 and the one re-pin compile on unchanged code
and must be seen **failing on an assertion**. C1-C4 must arrive green and stay green unmodified.

**Engineer:** backend-engineer (T010). **Test-writer:** T001-T002. **Test-adversary:** T020.
**Security-sensitive: yes** — phase 6 runs `security-reviewer` alongside `/code-review`.
**New ADR:** no (owner decision on #3006).

**Authorised test edit (only this one):** the `("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth1]/x", true)`
row of `AuditPayloadRedactionPostFixAdversarialTests.DetailChanged_edges`, moved to its own fact asserting
`null`, per plan §4.2.

**Forbidden in every task:** any other edit to an existing test file (incl. `AuditPayloadDetailChangedAdversarialTests.cs`,
`AuditPayloadDetailChangedUnknownTests.cs`, `AuditPayloadRedactionTests.cs`, every `Shared.Kernel.Tests/UrlRedaction*`
file and `Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.cs`); any production file other than
`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`; `UrlRedaction.cs` (`ToOrigin`'s output
must not change), `RtspUrl.cs`, `StreamSourceUrl.cs`, `Shared.Contracts`; migrations; the UI; ADRs, the
constitution, CLAUDE.md; suppressions, thresholds, `.editorconfig`, analyzer configs.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).
**Foundational:** none — no Shared.Contracts, AppHost or Aspire change. T001 and T002 own disjoint files in one
test project and may run in parallel; T010 is the only production task.

---

## Phase 1: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] New
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedZoneTests.cs` per plan
  §4.1: Z1, Z2 (8 rows), Z3 (2), Z4, Z5 (3) red; C1 (5), C2 (2), C3, C4 green.
- [ ] **T002** [P] [US1] The one re-pin per plan §4.2 in `AuditPayloadRedactionPostFixAdversarialTests.cs`.

Run:

```
dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~AuditPayload"
```

Expected: **16 failed** (Z1; Z2 x8; Z3 x2; Z4; Z5 x3; the new
`Two_link_local_addresses_that_differ_only_in_their_zone_report_the_detail_change_as_unknown`), each on an
assertion: `should be Null but was True` for Z1-Z4 and the re-pin, `... but was False` for Z5. Every other
`AuditPayload*` test passes, including the four remaining `DetailChanged_edges` rows and every spec 384 test.

Commit T001-T002 together: `test(3006): pin DetailChanged as unknown when an address carries an IPv6 zone (red)`.
Return the **verbatim** red output.

Gate: the orchestrator holds the verbatim red output, every red failing on an assertion and every green
passing, before dispatching T010.

## Phase 2: Green (backend-engineer)

- [ ] **T010** [US1] `src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs` per plan §2:
  a `CarriesZone` check (`!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.HostNameType ==
  UriHostNameType.IPv6 && uri.DnsSafeHost.Contains('%'))`) on both raw values, after the placeholder check and
  before the ordinal rule, returning `null`; extend the existing *why* comment by one clause. `ToOrigin` is
  not touched. Run the four counterfactuals in plan §4.4 (each must leave its named tests red), then plan §4.4
  in full. T001-T002 green **unmodified**. Do not stop or boot a shared Aspire stack.
  Commit: `fix(3006): report DetailChanged as unknown when an address carries an IPv6 zone`.

## Phase 3: Adversarial (test-adversary)

- [ ] **T020** [US1] New `AuditPayloadDetailChangedZoneAdversarialTests.cs` per plan §4.3. Every fact passes
  against T010; a failing one is a finding returned verbatim (fix red-first as T011), not an edited test.
  Commit: `test(3006): adversarial zone spellings for DetailChanged`.

- [x] **T021** [US1] Finding B1 (security review): red tests
  `tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedBracketContentTests.cs`
  (c30072c5; 10 assertion failures `should be Null but was True`) for a prefix length or zone written inside the
  brackets, which the parser strips from `DnsSafeHost`.
- [x] **T022** [US1] B1 fix in `AuditPayloadRedaction.cs` only (FR-006): written bracket content other than hex
  digits, `:` and `.` makes the value unknown; unlocatable content is refused. Tests unmodified. Commit:
  `fix(audit): report DetailChanged as unknown when the bracket content carries more than an address`.

## Phase 4: Verify, review, PR (orchestrator)

- [ ] **T030** Phase 5 per spec §5: step 1 always; step 3 only if no other Aspire stack runs; otherwise state
  that steps 1-2 stand. Quote the T001-T002 red output and the post-fix green run in the PR body.
- [ ] **T031** Phase 6: `/code-review` and `security-reviewer` (ask it to confirm the detection on the pinned
  runtime, that `ToOrigin`'s output is byte-identical, and that no other audit path derives "same origin" from
  two `ToOrigin` results). Raise in the PR body: the rejected `DnsSafeHost != Host` detection and why (spec §1.2
  finding 1), the either-side scope (FR-002: zone-vs-different-host and identical zoned addresses move from
  `false` to `null`), the one re-pin and its authorising clause, that existing rows keep their stored `true`
  (#2990), and the audit-copy re-serialisation limitation (plan §6).
- [ ] **T032** Re-check the spec number (387) against `origin/develop` and every branch head (385 #2991, 386
  #3001, 388 #2994 are siblings) before `gh pr create --base develop`; PR body uses `Closes #3006`.

## Dependencies

T001 ‖ T002 -> T010 -> T020 -> T030 -> T031 -> T032.
