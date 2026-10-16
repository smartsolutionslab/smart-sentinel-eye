# Spec 387: `DetailChanged` is unknown when either camera address is an IPv6 literal with a zone

**Feature Branch**: `fix/3006-detailchanged-ipv6-zone` (cut from `origin/develop` at `b932aa70`, which contains
specs 377, 379, 381 and 384)

**Created**: 2026-10-16

**Status**: Specified. No `[NEEDS CLARIFICATION]` remains: the rule was decided by the owner on #3006
("Decision (owner delegated, 2026-10-16)" in the issue body). Where the decision's suggested detection was
tested and found unusable on the pinned runtime, §1.2 records the experiment and the detection chosen.

**Input**: Issue [#3006](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3006),
*DetailChanged claims 'same origin' for two IPv6 link-local addresses that differ only in their zone (eth0 vs
eth1)*. Found by the security review of #2998 (spec 384).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (the stored `DetailChanged` value changes for every
address change in which a side's host is an IPv6 literal with a zone): phase 4a is **red** (ADR-0139).
**Security-sensitive** (the failure mode is a "same origin" claim over two possibly different devices in the
audit trail): phase 4 includes a `test-adversary` pass, phase 6 includes `security-reviewer`.

**Spec number.** 387. Checked 2026-10-16 against `origin/develop` (highest: 384) and every local and remote
branch head plus every open PR head (`git ls-tree <ref> specs/`): 385 is on `fix/2991-repoint-ordered-by-changedat`,
386 on `test/3001-crossfab-devices-webhook-403` (PR #3004); 388 is reserved by the sibling architect for
#2994. Nothing claims 387. Re-check before the PR is opened.

**ADRs.** ADR-0139 / ADR-0144 (phase 4a red; the lane implements a decision it did not make), ADR-0036
(smallest change), ADR-0065 (Application >= 80% coverage), ADR-0084 (metrics, advisory). Constitution §III
(no cross-context reference; `Shared.Contracts` and `Shared.Kernel` untouched). **No new ADR**: the owner
decided the rule on #3006; no contract, scope, persistence shape or value object changes; the stored payload
uses the `null` value spec 384 already introduced.

**Latency budget.** N/A. `AuditPayloadRedaction.Serialize` runs in AuditObservability's audit write path,
which is on no §IV leg. The change adds at most two `Uri.TryCreate` calls per `CameraAddressChangedV1` audit
write, and only when both origins were read.

**What "same origin" means.** `DetailChanged: true` is a statement about **names, not devices**: it says the
two addresses name the same scheme, host and port, not that they reach the same device
(`rtsp://cam/a` -> `rtsp://cam/b` is `true` even if DNS for `cam` moved between the two). An IPv6 zone is
part of the name; the stored origin drops it (deliberately, spec 377), so with a zone present the name was
not fully read and no claim is made.

---

## 1. What was found (premise verified, not assumed)

### 1.1 The claim, reproduced

A scratch file-based app outside the repository
(`%TEMP%\claude\...\scratchpad\probe387\probe.cs`, `#:project` reference to the real
`src/AuditObservability/Application/SmartSentinelEye.AuditObservability.Application.csproj` at `b932aa70`,
SDK 10.0.402, runtime 10.0.13) called the real `AuditPayloadRedaction.Serialize`:

| Previous -> Url | Stored origins | `DetailChanged` today |
|---|---|---|
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1%25eth1]/y` (the issue's pair) | `rtsp://[fe80::1]` / `rtsp://[fe80::1]` | **`true`** |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1%25eth1]/x` (the pinned row) | same | **`true`** |
| `rtsp://[fe80::1%eth0]/x` -> `rtsp://[fe80::1%251]/x` | same | **`true`** |
| `rtsp://[::ffff:10.0.0.1%25eth0]/x` -> `…%25eth1]/x` | `rtsp://[::ffff:10.0.0.1]` twice | **`true`** |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1%25ETH0]/x` | `rtsp://[fe80::1]` twice | **`true`** |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1]/x` (and reverse) | same | **`true`** |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1%25eth0]/y` | same | `true` |
| `rtsp://[fe80::1%25eth0]/x` -> identical | same | `false` |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::2]/x` | `[fe80::1]` / `[fe80::2]` | `false` |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://cam/x` | `[fe80::1]` / `rtsp://cam` | `false` |
| `rtsp://[fe80::1%25eth0]/x` -> `null` | `[fe80::1]` / null | `false` |
| `rtsp://[fe80::1%25eth0]/x` -> `rtsp://cam/s?x=@` | `[fe80::1]` / placeholder | `null` (spec 384) |
| `rtsp://[fe80::1]/x` -> `rtsp://[fe80::1]/y` | `[fe80::1]` twice | `true` |
| `rtsp://[fe80::1]/x` -> `rtsp://[fe80::2]/y` | differ | `false` |
| `rtsp://[fe80::1]/x` -> identical | same | `false` |
| `rtsp://[fd00::5]/x` -> `rtsp://[fd00:0:0:0:0:0:0:5]/x` | `[fd00::5]` twice | `true` |

