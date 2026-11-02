import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { useResolveOverlayTextQuery } from '@smart-sentinel-eye/shared/api/systemVariables.api';
import { useDebouncedValue } from '@smart-sentinel-eye/shared/hooks';

/**
 * Spec 148 US1 + US3. The query lives here, not in `OverlayEditor` — three
 * suites render that component bare, with no Redux `<Provider>`, and
 * mounting the hook there fails all of them with "could not find
 * react-redux context value" (plan.md "Frontend wiring"). The settled text
 * drives the query; `value.text` (via the form's `Controller`) keeps driving the
 * input, never the reverse — `useDebouncedValue`'s own doc comment says the
 * field would drop characters otherwise. The Text element only (spec 150;
 * spec 300 FR-019) — this form edits exactly one label, FR-016's
 * single-label editor seam. `''` when the set carries no Text element
 * (shapes only) — there is nothing to resolve.
 */
export function useResolvedPreview(selectedElement: OverlayElement | undefined) {
  // Spec 441: the SELECTED label's text; `''` when a shape is selected.
  const labelText = selectedElement?.kind === 'Text' ? selectedElement.text : '';
  const settledLabelText = useDebouncedValue(labelText);
  const shouldResolve = settledLabelText.includes('{{');
  const {
    currentData,
    isFetching,
    isError: resolveFailed,
  } = useResolveOverlayTextQuery({ text: settledLabelText }, { skip: !shouldResolve });
  // `data` retains the last successful result across `skip` and arg changes
  // (RTK Query, not a bug here to work around) — clearing the field, or a
  // fresh mount of this form for a different overlay, would keep showing the
  // previous resolve. `currentData` resets on both. `settled` additionally
  // withholds the preview while debounce is still catching up to what was
  // typed, so the panel never diffs live braces against a response for an
  // earlier version of the text (phase 6 blockers 2+3).
  const settled = settledLabelText === labelText;
  const resolvedPreview = settled ? currentData : undefined;
  const isResolving = isFetching || !settled;
  return { resolvedPreview, isResolving, resolveFailed };
}
