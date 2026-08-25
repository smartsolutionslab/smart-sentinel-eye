# Quickstart: seeing the trap, then seeing it gone

**Feature**: `039-comparable-fab-identifier` · **Plan**: [plan.md](./plan.md)

No stack to boot. This one is entirely a test-seam change, so the whole
demonstration is a test that could not be written and now can.

---

## Before: reproduce the trap

Add to `tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs`:

```csharp
[Fact]
public async Task Two_cameras_registered_at_the_same_instant_can_be_listed()
{
    Camera one = RegisterCameraAt("2026-05-24T10:00:00Z", "Cam-A");
    Camera two = RegisterCameraAt("2026-05-24T10:00:00Z", "Cam-B");
    ListCamerasQueryHandler handler = NewHandler(one, two);

    Result<CameraListPageDto, ListCamerasError> result =
        await handler.HandleAsync(DefaultQuery(), CancellationToken.None);

    result.IsSuccess.ShouldBeTrue();
}
```

```sh
dotnet test tests/CameraCatalog.Application.Tests --filter "FullyQualifiedName~same_instant"
```

Expect:

```text
System.ArgumentException : At least one object must implement IComparable.
   at System.Collections.Comparer.Compare(Object a, Object b)
   at System.Linq.Enumerable.EnumerableSorter`2.CompareAnyKeys(Int32 index1, Int32 index2)
```

**Read that message and notice what it does not say.** Not the field. Not the
query. Not the fab. That is the half-hour this feature buys back, and it is worth
seeing once before fixing it.

---

## After

The same test passes, and the real one asserts the **order** the tie-break
produces rather than merely that nothing threw.

---

## Verification note for the PR

State each with what was observed, not what was expected:

- **All eight** copies implement the comparison. Show the count from a grep, not
  a claim.
- **The convention test passes**, and **fails when it should**: remove
  `IComparable<FabIdentifier>` from one copy, run it, record which file it names
  and what it says. Then revert. A guard that has never failed is a claim, not a
  check — and this one has a second job, so check its **message** names the file
  and explains the breakage rather than just asserting false.
- **The ordinality assertion fires too.** Change one copy's
  `StringComparison.Ordinal` to `StringComparison.InvariantCulture`, run the
  guard, record the failure, revert. This is the assertion that replaced the
  behavioural one the spec asked for and could not be written, so it carries more
  weight than usual.
- **The tying test asserts order.** Say which order, on **both** sort paths.
  Then prove it is not vacuous: make `CompareTo` return `0` unconditionally, run
  it, and record that the test fails — it would still not throw, which is exactly
  the wrong fix this guards against. Revert.
- **Coverage.** `pwsh scripts/coverage-check.ps1` if PowerShell 7 is available;
  otherwise replicate it (all non-integration test projects with coverage, merged
  through `reportgenerator` with the gate's assembly filter) and report **all
  eight Domain figures**. `Identity.Domain` is the one to watch: it measured
  91.7% against a ≥ 90% gate before this change.
- **Nothing else moved**: `git diff` over `src/` should show **only** the eight
  `FabIdentifier.cs` files. Any other file under `src/` is a finding to raise, not
  a change to keep.
- **The workaround comment is gone**, and the test that carried it still passes.
- **The drift is reported, not fixed**: `AuditObservability`'s copy omits
  `nameof(value)` from its guard, and it is the one context that had no
  `FabIdentifierTests.cs`. Say so in the PR; leave the code alone.
