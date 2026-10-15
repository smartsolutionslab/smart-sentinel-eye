# Spec 377: Camera URLs are audited as their origin only

**Feature Branch**: `fix/2977-redact-camera-url-in-audit` (cut from `origin/develop` at `6acde2b9`)

**Created**: 2026-10-16

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains: the decision the issue
asked for was made by the owner on #2977 (option 1, with a stated deviation). Three judgement calls
are marked **[Assumption]** in §6 for the reviewer to overturn if wanted.

**Input**: Issue [#2977](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2977),
*Audit rows, the audit API and the blob archive keep camera URLs in full, including query-string
credentials and previous addresses*. Found by the security review of #2908 (spec 373).

**Owner decision (2026-10-16, on #2977), recorded here and not re-opened:**

- Redact the camera URL fields **when the audit row is written**, reusing
  `Shared.Kernel.UrlRedaction.ToOrigin` (spec 373), so the audit payload carries
  `scheme://host[:port]` only.
- **No hash** of the redacted remainder: a short unsalted hash of a low-entropy credential is
  brute-forceable, and a keyed hash needs key management, which is a separate decision.
- When `PreviousUrl` and `Url` share an origin but differ, the payload records a boolean so the
  timeline still shows that the address changed. Nothing derived from the redacted part is stored.
- Redact **before** the payload is serialised into the row, so the API response and the blob archive
  inherit it.
- **Out of scope**: rows and blobs already written (needs its own issue, filed from this work);
  Wolverine inbox/outbox/dead-letter bodies; fab filtering on audit reads (#2927).

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (the stored, returned and archived payload
changes): phase 4a is **red** (ADR-0139), with an adversarial pass. Security-sensitive (audit
content, `sse.audit.read` scope): phase 6 includes `security-reviewer`.

**Spec number.** 377. Checked 2026-10-16 against `origin/develop` (highest: 374), every local and
remote branch head (`git ls-tree <ref> specs/`: 375 on `fix/2959-per-authority-circuit-breaker` =
PR #2987, 376 on `test/2984-handler-signature-coverage` = PR #2988) and every open PR branch
(#2988, #2987, #2983, #2982, #2981, #2979, #2978, #2970, #2811): nothing claims 377. Re-check before
the PR is opened.

**ADRs.** ADR-0073 (versioned integration events; the contracts are **not** changed), ADR-0040
(domain vs integration events; primitives at the wire), ADR-0101 / ADR-0155 (audit blob archive),
ADR-0139 / ADR-0144 (phase 4a red; the lane implements, it does not decide), ADR-0105 (`Ensure.That`),
ADR-0065 (Application >= 80% coverage), ADR-0036 (smallest change). Constitution §III (no
cross-context reference: AuditObservability reaches the contracts through `Shared.Contracts` only and
the helper through `Shared.Kernel`). **No new ADR is needed**: the owner made the one decision there
was to make, and nothing here changes a contract, a scope or a persistence shape.

**Latency budget.** N/A. The audit subscriber is its own Wolverine queue (ADR-0088), off every §IV
leg of the event-to-overlay path. The added work is one `JsonNode` round-trip for two message types.

---

## 1. What was found (premise verified, not assumed)

On `6acde2b9` (= `origin/develop`, 2026-10-16):

**One producer of the payload.** `IntegrationEventAuditHandler.AuditAsync`
(`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs:75`) is the only
production construction of `V1Envelope` (the others are tests). It sets
`Payload: JsonSerializer.Serialize(message, message.GetType())`. Every one of the 24 `Handle`
overloads delegates there, so every concrete `IIntegrationEvent` is audited through this one line
(guarded by `BoundaryTests.Every_integration_event_has_an_audit_handler`).

**Every sink copies that string; none re-reads the message.**

| Sink | Where | What it does with the payload |
|---|---|---|
| Domain row | `Domain/AuditEvent/AuditEvent.cs:167` | `StoredPayload.From(envelope.Payload)`; size derived from content |
| Postgres jsonb | `Infrastructure/Persistence/AuditEventRepository.cs:65` | `{row.Payload.Content.Value}::jsonb` |
| API DTO (search, timeline, single) | `Application/Queries/Handlers/AuditRowMapper.cs:20` | `audit.Payload.Content.Value`; all three query handlers call `AuditRowMapper.Map` |
| Blob archive | `Infrastructure/Archive/AzureBlobAuditChunkArchiver.cs:179` | re-reads rows from Postgres, writes `row.Payload.Content.Value` into NDJSON |

So redacting the string at line 75 reaches all four. `V1ResourceMap.Lookup` reads the message
**object** (for the resource `Guid`), not the payload, and is unaffected.

**Integration messages carrying a camera or stream-source URL** (every record in `Shared.Contracts`
read, including nested records and `EventMetadata`):

| Message | URL fields | Producer |
|---|---|---|
| `CameraCatalog.CameraRegisteredV1` | `Url` | CameraCatalog, on register |
| `CameraCatalog.CameraAddressChangedV1` | `PreviousUrl`, `Url` | CameraCatalog, on address change |

No other field is a camera URL. Fields read and found not to be one: `FabEventIngestedV1.Source`
(a source *name*, `plc|inference|manual|webhook`); `FabEventIngestedV1.Payload` (a fab event body,
not a camera address; out of scope); `StreamHealthChangedV1.Error` (MediaMTX path health:
`"not ready"` / `"path not registered"` / the handler's fixed fallbacks; spec 373 §1 checked
`ParsePathHealth`); `SystemVariableValueChangedV1.Value` / `SystemVariableValueRequestedV1.Value`
(an operator-defined variable value; not a camera address); `AuditChunkArchivedV1.ArchiveObjectKey`
(our own blob key). No contract has a `System.Uri` property.

**What the registration value objects admit.** `CameraCatalog.Domain.Camera.RtspUrl` refuses a
non-empty `Uri.UserInfo` but accepts any path, query and fragment up to 2048 characters, so a
`?token=SECRET` or `/SECRET/` URL reaches both messages today. The audit redaction must still
handle userinfo: it consumes a wire contract, not the value object.

**Readers of the URL fields in audit payloads.** None in `apps/` (no `previousUrl`/`url` read off an
audit payload); `AuditPage.tsx` renders the payload as an opaque JSON document. One test reads them:
`tests/Integration.Tests/CameraCatalog/CameraAddressAuditIntegrationTests.cs:40-41`.

## 2. User story

### US1 (P1): An audit record names a camera's address by its origin only

As a fab security officer, I want every audit row, audit API response and archived audit chunk that
records a camera's address to show only `scheme://host[:port]`, so that a caller holding
`sse.audit.read` but not `sse.cameras.read` cannot read a credential a vendor URL carries in its
userinfo, path, query or fragment, while the trail still shows which device was registered and that
an address changed.

**Why P1 / independently shippable:** one redaction step at the single payload producer, plus a
fail-loud classification guard; observable end to end in the stored row, the `GET /audit/{id}`
response and the archived NDJSON.

## 3. Acceptance scenarios

```gherkin
Feature: camera URLs reach the audit trail as their origin only

  # happy path: registration
  Scenario: a registered camera's URL is audited as its origin
    When CameraRegisteredV1 arrives with Url "rtsp://cam/stream?token=SECRET"
    Then the stored audit payload's "Url" is "rtsp://cam"
    And the stored payload, the AuditRowDto payload and the archived chunk line contain no "SECRET"
    And every other payload field ("Camera", "Name", "RegisteredAt", "RegisteredBy", "Metadata") is unchanged
    And the row's PayloadSizeBytes is the UTF-8 byte count of the redacted payload

  # happy path: same-origin change
  Scenario: an address change within one origin is recorded as a detail change
    When CameraAddressChangedV1 arrives with PreviousUrl "rtsp://cam/a?token=OLD" and Url "rtsp://cam/b?token=NEW"
    Then the payload's "PreviousUrl" is "rtsp://cam" and "Url" is "rtsp://cam"
    And the payload's "DetailChanged" is true
    And the payload contains neither "OLD" nor "NEW"

  # the origin itself moved
  Scenario: an address change across origins shows the move itself
    When CameraAddressChangedV1 arrives with PreviousUrl "rtsp://10.0.5.12/h264" and Url "rtsp://10.0.5.44/h264"
    Then "PreviousUrl" is "rtsp://10.0.5.12", "Url" is "rtsp://10.0.5.44" and "DetailChanged" is false

  # conflict-shaped: identical addresses (the domain never emits it, the contract allows it)
  Scenario: identical addresses record no detail change
    When CameraAddressChangedV1 arrives with PreviousUrl and Url both "rtsp://cam/x?token=S"
    Then "DetailChanged" is false and the payload contains no "token"

  # bad request: what the redactor is handed is not a URL
  Scenario Outline: an unusable URL never echoes its input
    When CameraRegisteredV1 arrives with Url "<input>"
    Then the stored "Url" is "<unparseable url>"
    And the payload contains no "SECRET"
    Examples:
      | input              |
      | not a url SECRET   |
      | /relative/SECRET   |
      |                    |

  Scenario: a null URL on the wire is stored as null and still audits
    When CameraRegisteredV1 arrives with a null Url
    Then one audit row is written and its "Url" is null

  # unaffected messages
  Scenario: a message with no URL field is audited byte for byte as before
    When CameraRenamedV1 (or any of the other 22 audited types) arrives
    Then the stored payload equals JsonSerializer.Serialize(message, message.GetType())

  # fail loud: a new URL field cannot bypass the redaction
  Scenario: a contract string field nobody classified fails the build
    Given a new or changed integration event has a string or Uri property not yet classified
    Then the architecture test fails, naming "Type.Property"
    And the message says to register it as a redacted URL field or list it as verbatim

  # auth: unchanged, restated so the reviewer sees it was considered
  Scenario: who may read the audit is unchanged
    Then GET /audit/* still requires sse.audit.read and nothing else changes about authorization
```

## 4. Functional requirements

- **FR-001** The audit payload for `CameraRegisteredV1` carries `Url` as
  `UrlRedaction.ToOrigin(Url)`; for `CameraAddressChangedV1`, `PreviousUrl` and `Url` likewise.
- **FR-002** The redaction happens in AuditObservability.Application, on the copy being serialised
  for the audit row, before `V1Envelope` is built. The integration message object is not mutated and
  the contract records in `Shared.Contracts` are not changed in shape; every other consumer
  (StreamDistribution re-pointing the SFU) still receives the full URL.
- **FR-003** `CameraAddressChangedV1` audit payloads carry a top-level boolean `DetailChanged`:
  `true` iff `ToOrigin(PreviousUrl) == ToOrigin(Url)` (ordinal) **and** `PreviousUrl != Url`
  (ordinal, on the raw values); otherwise `false`. Present (true or false) on every row written from
  this spec on; absent on rows written before it. If either raw value is null, `DetailChanged` is
  `false`.
- **FR-004** Nothing derived from the redacted part (hash, length, prefix, character class) is stored.
- **FR-005** A null URL value is written as JSON `null` and the row is still written. An empty,
  whitespace or unparseable value is written as `UrlRedaction.Unparseable`.
- **FR-006** Payloads of all other audited message types are byte-identical to
  `JsonSerializer.Serialize(message, message.GetType())` (characterisation).
- **FR-007** The set of redacted fields is an explicit register keyed by message type with
  compile-checked property names (`nameof`). An architecture test enumerates every `string` or
  `System.Uri` property of every concrete `IIntegrationEvent` in `Shared.Contracts`, recursing into
  nested `Shared.Contracts` record types (direct and as collection elements), and fails unless each
  is either in the register or in the test's explicit verbatim inventory. It also fails on a register
  entry that names no such property, and pins the register to exactly the three fields of §1.
- **FR-008** The API DTO and the archived chunk carry the payload exactly as stored; no read-side
  transformation is added (no redact-on-read).
- **FR-009** A follow-up issue is filed for the rows and blobs already written (rewrite vs retention),
  referencing this spec; it is not decided here.

## 5. Independent end-to-end test procedure

1. In-process (no Docker): run the real `IntegrationEventAuditHandler` over the in-memory repository
   with `CameraRegisteredV1` / `CameraAddressChangedV1` carrying `?token=SECRET...`; read the stored
   row, map it through `AuditRowMapper.Map`, and feed the stored payload to the real
   `AzureBlobAuditChunkArchiver` through the spec 352 no-Azure harness; gunzip; assert the marker is
   absent from all three and the fields read as specified.
2. Against the Aspire stack (CI authoritative): register a camera with
   `rtsp://10.0.5.12/h264?token=SECRET-<guid>`, change it to `...?token=SECRET2-<guid>` on the same
   origin, then read `audit_events.payload::text` and `GET /audit/{auditIdentifier}`: no marker, both
   URL fields `rtsp://10.0.5.12`, `DetailChanged: true`.

## 6. Judgement calls

- **[Assumption] `DetailChanged` follows the owner's condition literally** (FR-003): it is `true`
  only when the origin is unchanged. When the origin moved, the move is visible in the two origins
  and `DetailChanged` is `false` even if the path also changed. The alternative ("the redacted part
  differs", independent of origin) leaks the same single bit and is equally safe; it was not chosen
  because the owner stated the condition, and the name reads as "changed beyond what is shown".
- **[Assumption] `SchemaVersion` stays at 1.** The row's `SchemaVersion` versions the row, not the
  captured payload, and redaction is idempotent (`ToOrigin` of an origin is that origin), so the
  follow-up (FR-009) can re-apply it to old rows without needing a marker to find them. Old and new
  `CameraAddressChangedV1` rows are also told apart by the presence of `DetailChanged`.
- **[Assumption] A doc-only edit to `CameraAddressChangedV1`'s remarks** ("so the audit trail records
  what changed") is in scope: it no longer holds, and a stale claim is a defect. No shape change.
- **Not fixed, recorded:** `FabEventIngestedV1.Payload` and `SystemVariable*.Value` are free text an
  operator or integration controls and could contain anything, a URL included. They are not camera
  addresses and are outside the owner's decision; the verbatim inventory names them so the choice is
  visible rather than implicit.

## 7. Out of scope

Existing rows and blobs (FR-009 follow-up); Wolverine inbox/outbox/dead-letter bodies (including a
dead-lettered audit message, which carries the full contract); fab filtering on audit reads (#2927);
the two ScenarioSimulator log sites (#2976); changing what `RtspUrl` / `StreamSourceUrl` accept.
