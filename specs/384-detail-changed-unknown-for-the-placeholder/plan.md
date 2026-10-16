# Plan 384: `DetailChanged` is unknown when either camera address redacts to the placeholder

**Spec**: [spec.md](./spec.md) | **Issue**: #2998 | **ADRs**: 0036, 0065, 0084, 0105, 0139, 0144

## 1. Bounded context and layers

**AuditObservability / Application only**: one production file,
`src/AuditObservability/Application/EventHandlers/AuditPayloadRedaction.cs`. No Domain, Infrastructure or Api
file; no `Shared.Kernel` or `Shared.Contracts` change; no migration (the column is already `jsonb`); no
AppHost resource; no UI change.

**Boundary rules.** Unchanged. The file already references `Shared.Kernel.UrlRedaction` and
`Shared.Contracts.CameraCatalog`; no reference is added. NetArchTest rules untouched. No new
Integration.Tests class, so no `tests/Integration.Tests/ci-shards/shard-N.filter` entry.

## 2. Mechanism

Inside the `CameraAddressChangedV1` registration's suffix (the existing `Registration.Suffix` slot; the
register shape from spec 377 stays):

1. `DetailChanged(string? previousUrl, string? url)` returns `bool?`:
   - either raw null -> `false` (first, unchanged);
   - `previousOrigin = UrlRedaction.ToOrigin(previousUrl)`, `origin = UrlRedaction.ToOrigin(url)` (computed
     once each, as today);
   - either equals `UrlRedaction.Unparseable` (ordinal) -> `null`;
   - otherwise `previousOrigin == origin && previousUrl != url` (ordinal, unchanged).
2. `DetailChangedMember(bool? detailChanged)` maps `true` / `false` / `null` to `"DetailChanged":true` /
   `"DetailChanged":false` / `"DetailChanged":null` (a switch expression). `AppendMember` is unchanged.
3. One short *why* comment on the `null` arm: an unread origin must never be reported as "same origin"
   (the placeholder compares equal to itself). No issue/task reference in code.

Comparing against `UrlRedaction.Unparseable` is safe: a read origin is always `scheme://host[:port]` and can
never equal `<unparseable url>`.

