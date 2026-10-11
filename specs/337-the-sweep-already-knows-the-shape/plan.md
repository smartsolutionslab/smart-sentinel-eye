# Plan — Spec 337 (#2797)

Phase 2 of ADR-0037. One file changes behaviour; one test file splits an
existing fact and adds two new ones.

## Shape

| Layer | Change |
|---|---|
| Application | `OrphanedClientSweep.SweptKinds` gains `"webhook"`; doc comments on the class and the constant updated |
| Tests (unit) | `A_webhook_stamped_client_and_an_unknown_kind_are_never_candidates` split: webhook half rewritten to assert the opposite (now swept); unknown-kind half kept, renamed, unchanged assertion |

No Infrastructure, Domain, or Api change. `GetActiveClientIdsAsync`,
`GetStampedClientsAsync`, `DisableClientAsync` are all already kind-
agnostic — nothing there needs to change for webhook to flow through them.

## Engineer

`backend-engineer` — pure Application-layer C#, no new ports.

## Colour (ADR-0139/ADR-0144)

**Behaviour-changing** for the webhook kind specifically: a webhook-kind
orphan that was previously left alone (`Examined == 0`) is now disabled.
Expect genuine red on the new fact before the fix (either `Examined`/
`Disabled` mismatch or, if the test is written against current code
first, a straightforward assertion failure — not a compile error).

Device/kiosk facts are characterisation and must stay green and
unmodified throughout — this change touches none of their logic.

## Tasks

1. Split the existing webhook+unknown-kind test into two facts.
2. Write the new "webhook orphan is now swept" fact red against
   unmodified `OrphanedClientSweep.cs`.
3. Write a new "webhook kind WITH an active row is left alone" fact —
   this one should already pass unmodified (the active-row check is
   kind-agnostic), proving the fix is purely the `SweptKinds` filter, not
   a change to the active-row logic. If it doesn't pass unmodified before
   the fix (because the kind filter runs first and drops webhook before
   the active-row check is ever reached), that's expected — both new
   facts go red together, both green together after the one-line fix.
4. Add `"webhook"` to `SweptKinds`; update the two doc comments that say
   webhook is out of scope.
5. Full `Identity.Application.Tests` run: existing device/kiosk/unknown-
   kind facts unmodified and green; new facts green.
6. `backend-reviewer` on the diff (dispatched by the orchestrator, not
   this fork, if forks can't spawn subagents).
