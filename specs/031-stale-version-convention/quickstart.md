# Quickstart: One way to say a version is stale

**Feature**: `031-stale-version-convention` · 2026-08-24

How to see this working, and how to prove the three things most likely to be
wrong. Two of the three are about what *did not* change.

---

## 1. The rename reached the wire

```sh
dotnet run --project src/AppHost
```

Read one camera, correct it with the version you were given, then correct it
again with the **same, now stale** version:

```sh
curl -si -X PATCH "http://localhost:<camera-catalog>/cameras/<guid>" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -H 'If-Match: "1"' -d '{"rtspUrl":"rtsp://camera-sim:8554/second"}'
```

> Expect **412**, and `"title": "CAMERA_VERSION_STALE"`.
> **Not** `CAMERA_VERSION_MISMATCH` — that is the string this feature removes.

The status is deliberately unchanged. If it has become 409, someone standardised
the statuses instead of the codes, which is the spec's central decision
reversed.

## 2. The six that were already right did not move

**The most important check, and the one nothing else will catch.** Every test
here can pass while a layouts operator starts seeing different words.

```sh
dotnet test tests/LayoutComposition.Application.Tests
dotnet test tests/OverlayDesigner.Application.Tests
dotnet test tests/SystemVariables.Application.Tests
dotnet test tests/Automation.Application.Tests
pnpm --filter @smart-sentinel-eye/management-web test
```

> All green, **with no edits to any of those test files.**

That last clause is the assurance. A passing suite that had to be adjusted to
pass proves nothing; `git diff` over those paths must be empty.

Provoke one by hand to be sure — publish a rule from a stale version and read
what the app says:

> Must still say *reload to see their version*, exactly as it did before.

## 3. The convention is enforced, not just written

Temporarily add a plausible wrong code to any errors file:

```csharp
public sealed record SomethingStale(...)
    : SomeError("WIDGET_VERSION_MISMATCH", "...", HttpStatusCode.Conflict);
```

```sh
dotnet test tests/Architecture.Tests
```

> Must **fail**, naming the offending code and the convention it missed.

Then remove it and watch the suite go green again. **Do this.** A check that
only looks for the exact old string passes forever and catches nothing — the
test for the test is that it fires for a code a future context would plausibly
invent, not for the one already removed.

## 4. The provisional note is gone

```sh
grep -rn "Provisional, pending #1857" apps/
```

> Must return nothing.

This feature exists because a decision was deferred and became a comment in
shared code. Deleting the branch while leaving the comment would be the same
failure in miniature (FR-008).

## 5. The decision is written down

```sh
ls docs/adr/ | grep -i stale
```

> An ADR exists, says whether the **code** or the **status** is authoritative,
> and says why the sixteen were left alone — including that the outlier's status
> is the *more* correct one and was kept.

An ADR that only records what was done, without the trade that was refused, is
the kind that gets reversed by someone who rediscovers the correctness argument.

---

## Verification checklist

| | |
|---|---|
| The camera's stale refusal carries `CAMERA_VERSION_STALE` | FR-001 |
| Its status is still 412 | data-model |
| A client recognises a stale refusal without reading the status | FR-002 |
| A terminal refusal is distinguishable from a lost update | FR-003 |
| No lost-update message anywhere says retry | FR-004 / SC-002 |
| An unrecognised refusal still shows the server's message | FR-005 |
| **The six correct contexts' tests pass unmodified** | FR-006 / SC-004 |
| A plausible wrong code fails the build | SC-001 |
| The convention is recorded as an ADR, with the refused trade | FR-007 / SC-005 |
| No "provisional" note remains in shared code | FR-008 |
