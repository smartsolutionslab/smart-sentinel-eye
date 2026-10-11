# ADR-0173: Private constants and static readonly fields stay PascalCase

**Status:** Accepted
**Date:** 2026-10-11
**Supersedes:** —
**Superseded by:** —

## Context

`.editorconfig`'s `private_fields_are_camel_case` naming rule matches
`applicable_kinds = field` with no `required_modifiers`, so it claims
every private field including `const` and `static readonly` — which
this codebase, like every C# codebase, spells PascalCase. `dotnet
build` never surfaces the resulting 252 violations because the naming
rule's own severity key is read by the IDE and `dotnet format`, not by
the compiler (the separate `dotnet_diagnostic.IDE1006.severity` key
controls compiler visibility, and until #2764 it wasn't set at all).
Once that key is set (#2764), `dotnet format --verify-no-changes`
disagrees with what `dotnet build` calls clean — 252 `warning IDE1006`
plus 2 unrelated `error WHITESPACE` (#2174).

Two remedies exist: narrow the rule so `const`/`static readonly` fall
outside it, or rename all 252 fields to camelCase as the rule literally
asks. The second is not how any C# codebase in this repo, or generally,
spells a constant — renaming would fight the prevailing convention
rather than follow it.

## Decision

**Narrow `private_fields_are_camel_case`** so it no longer matches
`private const` or `private static readonly` fields. Add dedicated
rules permitting PascalCase for those two shapes specifically, so
`dotnet format` and `dotnet build` agree, and the 252 existing sites
become compliant rather than violating.

This matches the carve-out #2764 already added for IDE1006 enforcement
(`private_constants_are_pascal_case`, `private_static_readonly_fields_are_pascal_case`)
— that work assumed this decision without a formal record; this ADR
supplies it.

## Consequences

**Positive:**
- `dotnet format --verify-no-changes` and `dotnet build` agree on a
  tree that was already clean by the compiler's own warning-severity
  config.
- No field renames; the 252 existing sites were already following the
  convention this ADR records, not violating it.
- Unblocks #2175 (adding a `dotnet format` gate to CI's backend job) —
  that step would otherwise fail on arrival against the then-unnarrowed
  rule.

**Negative:**
- None identified; this records existing practice rather than changing
  behaviour.

## Follow-up (doc-only, lane-eligible)

- Confirm `.editorconfig`'s carve-out rules (if #2764 already added
  them) cite this ADR, or add them if #2764's PR predates this record.
- Close #2174, citing this ADR.
- #2175 (the CI format gate) can now proceed — it was blocked only on
  this decision.
