# Plan 280

## Files touched

- `apps/shared/src/ui/composites/OverlayGeometryFields.tsx` — always-mount the error span, add its
  per-field testid.
- `apps/shared/src/ui/composites/OverlayGeometryFields.test.tsx` — rescope every `role="alert"` query to
  the field it concerns; two assertions change what they assert (absent → present+empty).

## Sequence

1. Run the full test file against current `develop` first, green, as the documented "before."
2. Implement the component change.
3. Rescope every affected query one at a time, re-running the file after each to keep the failure surface
   small and attributable.
4. Full file green; full `apps/shared` suite green (this file's component is consumed by
   `OverlayEditor.tsx` — check nothing there queries by unscoped role either).
5. Typecheck, lint.
6. Self-review against spec §3's out-of-scope list.

## Verification

- `pnpm --filter ./apps/shared test OverlayGeometryFields` — before and after.
- `pnpm --filter ./apps/shared typecheck && pnpm --filter ./apps/shared lint`.
- Grep `apps/management-web` and `apps/kiosk-web` for any other consumer that might query this
  component's alert role unscoped (unlikely — it's a `shared` composite — but check before, not assume).
