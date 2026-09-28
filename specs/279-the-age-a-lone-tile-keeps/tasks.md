# Tasks 279

- [ ] T001 Write the new one-tile-staleness fact in `useWallAlignment.test.ts`; run it; confirm it fails
      for the right reason (age never clears), not a compile error. Quote the output.
- [ ] T002 Add the always-on pruning `useEffect` in `useWallAlignment.ts`.
- [ ] T003 Tighten `frameAgeFor`'s doc comment.
- [ ] T004 Re-run the new fact (green) and the full `useWallAlignment.test.ts` suite (all green,
      unmodified elsewhere). Typecheck clean.
- [ ] T005 Self-review the diff against spec §3's out-of-scope list; open the PR.
