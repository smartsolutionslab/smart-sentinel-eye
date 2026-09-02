# Contract: The guard ban and its scope

**Feature**: 057 | **Date**: 2026-09-02

This feature exposes no new runtime interface. Its externally-visible contract
is the **build-time rule** — what fails, where, and what stays exempt — plus a
standing assertion that the HTTP surface does not move.

---

## 1. The banned set

Placed in `build/guards/BannedSymbols.txt`. The filename must be exactly
`BannedSymbols.txt`; the directory is what distinguishes it from the existing
root file (research R1).

| Banned symbol | Replacement |
|---|---|
| `ArgumentNullException.ThrowIfNull(object, string)` | `Ensure.That(x).IsNotNull()` |
| `ArgumentException.ThrowIfNullOrWhiteSpace(string, string)` | `Ensure.That(x).IsNotNull().IsNotNullOrWhiteSpace()` |
| `ArgumentException.ThrowIfNullOrEmpty(string, string)` | `Ensure.That(x).IsNotNull().IsNotNullOrWhiteSpace()` |
| `ArgumentOutOfRangeException.ThrowIfLessThan<T>(T, T, string)` | `Ensure.That(x).AtLeast(n)` |
| `ArgumentOutOfRangeException.ThrowIfGreaterThan<T>(T, T, string)` | `Ensure.That(x).InRange(lo, hi)` |
| `ArgumentOutOfRangeException.ThrowIfNegative<T>(T, string)` | `Ensure.That(x).AtLeast(0)` |

Each entry carries a message naming the replacement and ADR-0139, in the
style the existing root file already uses for ADR-0049.

**All six are included, including the three with zero call sites.**

An earlier draft of this contract said the opposite — trim anything the
codebase cannot currently hit, on the grounds that "no speculative generality"
applies to rules as much as to code. Measurement at T002 showed
`ThrowIfNullOrEmpty`, `ThrowIfGreaterThan` and `ThrowIfNegative` at **zero**
sites, which under that rule would have dropped all three.

That reasoning was wrong. "No speculative generality" governs *code
abstractions* — layers and knobs built for needs that do not exist. A ban list
is not an abstraction; it is a prohibition, and a prohibition is
forward-looking by definition. Its entire purpose is the call site nobody has
written yet.

The operative test is **substitutability**: does the entry name a direct
alternative to an already-banned API? `ThrowIfNullOrEmpty` is
`ThrowIfNullOrWhiteSpace`'s sibling; `ThrowIfGreaterThan` and `ThrowIfNegative`
are `ThrowIfLessThan`'s. Banning one of a pair and leaving the other legal is
worse than banning neither — the omission reads as a deliberate carve-out, and
the next engineer reaches for the sibling that still compiles.

The cost is three lines of data and no code. What a padded list would actually
risk — a reader disbelieving the entries that matter — does not arise here,
because every entry names a real BCL guard that a real engineer could
plausibly reach for.

---

## 2. Scope matrix

Two ban lists now exist with **different** scopes. This is the whole reason
for a second file.

| Project group | ConfigureAwait ban (ADR-0049, existing) | Guard ban (ADR-0139, new) |
|---|---|---|
| Bounded contexts — Domain, Application, Infrastructure, Api | applies | **applies** |
| `Shared.Kernel`, `Shared.Contracts`, `Shared.CQRS` | exempt | **applies** |
| `ServiceDefaults`, `ApiGateway`, `MigrationRunner`, `ScenarioSimulator` | applies | **applies** |
| `tests/**` | exempt | **applies** |
| `AppHost` | applies | **exempt** |
| `**/Migrations/*.cs` | n/a | **exempt** |

Two exemptions, two different mechanisms, for two different reasons:

- **`AppHost`** is exempt by *project*, because it does not reference
  `Shared.Kernel` by design — the replacement is not merely unused there, it
  is unavailable. Excluded via the MSBuild condition.
- **Migrations** are exempt by *path*, because they share a project with
  infrastructure code that must stay covered. Excluded via `.editorconfig`
  severity, since an inline suppression would not survive regeneration
  (research R4).

`Shared.*` moving from exempt to covered is the one place this feature
*changes* an existing scoping decision. It is deliberate: ADR-0049's
exemption exists because a `Shared.*` library might one day ship standalone
and legitimately need `ConfigureAwait`. No equivalent argument applies to
argument guards — `Ensure` *lives* in `Shared.Kernel`.

---

## 3. Severity

`dotnet_diagnostic.RS0030.severity = error` is already set repo-wide and is
**unchanged**. Both ban lists inherit it.

Consequence worth stating plainly: the moment the new list is added, the build
**breaks** on the 2 production and ~28 test sites until they are converted.
That break is Story 1's red (FR-008), and it is why the ban and the
conversions land in one commit rather than two.

---

## 4. Unchanged HTTP surface

The standing assertion for Stories 2–5, and the thing most likely to be
violated by accident:

- No endpoint route, verb, request shape, or response shape changes.
- No status code changes. In particular a stale write stays `412`, and
  malformed input stays `400` — a value-object conversion failure must **not**
  surface as `500`.
- No error `Code` or `Message` string changes — `ApiError` is exempt precisely
  so this stays true.
- `If-Match` / `ETag` header syntax is unchanged; only the type it parses into
  differs.

Verified by the existing API and integration tests passing **unmodified**. A
test that has to be edited to accommodate a retyping is evidence the retyping
changed behaviour — the edit is the finding, not the fix.

---

## 5. Unchanged database schema

- No migration is generated by any part of this feature.
- Every converted column keeps its type, length, and nullability.

Verified per data-model.md's tables and by the empty-diff check in
quickstart.md. This is SC-004 and it is checkable mechanically, so it should
never be asserted from inspection.
