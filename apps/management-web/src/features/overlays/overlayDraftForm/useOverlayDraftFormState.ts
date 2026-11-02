import type { OverlayElement } from '@smart-sentinel-eye/shared/api/overlays.api';
import { createOverlayDraftSchema, type CreateOverlayDraftInput } from '@smart-sentinel-eye/shared/api/overlays.schema';
import { newElement } from '@smart-sentinel-eye/shared/ui/composites/overlayLayers';
import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect, useMemo } from 'react';
import { useFieldArray, useForm, useWatch } from 'react-hook-form';
import type { OverlayEditTarget } from './overlayEditTarget.js';
import { useElementsChangeNotifier } from './useElementsChangeNotifier.js';

export const DEFAULT_INPUT: CreateOverlayDraftInput = {
  name: '',
  elements: [newElement('Text')],
};

/** The form values, their field array, the live elements, and the re-seed when the target changes. */
export function useOverlayDraftFormState(
  editTarget: OverlayEditTarget | undefined,
  onElementsChange: ((elements: OverlayElement[]) => void) | undefined,
) {
  // The create seed (`DEFAULT_INPUT`) is untouched; edit seeds a second value
  // computed from `editTarget`, exactly as `LayoutEditorDialog.tsx:155-162`
  // computes one beside `EMPTY_CREATE`. `name` is still seeded from the
  // chain's real name even though the field is hidden in edit mode, so
  // `createOverlayDraftSchema` — unchanged — keeps validating it. The form
  // holds the WHOLE `elements` array (FR-018), not just the one the editor
  // shows.
  const defaultValues = useMemo<CreateOverlayDraftInput>(() => {
    if (editTarget === undefined) return DEFAULT_INPUT;
    // The cast is needed because `OverlayShape` (`overlays.api.ts`) declares
    // `kind: 'Box' | 'Ellipse'` as ONE interface, while `CreateOverlayDraftInput`
    // (the schema's inferred output) carries `boxSchema`'s and `ellipseSchema`'s
    // member types separately — a discriminated union of two distinct object
    // types, not one shape with a union-valued `kind` — so neither narrows onto
    // the other structurally. `color` here is also pre-`canonicalColor`
    // (upper-cased, alpha-appended); every element from the API already
    // satisfies that shape at runtime, but TS cannot see through the schema's
    // `.transform()` to know it. (This is unrelated to `OverlayLabel`'s own
    // optional `kind`/`color` — that is a different type, kept only for
    // `OverlayEditor.tsx`'s byte-identical guards, FR-016.)
    return { name: editTarget.name, elements: editTarget.elements as CreateOverlayDraftInput['elements'] };
  }, [editTarget]);

  const {
    control,
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<CreateOverlayDraftInput>({
    resolver: zodResolver(createOverlayDraftSchema),
    defaultValues,
  });

  // Spec 441. `fields` carries a client-side row `id` per element so a selection
  // follows its element across a move or a removal. That id is never part of the
  // submitted value (the submit handler's `input.elements` is the form value, not `fields`).
  const { fields, append, move, remove } = useFieldArray({ control, name: 'elements' });
  const watchedElements = useWatch({ control, name: 'elements' });
  // The live values, falling back to the row for the render a row was just added.
  const elements = fields.map((field, index) => (watchedElements?.[index] ?? field) as OverlayElement);
  useElementsChangeNotifier(watchedElements, onElementsChange);

  // Re-seed when the target (or create/edit mode) changes between mounts.
  useEffect(() => {
    reset(defaultValues);
  }, [defaultValues, reset]);
  return { control, register, handleSubmit, errors, reset, defaultValues, fields, append, move, remove, elements };
}
