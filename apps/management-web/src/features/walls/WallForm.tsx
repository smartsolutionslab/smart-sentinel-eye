import { useCreateWallMutation, type CreateWallInput } from '@smart-sentinel-eye/shared/api/walls.api';
import { createWallSchema } from '@smart-sentinel-eye/shared/api/walls.schema';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { problemDetail } from '@smart-sentinel-eye/shared/api/problemDetail';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Input } from '@smart-sentinel-eye/shared/ui/primitives/Input';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';

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

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<CreateWallInput>({
    resolver: zodResolver(createWallSchema),
    defaultValues: EMPTY,
  });

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
        {!layoutsLoading && published.length === 0 && (
          <p className="text-sm text-fg-muted">Publish at least two layouts before creating a wall.</p>
        )}
        {published.map((layout) => (
          <label key={layout.layoutIdentifier} className="flex items-center gap-2 text-sm text-fg-primary">
            <input type="checkbox" value={layout.layoutIdentifier} {...register('scenes')} />
            {layout.name}
          </label>
        ))}
        {errors.scenes?.message !== undefined && (
          <span role="alert" className="text-sm text-accent-fault">
            {errors.scenes.message}
          </span>
        )}
      </fieldset>

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
