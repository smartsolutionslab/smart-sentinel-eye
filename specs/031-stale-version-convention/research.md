# Phase 0 Research: One way to say a version is stale

**Feature**: `031-stale-version-convention` · **Spec**: [spec.md](./spec.md) · 2026-08-24

Five questions. The headline is that this is **much smaller than it sounds** —
the rename is five call sites — and that the convention can be **enforced**
rather than merely written down, following a pattern this repo already uses.

One thing turned up that nobody was looking for: spec 029's contract documents
an error code that does not exist.

---

## 1. How big is the rename?

**Decision: five sites in `src/` and `tests/`. That is the whole of it.**

| Site | What changes |
|---|---|
| `src/CameraCatalog/Application/Commands/ChangeCameraAddressErrors.cs` | the code literal, the record name, the failure factory |
| `src/CameraCatalog/Application/Commands/Handlers/ChangeCameraAddressCommandHandler.cs` | the factory call |
| `tests/CameraCatalog.Application.Tests/Commands/ChangeCameraAddressCommandHandlerTests.cs` | the type reference |

Plus, once spec 030 lands, the shared client helper and its tests — where the
change is a **deletion**: the provisional 412 branch goes away, and
`isStaleConflict` becomes the code test it always meant to be.

**Nothing else references the code.** No OpenAPI fixture, no consumer, no
integration test asserts the string. The sixteen `*_STALE` sites are untouched,
which is the point.

**Alternatives considered**: standardising the sixteen onto 412 instead. That is
the more HTTP-correct end state — RFC 9110 §15.5.13 specifies 412 for a failed
precondition — and it is a breaking contract change across six contexts for
nothing an operator can see. Rejected in the spec, and marked there as the
decision to overturn.

---

## 2. Can the convention be enforced, or only documented?

**Decision: enforced, and the pattern already exists.**

SC-001 asks that a seventh, eighth or ninth context cannot *silently* fall
outside the convention. A list of known codes in a test does not achieve that —
a new context simply is not on the list.

`tests/Architecture.Tests/HandlerDeconstructionTests.cs` **already reads source
files** rather than reflecting over assemblies. So a source-scanning
architecture test is an existing shape here, not an invention.

That matters because reflection cannot do this job: `ApiError` takes its code as
a **constructor argument**

```csharp
public abstract record ApiError(string Code, string Message, HttpStatusCode Status);
```

so the value only exists once an instance is built, and building every error
record would mean supplying constructor arguments for each. The string literal
in the source is the only place the code can be read without running anything.

**The shape of the check**: no error code may name a version conflict except by
ending in `_STALE`. A new context inventing `FOO_VERSION_MISMATCH` or
`BAR_VERSION_CONFLICT` fails the build with a message saying which convention it
missed.

**Alternatives considered**: enumerating the known codes in a frontend test.
Kept as well — SC-001 asks for per-context coverage — but it cannot catch a
context that never gets added to the list, so it is the weaker half.

---

## 3. Where does the decision get recorded?

**Decision: a new ADR that amends ADR-0113, mirroring how ADR-0113 amends ADR-0043.**

ADR-0113 is about the concurrency **mechanism** — that versions exist, that
`If-Match` carries them, that nothing is retried automatically. It says nothing
about how a refusal is *spelled*, which is why two spellings could appear
without contradicting it.

The vocabulary is adjacent but distinct, and ADR-0113 already carries the header
`— amends ADR-0043`, so an amending ADR is this project's established shape for
exactly this relationship.

**Alternatives considered**: editing ADR-0113 in place. Rejected — it is
Accepted and dated, and rewriting an accepted decision hides that the vocabulary
question was not settled at the time. The amendment records *when* it was
settled and *why it came up*.

---

## 4. Correction found while counting: spec 029's contract documents a code that does not exist

`specs/029-camera-read-edit/contracts/cameras-api.md` says the 412 carries:

| Status | Title |
|---|---|
| **412** | `PRECONDITION_FAILED` |

The implementation answers **`CAMERA_VERSION_MISMATCH`**. There is no
`PRECONDITION_FAILED` anywhere in `src/`.

Nothing broke, because nothing reads that contract mechanically — which is
precisely why it drifted. It also means the contract, if trusted, would have led
a client to key on a code that never arrives.

**Consequence**: the rename has to correct the contract as well as the code, and
the corrected value should be the one the implementation actually returns.
Filed here rather than absorbed silently, since it is a second instance of the
same class of problem this feature exists to fix — a written convention and a
running one disagreeing.

---

## 5. What order can this be done in?

**Decision: after spec 030 lands, and the parts are separable.**

Spec 030 (#1859) adds the provisional 412 branch to `problemDetail.ts`. This
feature deletes it. Doing them in the other order would mean writing the
workaround and removing it in the same breath.

Within this feature the backend and frontend halves are independent:

- The **rename** stands alone. After it, the frontend's provisional branch is
  dead code that still passes its tests, because it also matches on the code.
- The **helper simplification** stands alone once the rename has landed.

So they can be one change or two, and the plan sequences them so the build is
green at every step rather than only at the end.

---

## Summary

| # | Finding | Status |
|---|---|---|
| 1 | The rename is five sites; the sixteen are untouched | Applied in design |
| 2 | The convention can be **enforced** by a source-scanning test — pattern already exists | Applied in design |
| 3 | A new ADR amending ADR-0113, not an edit to it | Applied in design |
| 4 | **Spec 029's contract documents a code that does not exist** | **Raised — corrected by this feature** |
| 5 | Depends on #1859; backend and frontend halves are separable | Applied in sequencing |
