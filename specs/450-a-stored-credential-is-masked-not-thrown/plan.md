# Plan 450: A stored address holding a credential is masked on read, not thrown

[spec](spec.md) · [tasks](tasks.md) · Issue #3118

## Context and layers

One bounded context, **CameraCatalog**, **Domain only** for production code. No Application, Infrastructure, Api,
contract, migration, frontend or AppHost change. No cross-context reference.

## Options considered

| Option | Production diff | Keeps `From` refusing userinfo | Row correctable via API | Verdict |
|---|---|---|---|---|
| **A. Admit the shape in `FromStored`**, rely on `ToOrigin` | 1 file: move one `.Satisfies` clause from `EnsureStoredInvariants` into `From` | yes, by construction | **yes** (aggregate materialises; `PATCH` validates the new value with `From`) | **chosen** |
| B. Materialise the column as a string on the read path | `ICameraQuerySource` seam + EF implementation + in-memory fake + both handlers (+ the address handler) | yes | **no** — `PATCH`/retire/address still load the aggregate and throw | rejected: larger and leaves the row stuck |

A is the established pattern: spec 389 already moved the raw-@ rule to write-only the same way. B would also need a
second mapping of `rtsp_url` or a raw-SQL read, a new pattern for one defect.

## Domain change (the whole production diff)

`src/CameraCatalog/Domain/Camera/RtspUrl.cs`:

- `EnsureStoredInvariants`: drop `.Satisfies(HasNoUserInfo, "must not contain a user:password@ segment")`. It keeps
  not-blank, max length 2048, `rtsp://` prefix.
- `From`: add that same clause as the **first** `.Satisfies` after `EnsureStoredInvariants(value)`, so the refusal
  order and every message are unchanged (FR-002).
- Comments: `FromStored`'s line becomes "a row written before the raw-@ rule, or holding a userinfo segment written
  out of band, stays readable; reads mask it (`UrlRedaction.ToOrigin`). Every write goes through From." The class
  summary's userinfo sentence says the rule is refused **on write**, matching the other rules' wording.

**Invariant after the change.** New writes: unchanged (`From`). Stored reads: an `RtspUrl` may hold userinfo or be
unparseable. Every consumer of `Camera.Url` was checked (`grep "\.Url\b" src/CameraCatalog`): list and detail mask it
(`ToOrigin`), the change log masks it (`ChangeCameraAddressCommandHandler.cs:64`), the address read discloses it to a
writer by ADR-0179 design, and the domain events carry it to `CameraAddressChangedV1.PreviousUrl`, whose only reader
redacts it (`AuditPayloadRedaction.cs:34-43`). Nothing parses it.

## Messaging

None. No domain or integration event added or changed.

## ADR check

ADR-0179 §7 lists `FromStored` among what *that decision* leaves unchanged; it scopes #3016's change set, it does
not forbid a later fix, and its Context names `FromStored`'s purpose as keeping stored rows readable — which is what
this restores. The decision itself (mask on read, writer-only full read) is relied on, not altered. The precedent for
a write-only rule is spec 389. **No new ADR**; the PR body cites this paragraph so the reviewer can disagree.

## Phase 4a (red) — test-writer

Docker-free red (the phase-4a evidence):

- **R1** `tests/CameraCatalog.Domain.Tests/Camera/RtspUrlFromStoredTests.cs`: move rows `:32-33` out of
  `FromStored_keeps_refusing_what_From_refused_before_the_raw_at_rule` into a new theory
  `FromStored_reads_back_a_row_holding_a_userinfo_segment` with `rtsp://u:PWMARK@10.0.5.98/x`,
  `rtsp://admin:pass@cam/s`, `rtsp://admin:12345%23@cam/s`, `rtsp://user@[fd00::5]:8554/x`. Asserts `.Value`
  equals the input. **This edits an existing assertion on purpose**: those two rows pin the defect. Name it in the
  PR. The class summary's "must keep refusing everything From refused before the rule" is amended to except userinfo.
- **R2** new `tests/Architecture.Tests/Persistence/CameraUrlUserInfoHydrationTests.cs`: the EF converter (pattern
  of `CameraUrlHydrationCharacterisationTests.UrlProperty()`) reads the R1 rows back verbatim. Red today with the
  `ArgumentException` above.

Characterisation inside the PR (must pass **unmodified**): every `RtspUrl.From` userinfo refusal, e.g.
`RtspUrlAcceptSetCharacterisationTests`, `NonAsciiHostRows` userinfo rows, `CameraUrlZoneDelimiterRefusalTests`
(`UserInfo` tag, count 5), and the fuzz test (it requires `FromStored` never to
*throw* non-`ArgumentException`; fewer refusals cannot fail it).

Integration (CI-only unless a local stack is free — one machine, one stack):

- **R3** new `tests/Integration.Tests/CameraCatalog/StoredCredentialCameraAddressIntegrationTests.cs`, modelled on
  `StoredMisreadCameraAddressIntegrationTests` (same SQL insert, `ResetCameraCatalogAsync`): scenarios 1-2 of the
  spec, plus the stored column byte-identical. **Add the class to `tests/Integration.Tests/ci-shards/shard-1.filter`**
  next to `StoredMisreadCameraAddressIntegrationTests`. Red is a 500 on both reads.

## Phase 4b — backend-engineer

The `RtspUrl.cs` change above. May not edit any test.

## Counterfactuals (phase 4b / 5)

1. Remove the clause from `From` too (admit userinfo on write) → existing `From` userinfo tests fail. Quote, revert.
2. Put the clause back in `EnsureStoredInvariants` → R1, R2 fail. Quote, revert.
3. Replace `ToOrigin` with the raw value in `GetCameraQueryHandler` → R3's "no PWMARK" fails (CI or local). Revert.

## Security review focus (phase 6)

The credential reaches only: the writer-only audited address route (ADR-0179), the audit-redacted
`PreviousUrl`, and the database. Confirm no log, ProblemDetails, read DTO or SignalR payload carries `Url.Value`
unmasked; confirm the 500 path today did not echo the value (it did not — the message carries no input).
