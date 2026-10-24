# Spec 450: A stored address holding a credential is masked on read, not thrown

**Feature Branch**: `fix/3118-legacy-userinfo-row` (cut from `origin/develop` at `8479dcdb`)

**Created**: 2026-10-24

**Status**: Ready for phase 4. No `[NEEDS CLARIFICATION]`.

**Owner approval** of the contract change (`FromStored` stops refusing userinfo; 39 existing cases retargeted, listed
in plan §Retargeted cases): recorded as a comment on issue #3118, 2026-10-24.

**Input**: Issue [#3118](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3118), found by the
spec 438 (#3016 slice B) security review.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing**: phase 4a is **red** (ADR-0139). **Security-sensitive**
(a stored credential, read-scope responses, the ADR-0179 disclosure route): phase 6 runs `security-reviewer`
alongside `/code-review`.

**Spec number.** 450. Checked 2026-10-24: `origin/develop` highest 448; remote and local branches hold 449
(`feat/2990-redact-audit-urls`); open PR #3136 holds 447; worktrees `D:\Github\sse-{2990,3078,3125,3127}` hold
nothing above 449. `sse-3125` (`fix/3125-rotate-recheck`) has no spec yet and may also reach for 450:
**re-check before the PR opens**.

**ADRs.** ADR-0179 (mask on read; the writer-only full read), ADR-0038/0046 (value objects, `From` +
`Ensure.That`), ADR-0067 (persisted shape), ADR-0103 (Aspire fixture), ADR-0139/0144 (lane, red first).
Prior art: spec 389 (`FromStored`, a rule enforced on write only), spec 438 (`UrlRedaction.ToOrigin` on both reads).

**No new ADR is needed** (reasoning in plan §ADR check). **Latency budget: N/A** — management reads, off every
§IV leg (ADR-0179 §7).

## 1. Premise (verified on `8479dcdb`)

**True, with one correction.** `CameraConfiguration.cs:99` maps `rtsp_url` with
`HasConversion(url => url.Value, value => RtspUrl.FromStored(value))`. `RtspUrl.FromStored` (`RtspUrl.cs:83-87`)
runs `EnsureStoredInvariants`, whose last clause is `.Satisfies(HasNoUserInfo, "must not contain a user:password@
segment")` (`:94`). `HasNoUserInfo` is false when `Uri.UserInfo` is non-empty **or the text does not parse**. So EF
materialising a row such as `rtsp://u:p@10.0.5.98/x` throws `ArgumentException("value: must not contain a
user:password@ segment")` from `Ensure.Satisfies` (`Ensure.cs:136-142`) and the request ends in a 500. The throw is
pinned today by `RtspUrlFromStoredTests.cs:32-33`, written by spec 389 to keep `FromStored` refusing what `From`
refused before the raw-@ rule.

| Read | Materialises | Effect of one such row |
|---|---|---|
| `GET /cameras` (`ListCamerasQueryHandler.cs:79-87`) | `camera.Url.Value` in the projection runs the converter | **500 for every page that contains the row**, i.e. the fab's list |
| `GET /cameras/{camera}` (`GetCameraQueryHandler.cs:35-38`) | whole `Camera`, filtered by id | 500 **for that camera only** — the issue's "every camera of the fab" holds for the list, not for this route |
| `GET /cameras/{camera}/address`, `PATCH`, `POST …/retire` | whole `Camera` via the query source/repository | 500 for that camera — so the row **cannot be corrected through the API** either |

**Slice B changed nothing here.** It wrapped both reads in `UrlRedaction.ToOrigin` *after* materialisation; the
converter throws first. `ToOrigin("rtsp://u:p@10.0.5.98/x")` would return `rtsp://10.0.5.98` (userinfo dropped),
and any unparseable value returns `<unparseable url>`.

**How such a row exists.** Never through the API: `From` has refused userinfo since `71857456` (2026-05-25). Only
out-of-band — SQL, a restore, an import, or a `System.Uri` change across runtime versions reading userinfo in text
an older parser did not. Low likelihood, high blast radius: one row takes a fab's camera list down.

## 2. User story

### US1 (P1): a camera stored with a credential is listed and shown as its origin

As an operator (or any `sse.cameras.read` holder), when a camera row carries a `user:password@` segment I want
`GET /cameras` and `GET /cameras/{camera}` to answer 200 with that camera's origin, so that one bad row does not take
the fab's camera list down and the credential still never reaches a read-scope response; and as a writer I want to be
able to correct that row through the API (ADR-0179's full read, then `PATCH`).

## 3. Acceptance scenarios

```gherkin
Feature: a stored credential is masked on read, not thrown (ADR-0179, spec 438)

  Background:
    Given operator "op-3@munich.test" holds sse.cameras.read and sse.cameras.write in fab "munich"
    And camera "Legacy-1" in "munich" is stored, by SQL, as "rtsp://u:PWMARK@10.0.5.98/x"
    And camera "Plain-1" in "munich" is stored as "rtsp://10.0.5.99/s"

  Scenario: the list answers and masks the credential
    When the operator sends GET /cameras?limit=200
    Then the response is 200
    And "Legacy-1" is listed with rtspUrl "rtsp://10.0.5.98"
    And "Plain-1" is listed too
    And the body does not contain "PWMARK"

  Scenario: the detail answers and masks the credential
    When the operator sends GET /cameras/{Legacy-1}
    Then the response is 200 with rtspUrl "rtsp://10.0.5.98"
    And the body does not contain "PWMARK"
    And the stored column is still byte-identical (no rewrite, #2990)

  Scenario: the writer-only address route and a correcting PATCH work
    When the operator sends GET /cameras/{Legacy-1}/address
    Then the response is 200 with the stored text, Cache-Control no-store
    When the operator then sends PATCH /cameras/{Legacy-1} with a credential-free address
    Then the response is 204, the stored column holds the new address
    And the audit row of the PATCH does not contain "PWMARK"
```

Not scenarios of this spec, because nothing here changes them and existing tests already pin them: a `POST` with a
userinfo address is still refused with 400 (the `From` refusal, pinned by every unmodified `From` test), and an
unauthenticated read is still 401 (the auth boundary is untouched).

Consequences the security review confirmed (accepted, no blockers): the writer-only
`GET /cameras/{Legacy-1}/address` now answers 200 with the stored text including the credential,
`Cache-Control: no-store`, audited as `CameraAddressReadV1` — exactly ADR-0179 §2-5, which exists for
credential-bearing rows. **A `PATCH` correcting a credential row now puts the credential in
`CameraAddressChangedV1.PreviousUrl`**: on RabbitMQ, in CameraCatalog's durable outbox table (ADR-0185) and possibly in
dead-letter storage (service-to-service transport). StreamDistribution discards `PreviousUrl`; the audit copy is
redacted to an origin by `AuditPayloadRedaction.cs:34-43` (spec 377). The security review accepted this, because it is
the price of correcting the row through the API at all.

## 4. Requirements

- **FR-001** `RtspUrl.FromStored` reads back any stored text that is non-blank, at most 2048 characters and starts
  with `rtsp://` (case-insensitive), userinfo or not, parseable or not (unparseable stored text is admitted too:
  `HasNoUserInfo` was false for it, so the old clause refused it, and `ToOrigin` renders it as `<unparseable url>`).
- **FR-002** `RtspUrl.From` refuses userinfo exactly as before: same message, same precedence among its refusals.
  Every existing `From` test passes unmodified.
- **FR-003** Both reads return `UrlRedaction.ToOrigin` of such a row (unchanged code, spec 438); no read-scope
  response carries userinfo, path, query or fragment.
- **FR-004** No stored row is rewritten (ADR-0179 §7, #2990).
- **Out of scope**: `StreamSourceUrl.FromStored` (`StreamSourceUrl.cs:104`) carries the same clause for
  StreamDistribution's `streams` table — the same defect shape in another context. Recommended as a follow-up issue
  (check the board first); not fixed here, to keep this a one-context change.

## 5. Independent end-to-end test

Boot the stack (`AspireFixture`), insert the two rows by SQL, call both reads with op-3's token, assert scenario 1-2.
On `develop` both answer 500; after the fix 200 with the origin. Docker-free: the EF converter built from
`CameraCatalogDbContext` (`CameraUrlHydrationCharacterisationTests` pattern) throws today and reads back the text after.
