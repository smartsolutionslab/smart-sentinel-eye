# Verification: Sign-in at the one origin

Observed, in the order the tasks ran. Nothing here is inferred.

## T001 — baseline (2026-11-13, branch `496-one-origin-sign-in`, clean tree, Release)

`dotnet test tests/Architecture.Tests -c Release --filter "WallClientDeclarationTests|RealmIdentityTests|KioskScopeParityTests|PasswordGrantClientTests|RealmAudienceTests"`:
**32 passed**, 0 failed. (Not the full `Architecture.Tests` run: it failed locally earlier because of a nested worktree, since removed, and a
full run is heavy on a machine that has been short of memory. CI runs it.)
