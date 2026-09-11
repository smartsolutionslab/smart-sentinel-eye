# Tasks — Spec 131

| # | Task | Phase | Done when |
|---|---|---|---|
| T1 | Re-establish the partial index predicate from the configuration and the migration, not from the issue | 1 | Predicate quoted in `spec.md` with file:line |
| T2 | Establish whether #2076 has landed | 1 | `CameraCatalogFabLookup` read; issue state checked |
| T3 | Audit every paged handler and every `(Name, Fab)`-shaped tie-break | 1 | `grep '\.Skip('` census in `spec.md`; siblings named, none fixed |
| T4 | Red: paging across a `(Name, Fab)` tie between a retired camera and its live replacement returns a row twice and drops another | 4a | Test fails, output quoted |
| T5 | Red: the same for `sort=registeredAt`, where two cameras share an instant | 4a | Test fails, output quoted |
| T6 | Append `camera.Id` to all four arms of `SortBy`; correct the two comments that credit `Fab` with breaking the tie | 4b | T4 and T5 green, unmodified |
| T7 | Full `CameraCatalog.Application.Tests` run — no existing order assertion moved | 4b | Suite green |
| T8 | `dotnet build -c Release` clean | 4b | No warnings, no errors |
| T9 | Prove `ORDER BY camera_id` translates against real Postgres, and read the plan | 5 | `verification.md` |