The exploit path is real at the domain boundary: a second probe (`probe387b\rtsp.cs`, `#:project` to
`src/CameraCatalog/Domain`) shows `RtspUrl.From` **accepts** `rtsp://[fe80::1%25eth0]/s`,
`rtsp://[fe80::1%25eth1]/s`, `rtsp://[fe80::1%eth0]/s`, `rtsp://[fe80::1%1]/s`, `rtsp://[fe80::1%25]/s` and
`rtsp://[::ffff:10.0.0.1%25eth0]/s`. (Rejecting zones at registration is out of scope, §4.)

### 1.2 Which spellings carry a zone, and how to detect one

Same probe, per input: `UrlRedaction.ToOrigin`, then `Uri.TryCreate(url, UriKind.Absolute, out uri)` — the
exact call `UrlRedaction.TryParse` makes.

| Input | `ToOrigin` | `HostNameType` | `Host` | `DnsSafeHost` |
|---|---|---|---|---|
| `rtsp://[fe80::1%25eth0]/x` | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | `fe80::1%25eth0` |
| `rtsp://[fe80::1%eth0]/x` (unescaped `%`) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | `fe80::1%eth0` |
| `rtsp://[fe80::1%1]/x`, `%251` (numeric) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | `fe80::1%1`, `fe80::1%251` |
| `rtsp://[fe80::1%25]/x`, `rtsp://[fe80::1%]/x` (empty zone) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | `fe80::1%25`, `fe80::1%` |
| `%25ETH0`, `RTSP://[FE80::1%25eth0]` (case) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | zone kept as written |
| `%2525eth0`, `%2565th0`, `%25e%74h0`, `%09`, `%25%20` (encoded) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | zone kept, not decoded |
| `%25eth0%25eth1` (two zones) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | `fe80::1%25eth0%25eth1` |
| `rtsp://[::ffff:10.0.0.1%25eth0]/x` (IPv4-mapped) | `rtsp://[::ffff:10.0.0.1]` | IPv6 | `[::ffff:10.0.0.1]` | `::ffff:10.0.0.1%25eth0` |
| `[fe80:0:0:0:0:0:0:1%25eth0]` (long spelling) | `rtsp://[fe80::1]` | IPv6 | `[fe80::1]` | `fe80::1%25eth0` |
| `[fd00::5%25eth0]`, `[::1%25lo]`, `[::%25eth0]` (not link-local) | read, zone-free | IPv6 | bracketed, zone-free | zone present |
| `rtsps://[…%25eth0]:8554`, `http://…`, `http://…:80`, `file://[…%25eth0]/s`, `user:p@[…]`, padded with spaces, 300-char zone | read, zone-free | IPv6 | zone-free | zone present |
| `rtsp://[fe80::1]/x`, `[fd00::5]`, `[::ffff:10.0.0.1]` (no zone) | read | IPv6 | bracketed | **no `%`** |
| `rtsp://cam/x` | read | Dns | `cam` | `cam` |
| `rtsp://fe80::1%25eth0/x`, `rtsp://[fe80::1%25eth0/x`, `\\[fe80::1%25eth0]\s`, `rtsp://10.0.0.1%25eth0/x`, `rtsp://cam%25eth0/x` | placeholder | — (`TryCreate` false) | — | — |

