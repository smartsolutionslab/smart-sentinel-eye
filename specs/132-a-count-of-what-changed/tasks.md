# Tasks 132 — A count of what changed

**Spec:** `specs/132-a-count-of-what-changed/spec.md` · **Issue:** #2169
**Phase 4a colour: RED** — behaviour-changing (spec §"The shape chosen").

| # | Task | Done when |
|---|---|---|
| T001 | Give `EnrolledKiosksKeycloakAdminClient` a way to say a kiosk's account holds no direct realm role | The fake's strip can answer either way; the default stays "held the privilege", so `A_pass_that_finds_a_kiosk_says_so_once_and_names_the_count` is untouched |
| T002 | **Red 1** — `A_pass_over_a_kiosk_that_holds_nothing_says_nothing` in `KioskPrivilegeSweepSteadyStateTests` | Observed failing: the sweep logs `1 of 1` for a kiosk it stripped nothing from. Enumeration asserted, so the silence cannot be for the wrong reason |
| T003 | **Red 2** — `A_pass_that_strips_one_of_two_counts_only_what_changed` in the same file | Observed failing: the line reports `2 of 2`. Asserted on the structured `StrippedCount` / `KioskCount` fields (ADR-0050), not on message text |
| T004 | `IKeycloakAdminClient.StripInheritedRealmRolesAsync` returns `Task<bool>` | The XML doc says what the answer means, next to the idempotence paragraph that already explains the early return |
| T005 | `HttpKeycloakAdminClient` returns the answer from both overloads | `false` at `assigned.Length == 0`, `true` after the `DELETE` succeeds. The enrolment call site discards it explicitly |
| T006 | `KioskPrivilegeSweep` counts strips and guards the line on that count | T002 and T003 green; the empty-realm silence spec 092 bought is unchanged |
| T007 | `KioskSweepOutcome` carries `StrippedCount` instead of deriving it | The three pre-existing assertions on it pass **unmodified** — the check that this is a meaning correction, not a behaviour change in the outcome |
| T008 | Update the remaining `IKeycloakAdminClient` implementations | `FakeKeycloakAdminClient` answers with `HashSet.Add`'s own result, so it models the provider's idempotence; the two `KeycloakProvisionedFabSourceTests` stubs compile |
| T009 | Correct `specs/092/spec.md` step 9 and the block under it | Step 9 reads true; the block records when it became true and by which issue, rather than that it is false |
| T010 | `dotnet build -c Release` clean; the three affected test projects green | No new warning; no test edited to pass |

## Not in this feature

- The sweep's repair behaviour and its scope (#2166's finding — the sweep strips,
  it does not delete or flag, and it cannot tell a residue from a healthy kiosk).
- Re-running spec 092's phase 5 procedure. T009 corrects its text; observing it
  needs the Aspire stack, which another agent holds.
- `CouldNotSweepKiosk`. It already names each unreachable kiosk and is untouched.
