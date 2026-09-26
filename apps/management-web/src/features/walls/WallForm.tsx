import { useCreateWallMutation, type CreateWallInput } from '@smart-sentinel-eye/shared/api/walls.api';
import { createWallSchema, MIN_SCENES } from '@smart-sentinel-eye/shared/api/walls.schema';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import type { PublishedLayout } from '@smart-sentinel-eye/shared/api/layouts.api';

export interface WallFormProps {
  /** Called with the new wall's identifier once creation succeeds. */
  onSaved?: (wallIdentifier: string) => void;
}

const EMPTY: CreateWallInput = { name: '', scenes: [] };

/**
 * Spec 258 US1: names a wall and picks its ordered scene set from the fab's
 * Published layouts (PD-5: 2..8, no duplicates — `createWallSchema` enforces
 * both). Create-only for v1's UI; PD-3 resolves each scene to its chain's
 * current Published revision at display time, so nothing here pins a
 * revision. Gated server-side by `sse.layouts.write` (PD-4) — this app's
 * write mutations are not additionally gated on the frontend, matching
 * `features/layouts`, since `sse.layouts.write` is a default client scope
 * every signed-in operator already holds (spec 200).
 */
export function WallForm({ onSaved }: WallFormProps) {
  const [createWall, { isLoading, error }] = useCreateWallMutation();
  const { data, isLoading: layoutsLoading } = useListLayoutsQuery('Published');
  const published = data?.published ?? [];
  const nameFor = (layoutIdentifier: string): string =>
    published.find((layout) => layout.layoutIdentifier === layoutIdentifier)?.name ?? layoutIdentifier;

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
    setValue,
    watch,
  } = useForm<CreateWallInput>({
    resolver: zodResolver(createWallSchema),
    defaultValues: EMPTY,
  });

  // FR-002: the scene list is ordered, and that order is both the initial
  // `Showing` and where "Next" cycles from — so selection order (pick order),
  // not the checkbox list's own display order, is what gets submitted.
  // Toggling a box on appends its identifier to the end; toggling it off
  // removes it without disturbing the others' relative order.
  // react-hook-form's watch() is opaque to React Compiler (see RuleDialog.tsx).
  // eslint-disable-next-line react-hooks/incompatible-library -- see above
  const scenes = watch('scenes');

  const toggleScene = (layoutIdentifier: string, checked: boolean): void => {
    const next = checked
      ? [...scenes, layoutIdentifier]
      : scenes.filter((selected) => selected !== layoutIdentifier);
    setValue('scenes', next, { shouldValidate: true });
  };

  const moveScene = (index: number, direction: -1 | 1): void => {
    const target = index + direction;
    if (target < 0 || target >= scenes.length) {
      return;
    }
    const next = [...scenes];
    const moved = next.splice(index, 1)[0];
    if (moved === undefined) {
      return;
    }
    next.splice(target, 0, moved);
    setValue('scenes', next, { shouldValidate: true });
  };

  const onSubmit = handleSubmit(async (value) => {
    const result = await createWall(value);
    if (!('error' in result)) {
      reset(EMPTY);
      onSaved?.(result.data);
    }
  });

  const backendError = problemDetail(error, 'Could not save the wall. Try again.');

  return (
    <form onSubmit={onSubmit} className="flex flex-col gap-4">
      <FormField label="Name" htmlFor="wall-name" error={errors.name?.message}>
        <Input id="wall-name" autoFocus {...register('name')} />
      </FormField>

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-fg-primary">Scenes</legend>
        {layoutsLoading && <p className="text-sm text-fg-muted">Loading layouts…</p>}
        {!layoutsLoading && published.length < MIN_SCENES && (
          <p className="text-sm text-fg-muted">Publish at least two layouts before creating a wall.</p>
        )}
        {published.map((layout: PublishedLayout) => (
          <label key={layout.layoutIdentifier} className="flex items-center gap-2 text-sm text-fg-primary">
            <input
              type="checkbox"
              checked={scenes.includes(layout.layoutIdentifier)}
              onChange={(event) => toggleScene(layout.layoutIdentifier, event.target.checked)}
            />
            {layout.name}
          </label>
        ))}
        {errors.scenes?.message !== undefined && (
          <span role="alert" className="text-sm text-accent-fault">
            {errors.scenes.message}
          </span>
        )}
      </fieldset>

      {scenes.length > 0 && (
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium text-fg-primary">
            Scene order (first shown, then &quot;Next&quot; cycles in this order)
          </legend>
          <ol className="flex flex-col gap-1">
            {scenes.map((scene, index) => (
              <li
                key={scene}
                className="flex items-center justify-between gap-2 rounded-md border border-fg-muted/30 bg-bg-elevated px-3 py-1.5 text-sm text-fg-primary"
              >
                <span>
                  {index + 1}. {nameFor(scene)}
                </span>
                <span className="flex gap-1">
                  <button
                    type="button"
                    aria-label={`Move ${nameFor(scene)} up`}
                    disabled={index === 0}
                    className="rounded-md bg-bg-elevated/60 px-2 py-0.5 text-xs disabled:opacity-40"
                    onClick={() => moveScene(index, -1)}
                  >
                    Up
                  </button>
                  <button
                    type="button"
                    aria-label={`Move ${nameFor(scene)} down`}
                    disabled={index === scenes.length - 1}
                    className="rounded-md bg-bg-elevated/60 px-2 py-0.5 text-xs disabled:opacity-40"
                    onClick={() => moveScene(index, 1)}
                  >
                    Down
                  </button>
                </span>
              </li>
            ))}
          </ol>
        </fieldset>
      )}

      {backendError !== null && (
        <p role="alert" className="text-sm text-accent-fault">
          {backendError}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <Button type="submit" disabled={isLoading}>
          {isLoading ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </form>
  );
}