Findings:

1. **`uri.DnsSafeHost != uri.Host` (the decision's first suggestion) does not detect a zone on this runtime.**
   `Host` keeps the brackets and `DnsSafeHost` drops them, so the comparison is `true` for **every** IPv6
   literal, zone or not (`[fe80::1]`, `[fd00::5]`, `[::ffff:10.0.0.1]` all report `True`). Using it would turn
   every IPv6 address change into `null` and break the zone-free rows that must not change (§1.3). It is
   rejected.
2. **A `%` in `DnsSafeHost` of an IPv6-typed host** (the decision's second suggestion, "a `%` in the bracketed
   host", read from the parsed `Uri`) is present for every zone spelling above, including the empty zone, and
   absent for every zone-free IPv6 literal. `IdnHost` behaves identically. `Host` never carries the zone.
3. A zone on a non-IPv6 host does not parse (`TryCreate` false), so it already redacts to the placeholder and
   is `null` by spec 384. The detection therefore only needs to look at IPv6-typed hosts.
4. Zones are not decoded or case-folded: `%25eth0`, `%25ETH0` and `%eth0` are three different strings. The
   rule never compares zones, so this does not matter: any zone on either side makes the value unknown.

Detection used by this spec: `uri.HostNameType == UriHostNameType.IPv6 && uri.DnsSafeHost.Contains('%')`.

### 1.3 Every test that pins `DetailChanged` for a zone-bearing pair

Grep for `%25`, `%eth`, `fe80`, `::ffff` across `tests/` (`*.cs`, excluding `obj/`):

| # | Test (file : line) | Uses a zone how | Pins `DetailChanged`? | Under this spec | Change? |
|---|---|---|---|---|---|
| 1 | `AuditPayloadRedactionPostFixAdversarialTests.DetailChanged_edges`, row `("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth1]/x", true)` (:99) | both sides zoned | **`true`** | `null` | **yes — the one assertion the decision authorises**, moved to its own fact (plan §4.2) |
| 2 | `AuditPayloadRedactionPostFixAdversarialTests.Address_change_leaks_nothing`, spelling `rtsp://[fe80::1%25SECRET]/x` (:30) | change `…/x` -> `…/x2` | no (asserts only that the marker is absent) | value becomes `null`, not asserted | no |
| 3 | same file, `Registration_leaks_nothing` with the same spelling | `CameraRegisteredV1` | no (no `DetailChanged` on that event) | unchanged | no |
| 4 | `Shared.Kernel.Tests/UrlRedactionAdversarialTests` (:41, :42, :116), `UrlRedactionHostCharsetTests` (:35, :235, :237), `UrlRedactionHostCharsetAdversarialTests` (:94-:96), `UrlRedactionUserInfoAdversarialTests` (:196) | `ToOrigin` outputs | no | `ToOrigin` is not changed | no |
| 5 | `AuditPayloadDetailChangedAdversarialTests` (spec 384) | its corpus has no zone (only `[fd00::5]` and `[fd00:0:0:0:0:0:0:5]`) | yes, for zone-free pairs | unchanged | no |
| 6 | `AuditPayloadDetailChangedUnknownTests`, `AuditPayloadRedactionTests`, `AuditPayloadHostCharsetRedactionTests`, `AuditPayloadUserInfoRedactionTests`, `Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests` | no IPv6 literal with a zone | yes, for non-zone pairs | unchanged | no |

**Exactly one existing assertion changes** (row 1), and it is the one the decision names. No other test
pins `DetailChanged` for a zone-bearing pair.

## 2. User story

### US1 (P1) — An audit reader is never told "same origin" when a zone was dropped from the origin

As an auditor reading a `CameraAddressChangedV1` row, when either address is an IPv6 literal with a zone,
I see `DetailChanged: null` (unknown), because the stored origin `rtsp://[fe80::1]` does not say which link
it was on, and two such addresses may name different devices.

**Why P1 / why one slice.** One rule in one function, observable in the stored row and the audit API answer.
It is the whole issue.

**Independent test.** Handle a `CameraAddressChangedV1` through `IntegrationEventAuditHandler` with an
`InMemoryAuditEventRepository` and read `DetailChanged` and the two URL fields in the stored payload and in
`AuditRowMapper.Map(...).Payload` (plan §4).

#### Acceptance scenarios

```gherkin
Feature: DetailChanged is unknown when an IPv6 zone was dropped from an origin

  Scenario: The pair from #3006 is no longer reported as a detail change on one origin
    Given a camera address change from "rtsp://[fe80::1%25eth0]/x" to "rtsp://[fe80::1%25eth1]/y"
    When the audit row is written
    Then "PreviousUrl" and "Url" are both "rtsp://[fe80::1]"
    And the stored payload contains the member "DetailChanged":null
    And the stored payload contains neither "eth0" nor "eth1"
    And the audit API row's payload has "DetailChanged" null

  Scenario Outline: A zone on either side makes DetailChanged unknown, whatever the spelling
    Given a camera address change from "<previous>" to "<url>"
    When the audit row is written
    Then "DetailChanged" is null
    Examples:
      | previous                              | url                                    |
      | rtsp://[fe80::1%eth0]/x               | rtsp://[fe80::1%251]/x                 |
      | rtsp://[fe80::1%1]/x                  | rtsp://[fe80::1%2]/x                   |
      | rtsp://[fe80::1%25]/x                 | rtsp://[fe80::1%25eth0]/x              |
      | rtsp://[fe80::1%25eth0]/x             | rtsp://[fe80::1%25ETH0]/x              |
      | rtsp://[fe80::1%2565th0]/x            | rtsp://[fe80::1%25eth0]/x              |
      | rtsp://[::ffff:10.0.0.1%25eth0]/x     | rtsp://[::ffff:10.0.0.1%25eth1]/x      |
      | rtsp://[fe80:0:0:0:0:0:0:1%25eth0]/x  | rtsp://[fe80::1%25eth1]/x              |
      | http://[fe80::1%25eth0]/x             | http://[fe80::1%25eth1]:80/x           |
      | rtsp://[fe80::1%25eth0]/x             | rtsp://[fe80::1]/x                     |
      | rtsp://[fe80::1]/x                    | rtsp://[fe80::1%25eth0]/x              |
      | rtsp://[fe80::1%25eth0]/x             | rtsp://[fe80::1%25eth0]/y              |
      | rtsp://[fe80::1%25eth0]/x             | rtsp://[fe80::1%25eth0]/x              |
      | rtsp://[fe80::1%25eth0]/x             | rtsp://[fe80::2]/x                     |
      | rtsp://cam/x                          | rtsp://[fe80::1%25eth0]/x              |

  Scenario: An IPv6 address without a zone is unchanged
    Given a change from "rtsp://[fe80::1]/x" to "rtsp://[fe80::1]/y"
    Then "DetailChanged" is true
    And a change from "rtsp://[fe80::1]/x" to "rtsp://[fe80::2]/y" has "DetailChanged" false
    And a change from "rtsp://[fd00::5]/x" to "rtsp://[fd00:0:0:0:0:0:0:5]/x" has "DetailChanged" true

  Scenario: A null side still wins (unchanged, false)
    Given a change from null to "rtsp://[fe80::1%25eth0]/x"
    Then "DetailChanged" is false and the row is still written

  Scenario: The stored origin stays zone-free
    Given a change from "rtsp://[fe80::1%25SECRET]/x" to "rtsp://[fe80::1%25eth1]/x"
    Then "PreviousUrl" and "Url" are both "rtsp://[fe80::1]" and the payload does not contain "SECRET"
```

**Bad-request / auth / conflict scenarios.** N/A: no endpoint, scope, fab check or optimistic-concurrency
path changes. The audit handler is a message consumer; a null wire field (spec 377 FR-005) keeps its
behaviour (scenario "A null side").

## 3. Functional requirements

- **FR-001** For `CameraAddressChangedV1`, `DetailChanged` is, evaluated in this order:
  - `false` if either raw value is null (unchanged, spec 384 precedence);
  - otherwise `null` if either `ToOrigin(raw)` is `UrlRedaction.Unparseable` (unchanged, spec 384);
  - otherwise `null` if either raw value parses (`Uri.TryCreate(raw, UriKind.Absolute, ...)`) to a host whose
    `HostNameType` is `IPv6` and whose `DnsSafeHost` contains `%` (**new**);
  - otherwise `true` iff the two origins are ordinally equal and the raw values ordinally differ;
  - otherwise `false`.
- **FR-002** The zone rule applies to **either** side, as the decision states: a zone against a zone-free
  address, a zone against a different host, and two identical zoned addresses are all `null`. No zone is
  ever compared, decoded or case-folded.
- **FR-003** `UrlRedaction.ToOrigin`'s output is unchanged; the stored `PreviousUrl` / `Url` stay zone-free.
  `VerifyRedacted`, `AppendMember`, `DetailChangedMember`, the register and every other registered event are
  unchanged.
- **FR-004** Exactly one existing assertion changes: the zone row of `DetailChanged_edges` (§1.3 row 1),
  from `true` to `null`, moved to its own fact. No other existing test assertion changes.
- **FR-005** Rejected by the decision and not to be implemented: comparing on `DnsSafeHost` and returning
  `false` when zones differ (two identical displayed origins with `false` read as "identical address").

## 4. Out of scope

- Rejecting or normalising zones at registration (`RtspUrl`, `StreamSourceUrl`).
- Rewriting existing audit rows (#2990): rows written before this spec keep their stored `true`.
- Any change to `UrlRedaction`, `Shared.Contracts`, the audit API, the UI, the archiver, or the StreamDistribution
  log lines that print two origins (`MediaMtxSourceRepair`; they print, they do not claim "same origin").
- Making a re-serialised **audit copy** of a zoned change unknown: the copy holds only the zone-free origins,
  so the zone is gone by design (plan §6).

## 5. End-to-end test procedure

1. **Unit (decisive).** `dotnet test tests/AuditObservability.Application.Tests -c Release --filter
   "FullyQualifiedName~AuditPayload"`: the new class (plan §4.1) and the one re-pinned fact pass; every other
   `AuditPayload*` test passes unmodified.
2. **CI integration.** `CameraAddressAuditIntegrationTests` (CI shard; not edited) still asserts
   `DetailChanged` true for a read/read same-origin change against Postgres `jsonb`.
3. **Live (optional, only if no other Aspire stack runs on the machine).** Boot the AppHost, register a camera
   at `rtsp://[fe80::1%25eth0]/s`, change it to `rtsp://[fe80::1%25eth1]/s`, `GET /audit/{id}` and observe
   both origins `rtsp://[fe80::1]` and `"DetailChanged": null`. Otherwise step 1 plus step 2 stand as the
   evidence, stated in the PR.

## 6. Assumptions

- **The decision's two detection examples are alternatives, and the first is unusable.** §1.2 finding 1:
  `DnsSafeHost != Host` is true for every IPv6 literal on runtime 10.0.13. The decision says "verify on the
  pinned runtime"; the second example, a `%` in the parsed IPv6 host, is used.
- **A null raw side still wins over a zoned side** (`false`), carrying spec 384's precedence unchanged.
  `false` claims no "same origin", so this cannot conceal anything.
- **The zone check runs only after both origins were read.** A string `ToOrigin` read has already passed the
  identical `Uri.TryCreate` call without throwing, so the second parse cannot throw. If it ever returned
  `false`, the value is treated as unknown (fail closed), not as zone-free.
