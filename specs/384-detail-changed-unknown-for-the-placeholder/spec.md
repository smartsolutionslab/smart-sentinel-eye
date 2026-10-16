# Spec 384: `DetailChanged` is unknown when either camera address redacts to the placeholder

**Feature Branch**: `fix/2998-detailchanged-unknown-for-placeholder` (cut from `origin/develop` at `bd04dd8e`,
which contains specs 377, 379 and 381)

**Created**: 2026-10-16

**Status**: Implemented (T010), CI pending. Rows already written keep their stored `true` (#2990 tracks rewriting them). No `[NEEDS CLARIFICATION]` remains: the rule was decided
by the owner on #2998 (option 1, recorded in the issue comment "Decision (owner delegated, 2026-10-16)").
One reading of that rule where two of its clauses overlap is marked **[Assumption]** in §6.

**Input**: Issue [#2998](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2998),
*DetailChanged claims 'same origin' when both sides redact to the placeholder, so a camera host change can
be hidden in the audit trail*. Found by the security review of #2993 (spec 381).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (the stored `DetailChanged` value changes for every
address change in which a side redacts to `<unparseable url>`): phase 4a is **red** (ADR-0139).
**Security-sensitive** (the failure mode is concealment of a host move in the audit trail): phase 4
includes a `test-adversary` pass, phase 6 includes `security-reviewer`.

**Spec number.** 384. Checked 2026-10-16 against `origin/develop` (highest: 383) and every local and
remote branch head (`git ls-tree <ref> specs/`): 386 is on `test/3001-crossfab-devices-webhook-403`; 385 is
reserved by the sibling architect for #2991. Nothing claims 384. Re-check before the PR is opened.

**ADRs.** ADR-0139 / ADR-0144 (phase 4a red; the lane implements a decision it did not make), ADR-0036
(smallest change), ADR-0065 (Application >= 80% coverage), ADR-0084 (metrics, advisory), ADR-0105
(`Ensure.That`, unchanged). Constitution §III (no cross-context reference; `Shared.Contracts` untouched).
**No new ADR**: the owner decided the rule on #2998; no contract, scope, persistence shape or value object
changes. The stored payload gains a third value for an existing additive member.

**Latency budget.** N/A. `AuditPayloadRedaction.Serialize` runs in AuditObservability's audit write path,
which is on no §IV leg. The change replaces one `ToOrigin` pair with the same pair and adds two ordinal
comparisons.

---

## 1. What was found (premise verified, not assumed)

### 1.1 The concealment, reproduced

`DetailChanged` (spec 377 FR-003, `AuditPayloadRedaction.DetailChanged`) is today:

```
previousUrl is not null && url is not null
  && ToOrigin(previousUrl) == ToOrigin(url)        // ordinal
  && previousUrl != url                            // ordinal
```

`<unparseable url>` == `<unparseable url>`, so two placeholder sides with different raw text yield `true`,
which reads as "same origin, only the detail changed". A scratch file-based app
(`%TEMP%\claude\...\scratchpad\probe384\probe.cs`, `#:project` reference to the real
`src/Shared.Kernel/SmartSentinelEye.Shared.Kernel.csproj` at `bd04dd8e`) ran the real `UrlRedaction.ToOrigin`:

| Input | `ToOrigin` |
|---|---|
| `rtsp://cam-line4/s?x=@` | `<unparseable url>` (spec 381: `@` after the authority) |
| `rtsp://attacker-rtsp/s?x=@` | `<unparseable url>` |
| `rtsp://cam／a＝M1` | `<unparseable url>` (spec 379: non-ASCII authority) |
| `rtsp://operator:2468#@cam/s`, `rtsp://operator:1357#@cam/s` | `<unparseable url>` |
| `garbage SECRET1`, `garbage SECRET2` | `<unparseable url>` (not an absolute URI) |
| `rtsp://[fe80::1%25eth0]/x`, `rtsp://[fe80::1%25eth1]/x` | `rtsp://[fe80::1]` (read) |
| `rtsp://CAM/x`, `rtsp://cam/x `, `rtsp://cam`, `rtsp://cam/` | `rtsp://cam` (read) |
| `rtsp://10.0.5.12/h264?token=…`, `rtsp://cam/SECRET/a`, `rtsp://user:pass@cam/x` | read |

So `rtsp://cam-line4/s?x=@` -> `rtsp://attacker-rtsp/s?x=@` audits today as
`{"PreviousUrl":"<unparseable url>","Url":"<unparseable url>","DetailChanged":true}`: the host move is
hidden, and the stored payload is the only copy (the previous host cannot be recovered).

### 1.2 Every test that pins `DetailChanged` (grep `DetailChanged` across `tests/`, `src/`, `apps/`)

| # | Test (file : line) | Inputs | Sides | Pinned today | Under the decision | Change? |
|---|---|---|---|---|---|---|
| 1 | `AuditPayloadHostCharsetRedactionTests.Changing_between_two_different_non_ascii_addresses_still_reports_a_detail_change` (:94) | `rtsp://cam／a＝…1` -> `…2` | P / P | `true` | `null` | **yes — named in the decision** |
| 2 | `AuditPayloadUserInfoRedactionTests.Changing_between_two_cut_short_addresses_stores_the_placeholder_and_reports_a_detail_change` (:71) | `rtsp://operator:2468#@cam/s` -> `…1357…` | P / P | `true` | `null` | **yes — "spec 381's equivalent", named in the decision** |
| 3 | `AuditPayloadRedactionPostFixAdversarialTests.DetailChanged_edges`, row `("garbage SECRET1", "garbage SECRET2", true)` (:101) | not a URI | P / P | `true` | `null` | **yes — see below** |
| 4 | same theory, rows `CAM/x`~`cam/x`, `cam`~`cam/`, `cam/x`~`cam/x `, `[fe80::1%25eth0]`~`[…eth1]` | read / read, equal origin, raw differs | R / R | `true` | `true` | no |
| 5 | same theory, row `cam:554/x`~`cam/x` | read / read, origins differ | R / R | `false` | `false` | no |
| 6 | `AuditPayloadRedactionPostFixAdversarialTests.Both_null_still_audits_with_no_detail_change` (:92) | null / null | — | `false` | `false` | no |
| 7 | `AuditPayloadRedactionTests.An_address_change_within_one_origin_stores_both_origins_and_a_detail_change` (:129) | `rtsp://cam/a?…` -> `rtsp://cam/b?…` | R / R | `true` | `true` | no |
| 8 | `AuditPayloadRedactionTests.An_address_change_across_origins_shows_the_move_and_no_detail_change` (:140) | `10.0.5.12` -> `10.0.5.44` | R / R | `false` | `false` | no |
| 9 | `AuditPayloadRedactionTests.An_address_change_to_an_identical_url_records_no_detail_change` (:149) | identical, read | R / R | `false` | `false` | no |
| 10 | `AuditPayloadRedactionTests.An_address_change_with_a_null_side_records_no_detail_change_and_still_audits` (:162, 2 rows) | null + read | — | `false` | `false` | no |
| 11 | `AuditPayloadRedactionTests.The_audit_api_row_for_an_address_change_carries_no_credential` (:212) | `rtsp://cam/SECRET/a` -> `…/b` | R / R | `true` | `true` | no |
| 12 | `AuditPayloadUserInfoRedactionTests.Changing_between_addresses_with_a_proper_userinfo_still_stores_their_origins` (:94) | `user:pass@cam` -> `cam2` | R / R | `false` | `false` | no |
| 13 | `AuditPayloadRedactionPostFixAdversarialTests.A_serialisation_that_does_not_end_in_a_closing_brace_…` (:138) | passes the literal `"DetailChanged":true` as a member to `AppendMember` | — | (not a `DetailChanged` value) | unchanged | no |
| 14 | `Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.A_query_string_credential_reaches_neither_the_audit_row_nor_the_audit_api` (:96, CI-only) | `rtsp://10.0.5.12/h264?token=A` -> `…token=B` | R / R | `true` | `true` | no |

(P = redacts to the placeholder, R = origin read.)

**Row 3 is a third change, and the decision authorises it.** The decision names two tests and adds:
"grep for the other tests that pin `DetailChanged` true for two placeholder/placeholder addresses and
update ONLY those". Row 3 is exactly that shape (both sides are `<unparseable url>`, pinned `true`). No other
row is placeholder-involving, so **exactly three assertions change**, and nothing else. No existing test pins
`false` with a placeholder side (the one-sided case is untested today; §3 adds it).

### 1.3 How the member is produced and who reads it

- **Producer.** `Registration.Suffix` (`Func<IIntegrationEvent, string>`) returns the literal member text;
  `AppendMember` splices it before the closing `}` of the serialised redacted record. `DetailChangedMember`
  maps `bool` to `"DetailChanged":true` / `"DetailChanged":false`. A `bool?` therefore needs no serialiser
  change: the mapping gains a `null` arm producing `"DetailChanged":null`, which is valid JSON and valid
  `jsonb` (the column is `jsonb`; `AuditEventRepository` inserts `{payload}::jsonb`).
- **Consumers (none read the member).**
  - `AuditRowMapper.Map` passes `Payload.Content.Value` through as a string.
  - The search API / `AuditEventRepository` filter on columns, never on payload content.
  - `AzureBlobAuditChunkArchiver` writes the payload string into NDJSON unchanged.
  - `apps/management-web/src/features/audit/AuditPage.tsx` `formatPayload` pretty-prints
    `JSON.parse(payload)`; a `null` member renders as `"DetailChanged": null`. No `.ts`/`.tsx` in `apps/`
    mentions `DetailChanged`.
  - `docs/`: no mention. `Shared.Contracts/CameraCatalog/CameraAddressChangedV1.cs` mentions the flag in an
    XML remark ("a flag for a change that stays on one origin"), which stays accurate and is not edited.

  Nothing assumes a non-null boolean, so no consumer breaks.

## 2. User story

### US1 (P1) — An audit reader is never told "same origin" when no origin was read

As an auditor reading a `CameraAddressChangedV1` row, when either address could not be reduced to an origin,
I see `DetailChanged: null` (unknown), so a host move hidden behind the placeholder is not presented as a
detail change on one origin.

**Why P1 / why one slice.** One rule in one function, observable in the stored row and the audit API answer.
It is the whole issue.

**Independent test.** Handle a `CameraAddressChangedV1` through `IntegrationEventAuditHandler` with an
`InMemoryAuditEventRepository` and read `DetailChanged` in the stored payload and in
`AuditRowMapper.Map(...).Payload` (plan §4).

#### Acceptance scenarios

```gherkin
Feature: DetailChanged is unknown when an origin was not read

  Scenario: The concealment from #2998 is no longer reported as a detail change
    Given a camera address change from "rtsp://cam-line4/s?x=@" to "rtsp://attacker-rtsp/s?x=@"
    When the audit row is written
    Then "PreviousUrl" and "Url" are "<unparseable url>"
    And the stored payload contains the member "DetailChanged":null
    And the audit API row's payload has "DetailChanged" null

  Scenario Outline: Either side redacting to the placeholder makes DetailChanged unknown
    Given a camera address change from "<previous>" to "<url>"
    When the audit row is written
    Then "DetailChanged" is null
    Examples:
      | previous                    | url                         |
      | rtsp://cam-line4/s          | rtsp://attacker-rtsp/s?x=@  |
      | rtsp://cam-line4/s?x=@      | rtsp://cam-line4/s          |
      | rtsp://cam／a               | rtsp://cam/a                |
      | rtsp://cam-line4/s?x=@      | rtsp://cam-line4/s?x=@      |

  Scenario: Both origins read and equal, raw differs - unchanged (true)
    Given a change from "rtsp://cam/a?token=OLD" to "rtsp://cam/b?token=NEW"
    Then "DetailChanged" is true

  Scenario: Both origins read and different - unchanged (false)
    Given a change from "rtsp://10.0.5.12/h264" to "rtsp://10.0.5.44/h264"
    Then "DetailChanged" is false

  Scenario: A null side - unchanged (false), even when the other side is the placeholder
    Given a change from null to "rtsp://cam-line4/s?x=@"
    Then "DetailChanged" is false and the row is still written

  Scenario: A redelivered, already redacted message stays unknown
    Given a change whose "PreviousUrl" and "Url" are both the text "<unparseable url>"
    Then "DetailChanged" is null
```

**Bad-request / auth / conflict scenarios.** N/A: no endpoint, scope, fab check or optimistic-concurrency
path changes. The audit handler is a message consumer; a null wire field (spec 377 FR-005) is the only
malformed input and keeps its behaviour (scenario "A null side").

## 3. Functional requirements

- **FR-001** For `CameraAddressChangedV1`, `DetailChanged` is:
  - `false` if either raw value is null (unchanged rule, evaluated first — §6 [Assumption]);
  - otherwise `null` if either `ToOrigin(raw)` is `UrlRedaction.Unparseable`;
  - otherwise `true` iff the two origins are ordinally equal and the raw values ordinally differ;
  - otherwise `false`.
- **FR-002** The `null` value is stored as the literal member `"DetailChanged":null` (the key is present).
  Rows written before spec 377 keep having no key; nothing rewrites old rows (out of scope, #2990).
- **FR-003** `PreviousUrl` and `Url` redaction, `VerifyRedacted`, `AppendMember`, the register and every other
  registered event are unchanged. `CameraRegisteredV1` has no suffix and is unaffected.
- **FR-004** Exactly the three assertions in §1.2 rows 1-3 change, each from `true` to `null`. No other
  existing test assertion changes.

## 4. Out of scope

- Rewriting existing audit rows (#2990).
- Rejecting `?@` / `#@` / non-ASCII forms at registration (`RtspUrl`, `StreamSourceUrl`; #2999).
- Any change to `UrlRedaction`, `Shared.Contracts`, the audit API, the UI, the archiver.

## 5. End-to-end test procedure

1. **Unit (decisive).** `dotnet test tests/AuditObservability.Application.Tests -c Release --filter
   "FullyQualifiedName~AuditPayload"`: the new class (plan §4.1) and the three re-pinned assertions pass;
   every other `AuditPayload*` test passes unmodified.
2. **CI integration.** `CameraAddressAuditIntegrationTests` (CI shard; not edited) still asserts
   `DetailChanged` true for a read/read same-origin change against Postgres `jsonb`.
3. **Live (optional, only if no other Aspire stack runs on the machine).** Boot the AppHost, register a camera
   at `rtsp://cam-line4/s?x=@`, change it to `rtsp://attacker-rtsp/s?x=@`, `GET /audit/{id}` and observe
   `"DetailChanged": null` (and that Postgres accepted the `jsonb`). Otherwise step 1 plus step 2 stand as the
   evidence, stated in the PR.

## 6. Assumptions

- **[Assumption] A null raw side wins over a placeholder side.** The decision gives both "false iff ...
  either raw value is null (unchanged rule)" and "null when either side redacts to the placeholder". A null
  raw value never reaches `ToOrigin` (it is stored as JSON `null`, not as the placeholder), so it does not
  "redact to the placeholder"; the null-side rule is evaluated first and stays `false`. `false` does not claim
  "same origin", so this reading cannot conceal anything. Pinned by a green characterisation fact.
- **[Assumption] The two named tests are renamed with their re-pin.** Their names say "still reports a detail
  change" / "reports a detail change", which would contradict a `null` assertion. Renamed to
  `..._reports_the_detail_change_as_unknown`. Their inputs and every other assertion stay byte-identical.
- **Identical placeholder raw values are `null`, not `false`.** The decision's `false` arm for identical raw
  values requires "both were read". Such an event is not expected (CameraCatalog does not announce a no-op
  change, `CameraAddressAuditIntegrationTests`), but a redelivery or a forged message is redacted the same way.