**Rejected alternatives** (decided by the owner on #2998; recorded so the implementer does not drift):
`false` for a placeholder side (reads as "not changed"); keeping `true` (the concealment); omitting the key
(indistinguishable from a pre-spec-377 row, contradicts FR-002).

## 3. Entities, value objects, invariants

No entity or value object. Invariants of the stored `CameraAddressChangedV1` payload after this spec:

- **I1** If either `PreviousUrl` or `Url` is stored as `<unparseable url>`, `DetailChanged` is not `true`
  (it is `null`, or `false` when the other side is null).
- **I2** `DetailChanged: true` implies both stored origins are read, non-placeholder and ordinally equal.
- **I3** The key `DetailChanged` is present on every `CameraAddressChangedV1` row written from spec 377 on.

## 4. Tests

### 4.1 New class (test-writer, red)

`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedUnknownTests.cs`. Same
harness as `AuditPayloadUserInfoRedactionTests` (`Build(repo)`, `Metadata()`, `FakeClock`). Assertions read
the stored payload and the mapped row; none reads the input text.

| Id | Fact | Expected on unchanged code |
|---|---|---|
| D1 | The #2998 concealment: `rtsp://cam-line4/s?x=@` -> `rtsp://attacker-rtsp/s?x=@`. Both URL fields are the placeholder; `DetailChanged` `ValueKind` is `Null`; raw payload contains `"DetailChanged":null`; `AuditRowMapper.Map(...).Payload` parses to `DetailChanged` `Null`. | **red** (`True`) |
| D2 | Theory, one side placeholder: (`rtsp://cam-line4/s`, `rtsp://attacker-rtsp/s?x=@`), (`rtsp://cam-line4/s?x=@`, `rtsp://cam-line4/s`), (`rtsp://cam／a`, `rtsp://cam/a`), (`rtsp://cam/a`, `garbage`) -> `Null`. | **red** (`False`) |
| D3 | Identical placeholder raw: `rtsp://cam-line4/s?x=@` both sides -> `Null`. | **red** (`False`) |
| D4 | Redelivered redacted message: both fields the literal `UrlRedaction.Unparseable` -> `Null`. | **red** (`False`, identical) |
| C1 | Null side with a placeholder other side, both orders -> `False`, row written. | green (characterisation of §6 assumption) |
| C2 | Read/read same origin, raw differs (`rtsp://cam/a` -> `rtsp://cam/b`) -> `True`; read/read different origin -> `False`. | green |

D1-D4 must fail **on an assertion** (they compile on unchanged code: they only read `JsonElement.ValueKind`).

### 4.2 The three authorised re-pins (test-writer, red, same commit)

Each changes only its `DetailChanged` assertion from `.GetBoolean().ShouldBeTrue()` to
`.ValueKind.ShouldBe(JsonValueKind.Null)`; inputs and every other assertion stay byte-identical.

| File | Test | Edit |
|---|---|---|
| `AuditPayloadHostCharsetRedactionTests.cs` | `Changing_between_two_different_non_ascii_addresses_still_reports_a_detail_change` | assertion -> `Null`; rename to `Changing_between_two_different_non_ascii_addresses_reports_the_detail_change_as_unknown` |
| `AuditPayloadUserInfoRedactionTests.cs` | `Changing_between_two_cut_short_addresses_stores_the_placeholder_and_reports_a_detail_change` | assertion -> `Null`; rename to `Changing_between_two_cut_short_addresses_stores_the_placeholder_and_reports_the_detail_change_as_unknown` |
| `AuditPayloadRedactionPostFixAdversarialTests.cs` | `DetailChanged_edges` row `("garbage SECRET1", "garbage SECRET2", true)` | delete the row from the theory (theory body and the other five rows unchanged); add a `[Fact] Two_unparseable_spellings_report_the_detail_change_as_unknown` in the same file with the same inputs, asserting `DetailChanged` `Null` and `raw.ShouldNotContain(MarkerTail, Case.Insensitive)` |

All three must be observed red on unchanged code (`True` where `Null` expected).

### 4.3 Adversarial (test-adversary, after green)

`tests/AuditObservability.Application.Tests/EventHandlers/AuditPayloadDetailChangedUnknownAdversarialTests.cs`.
Hunt for any input where a placeholder side still yields `true`:

- Generate pairs over spec 379/381 placeholder shapes (`?@`, `#@`, `/x@`, backslash marker, fullwidth
  authority, IDN `xn--`/Unicode host, U+200E file authority that makes `Uri.TryCreate` throw, empty host,
  relative/no scheme, whitespace-only, empty string) × read shapes; assert I1 for every pair, both orders.
- Assert I2 over the same matrix: any `True` row has two non-placeholder, equal origins.
- Case/whitespace variants of the placeholder text itself (`<UNPARSEABLE URL>`, `" <unparseable url>"`) as
  raw input: whatever `ToOrigin` returns, I1/I2 hold.
- Idempotence: re-serialising a message built from a stored payload's `PreviousUrl`/`Url` keeps a `Null`
  `Null` (D4 generalised).
- The JSON is still a single object with exactly one `DetailChanged` key (no duplicate member).

A failing adversarial fact is a finding returned verbatim, fixed red-first in a follow-up commit, never an
edited test.

### 4.4 Full verification (engineer, after green)

```
dotnet build SmartSentinelEye.slnx -c Release
dotnet test tests/AuditObservability.Application.Tests -c Release
dotnet test tests/Architecture.Tests -c Release
dotnet format --verify-no-changes
```

Counterfactuals before
committing: (a) a build mapping the placeholder arm to `false` must leave D1 red; (b) a build checking only
*both* sides placeholder must leave D2 red; (c) a build checking the placeholder *after* the null rule
removed must leave C1 red.

## 5. Messaging

No domain or integration event changes. `CameraAddressChangedV1` is consumed unchanged; only the audit copy's
appended member changes value.

## 6. Risks

- **A consumer assuming a boolean.** Searched (spec §1.3): none in `src/`, `apps/`, `docs/`. The UI renders
  the raw JSON.
- **Old rows.** Pre-spec-377 rows have no key; spec 377-383 rows with placeholder sides keep their stored
  `true` (#2990 tracks rewriting). The PR body states this.
