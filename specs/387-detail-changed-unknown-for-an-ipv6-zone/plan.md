# Plan 387: `DetailChanged` is unknown when either camera address is an IPv6 literal with a zone

**Spec**: [spec.md](./spec.md) | **Issue**: #3006 | **ADRs**: 0036, 0065, 0084, 0139, 0144

## 1. Bounded context and layers

**AuditObservability / Application only**: one production file,
`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`. No Domain, Infrastructure or Api
file; no `Shared.Kernel` (`UrlRedaction`) or `Shared.Contracts` change; no migration; no AppHost resource; no
UI change.

**Boundary rules.** Unchanged. The file already references `Shared.Kernel.UrlRedaction`; the new check uses
only the BCL `System.Uri`. NetArchTest rules untouched. No new Integration.Tests class, so no
`tests/Integration.Tests/ci-shards/shard-N.filter` entry.

## 2. Mechanism

In `DetailChanged(string? previousUrl, string? url)` (returns `bool?`, spec 384), after the placeholder check
and before the ordinal rule:

```
if (previousUrl is null || url is null)            -> false      (unchanged)
previousOrigin, origin = ToOrigin(...)                           (unchanged)
if either is UrlRedaction.Unparseable               -> null       (unchanged)
if CarriesZone(previousUrl) || CarriesZone(url)     -> null       (new)
previousOrigin == origin && previousUrl != url                   (unchanged)
```

`CarriesZone(string url)` (private static, same file):

```
!Uri.TryCreate(url, UriKind.Absolute, out var uri)
|| (uri.HostNameType == UriHostNameType.IPv6 && uri.DnsSafeHost.Contains('%'))
```

- The `!TryCreate` arm is fail-closed (unknown), and unreachable in practice: the call is identical to
  `UrlRedaction.TryParse`, which already succeeded for both strings, and only strings that pass it get here.
  For the same reason no `IndexOutOfRangeException` guard is added (it would be dead code; a string that
  throws there was already turned into the placeholder by `ToOrigin`).
- **Not** `DnsSafeHost != Host`: true for every IPv6 literal on runtime 10.0.13 (spec §1.2 finding 1).
- **Not** a textual scan of the raw string for `%`: a `%` in the path or query (`rtsp://cam/%53E%43RET`) is
  not a zone; the parsed host is the only place the parser has already isolated it.
- Extend the existing *why* comment above `DetailChanged` by one clause: an origin with its zone dropped was
  not fully read either. No issue/task reference in code.

ADR-0084: `DetailChanged` grows by ~4 lines (well under 30) and one branch (complexity well under 10).

**Rejected alternatives** (decided by the owner on #3006; recorded so the implementer does not drift):
compare `DnsSafeHost` and return `false` for differing zones (two identical displayed origins with `false`
read as "identical address"); keep the zone in the stored origin (the zone is caller-chosen text and was
dropped deliberately, spec 377; `Registration_leaks_nothing` carries `%25SECRET`); change `ToOrigin`.

## 3. Entities, value objects, invariants

No entity or value object. Invariants of the stored `CameraAddressChangedV1` payload after this spec, in
addition to spec 384's I1-I3:

- **I4** If either raw address is a parseable IPv6 literal with a zone (a `%` in `DnsSafeHost`, or the host's written
  bracket content, located as `ToOrigin` does except that it trims first and does not re-check scheme characters, holding anything but hex digits, `:` and `.`; which covers the prefix-length spelling the
  parser strips; a bracket in the userinfo is unknown), `DetailChanged` is not `true` (it is `null`, or `false` when the other side is null).
- **I5** `PreviousUrl` / `Url` never contain the zone text (unchanged `ToOrigin` behaviour, re-asserted).
- **I6** For two zone-free addresses, `DetailChanged` is exactly what it was before this spec.

## 4. Tests

### 4.1 New class (test-writer, red)

`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedZoneTests.cs`. Same harness
as `AuditPayloadDetailChangedUnknownTests` (`Build(repo)`, `FakeClock`, `InMemoryAuditEventRepository`,
`AuditRowMapper.Map`). Assertions read the stored payload and the mapped row; expected values are written by
hand, none is derived from `UrlRedaction` or from the input text.

| Id | Fact | Expected on unchanged code |
|---|---|---|
| Z1 | The #3006 pair `rtsp://[fe80::1%25eth0]/x` -> `rtsp://[fe80::1%25eth1]/y`: both URL fields `rtsp://[fe80::1]`; `DetailChanged` `ValueKind` `Null`; raw payload contains `"DetailChanged":null` and does not contain `eth` (case-insensitive); mapped row's `DetailChanged` `Null`. | **red** (`True`) |
| Z2 | Theory, zone on both sides, origins equal (the first eight rows of spec §2's Scenario Outline). | **red** (`True`) x 8 |
| Z3 | Theory, zone on one side against the same zone-free address, both orders (`[fe80::1%25eth0]/x` <-> `[fe80::1]/x`). | **red** (`True`) x 2 |
| Z4 | Same zone both sides, path differs (`…%25eth0]/x` -> `…%25eth0]/y`). | **red** (`True`) |
| Z5 | Theory, zone against a different origin or identical raw: `[fe80::1%25eth0]/x` -> `[fe80::2]/x`; `rtsp://cam/x` -> `[fe80::1%25eth0]/x`; `[fe80::1%25eth0]/x` -> identical. | **red** (`False`) x 3 |
| C1 | Theory, zone-free IPv6 unchanged: `[fe80::1]/x`->`[fe80::1]/y` `True`; `[fe80::1]/x`->`[fe80::2]/y` `False`; `[fe80::1]/x` identical `False`; `[fd00::5]/x`->`[fd00:0:0:0:0:0:0:5]/x` `True`; `[::ffff:10.0.0.1]/x`->`[::ffff:10.0.0.1]/y` `True`. | green x 5 |
| C2 | Theory, null side against a zoned side, both orders -> `False`, row written. | green x 2 |
| C3 | Zoned side against a placeholder (`[fe80::1%25eth0]/x` -> `rtsp://cam/s?x=@`) -> `Null`. | green (spec 384) |
| C4 | `rtsp://[fe80::1%25SECRET]/x` -> `rtsp://[fe80::1%25eth1]/x`: both URL fields `rtsp://[fe80::1]`, payload does not contain `CRET` (assert on `DetailChanged` is Z-class and lives in Z2/Z1, not here). | green |

Every red must fail **on an assertion** (they compile on unchanged code: they read `JsonElement.ValueKind`).

### 4.2 The one authorised re-pin (test-writer, red, same commit)

`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadRedactionPostFixAdversarialTests.cs`:

- Delete the row `[InlineData("rtsp://[fe80::1%25eth0]/x", "rtsp://[fe80::1%25eth1]/x", true)]` from
  `DetailChanged_edges`. The theory body and its other four rows stay byte-identical.
- Add, next to `Two_unparseable_spellings_report_the_detail_change_as_unknown` (spec 384's precedent), a
  `[Fact] Two_link_local_addresses_that_differ_only_in_their_zone_report_the_detail_change_as_unknown` with
  the same inputs, asserting `DetailChanged` `ValueKind` `Null` and `raw.ShouldNotContain(MarkerTail,
  Case.Insensitive)`.

It must be observed red on unchanged code (`should be Null but was True`).

No other existing file under `tests/` is edited.

### 4.3 Adversarial (test-adversary, after green)

`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedZoneAdversarialTests.cs`.
A hand-written corpus, each entry tagged with its expected origin and a hand-written `Zoned` flag (not
computed by the code under test), every pair pushed through the real handler in both orders:

- Zone spellings: `%25eth0`, `%eth0`, `%1`, `%251`, `%25`, `%`, `%25ETH0`, `%2525eth0`, `%2565th0`,
  `%25e%74h0`, `%09`, `%25%20`, two zones, 300-char zone, zone on `[::ffff:a.b.c.d]`, `[::1]`, `[::]`,
  `[fd00::5]`; host spelled long, upper-case scheme/host, userinfo, surrounding whitespace, explicit default
  port, non-default port; schemes `rtsp`, `rtsps`, `http`, `file`.
- Zone-free shapes: `[fe80::1]`, `[fe80::2]`, `[fd00::5]` (short and long), `[::ffff:10.0.0.1]`, `rtsp://cam`,
  `10.0.5.12`.
- Placeholder shapes that look like zones but do not parse: `rtsp://fe80::1%25eth0/x`,
  `rtsp://[fe80::1%25eth0/x`, `rtsp://10.0.0.1%25eth0/x`, `rtsp://cam%25eth0/x`.
- Zoned spellings that do parse as IPv6 and come out `null` through the zone rule: `\\[fe80::1%25eth0]\s`,
  `file:\\[fe80::1%25eth0]\s`, `rtsp:\\[fe80::1%25eth0]/x`, and the prefix-length forms
  (`rtsp://[fe80::1/64%25eth0]/s`, covered by `AuditPayloadDetailChangedBracketContentTests`).
- A `%` outside the host (`rtsp://[fe80::1]/%25eth0`, `rtsp://[fe80::1]/?z=%25eth1`) must be treated as
  zone-free (guards against a textual implementation).
- Assert: the hand-modelled expectation per pair (spec FR-001) for every pair; I4 (no `True` with a zoned
  side); I5 (no stored field contains the zone text: check for the distinctive zone tokens); I6 (zone-free
  pairs equal the spec 384 rule); exactly one `DetailChanged` member in valid JSON.
- **Do not** assert "an unknown stays unknown when the stored audit copy is re-serialised" for zoned pairs:
  the copy has only zone-free origins by design (spec §4, §6 below).

A failing adversarial fact is a finding returned verbatim, fixed red-first in a follow-up commit, never an
edited test.

### 4.4 Full verification (engineer, after green)

```
dotnet build SmartSentinelEye.slnx -c Release
dotnet test tests/AuditObservability.Application.Tests -c Release
dotnet test tests/Architecture.Tests -c Release
dotnet test tests/Shared.Kernel.Tests -c Release --filter "FullyQualifiedName~UrlRedaction"
dotnet format --verify-no-changes
```

Counterfactuals before committing (scratch edits, restored, rebuilt — a restored file keeps its timestamp,
so force the rebuild): (a) detection `uri.DnsSafeHost != uri.Host` must leave every C1 row red;
(b) zone checked only when **both** sides carry one must leave Z3 and Z5's first two rows red;
(c) zone check placed before the null-raw rule must leave C2 red; (d) a textual `url.Contains('%')`
detection must turn the adversarial `%`-outside-the-host rows red.

## 5. Messaging

No domain or integration event changes. `CameraAddressChangedV1` is consumed unchanged; only the audit copy's
appended member changes value.

## 6. Risks

- **A re-serialised audit copy loses the unknown.** `Serialize` of a message rebuilt from a stored zoned row
  sees `rtsp://[fe80::1]` twice and stores `false` (identical). Not a production path (the audit copy is never
  re-consumed; a broker redelivery carries the full wire URLs and stays `null`). Spec 384's
  `Redelivering_the_audit_copy_...` adversarial test is unaffected because its corpus holds no zone; T020 must
  not add zoned entries to that file (it would require editing its oracle).
- **Old rows.** Rows written before this spec with zoned sides keep their stored `true` (#2990 tracks rewriting).
- **Runtime drift.** The detection depends on `DnsSafeHost` keeping the zone for IPv6. Z1-Z5 fail if a future
  runtime drops it (they would read `True` again), so the drift is loud, not silent. The prefix-length spelling,
  where the runtime already drops the zone, is covered by the B1 tests (`AuditPayloadDetailChangedBracketContentTests`)
  and the written-bracket-content rule, which does not depend on `DnsSafeHost`.
- **Consumers.** Unchanged since spec 384: nothing reads the member as a boolean (`AuditRowMapper`, the
  archiver and the UI pass the JSON through).
