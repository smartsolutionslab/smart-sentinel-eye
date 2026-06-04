import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import type { PublishedOverlay } from '@smart-sentinel-eye/shared/api/overlays.api';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { useFieldArray, type UseFormReturn } from 'react-hook-form';
import {
  buildCells,
  GRID_PRESETS,
  type GridDesignerValue,
} from './gridDesignerModel.js';

export interface GridDesignerProps {
  form: UseFormReturn<GridDesignerValue>;
  cameras: ReadonlyArray<CameraSummary>;
  overlays: ReadonlyArray<PublishedOverlay>;
  camerasLoading: boolean;
  overlaysLoading: boolean;
}

const SELECT_CLASS =
  'w-full rounded-md border border-fg-muted/40 bg-bg-base px-3 py-2 text-fg-primary';

/**
 * The wall designer (spec 010, FR-010): a grid-size preset picker plus a dense
 * cell grid. Each cell binds a required camera + optional overlay; an empty
 * cell is a sparse tile dropped before POST/PATCH. Validation runs against the
 * frozen multi-tile Zod schema via the form's resolver, surfacing inline
 * per-cell and grid-level errors (ADR-0079).
 */
export function GridDesigner({
  form,
  cameras,
  overlays,
  camerasLoading,
  overlaysLoading,
}: GridDesignerProps) {
  const {
    register,
    setValue,
    getValues,
    watch,
    formState: { errors },
    control,
  } = form;

  // `useFieldArray` owns the dense cell list; `replace` is the only safe way to
  // resize it (a raw `setValue` would not re-sync the rendered field rows).
  const { fields, replace } = useFieldArray({ control, name: 'cells' });
  const grid = watch('grid');

  const selectPreset = (rows: number, cols: number) => {
    const next = buildCells(rows, cols, getValues('cells'));
    setValue('grid', { rows, cols }, { shouldValidate: false });
    replace(next);
  };

  const gridError = errors.grid?.message;

  return (
    <div className="flex flex-col gap-4">
      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-fg-primary">Grid size</legend>
        <div className="flex gap-2" role="radiogroup" aria-label="Grid size">
          {GRID_PRESETS.map((preset) => {
            const active = grid.rows === preset.rows && grid.cols === preset.cols;
            return (
              <button
                key={preset.label}
                type="button"
                role="radio"
                aria-checked={active}
                onClick={() => selectPreset(preset.rows, preset.cols)}
                className={
                  active
                    ? 'rounded-md border border-accent-active bg-accent-active/10 px-3 py-1 text-sm text-accent-active'
                    : 'rounded-md border border-fg-muted/30 px-3 py-1 text-sm text-fg-muted'
                }
              >
                {preset.label}
              </button>
            );
          })}
        </div>
        {gridError !== undefined && (
          <span role="alert" className="text-sm text-accent-fault">
            {gridError}
          </span>
        )}
      </fieldset>

      <div
        className="grid gap-3"
        style={{ gridTemplateColumns: `repeat(${grid.cols}, minmax(0, 1fr))` }}
      >
        {fields.map((cell, index) => {
          const cameraError = errors.cells?.[index]?.cameraIdentifier?.message;
          const overlayError = errors.cells?.[index]?.overlayIdentifier?.message;
          const position = `${cell.row + 1},${cell.col + 1}`;
          return (
            <div
              key={cell.id}
              className="flex flex-col gap-2 rounded-md border border-fg-muted/30 bg-bg-base p-3"
            >
              <span className="text-xs font-medium text-fg-muted">Tile {position}</span>
              {/* row/col are not editable inputs — `useFieldArray` carries them
                  in the cell object so they survive submit without a registered
                  field (a hidden number input would deserialize to NaN). */}
              <FormField
                label="Camera"
                htmlFor={`tile-${index}-camera`}
                error={cameraError}
              >
                <select
                  id={`tile-${index}-camera`}
                  className={SELECT_CLASS}
                  {...register(`cells.${index}.cameraIdentifier`)}
                >
                  <option value="">
                    {camerasLoading ? 'Loading cameras…' : '(empty cell)'}
                  </option>
                  {cameras.map((camera) => (
                    <option key={camera.cameraIdentifier} value={camera.cameraIdentifier}>
                      {camera.name}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField
                label="Overlay"
                htmlFor={`tile-${index}-overlay`}
                error={overlayError}
              >
                <select
                  id={`tile-${index}-overlay`}
                  className={SELECT_CLASS}
                  {...register(`cells.${index}.overlayIdentifier`)}
                >
                  <option value="">{overlaysLoading ? 'Loading overlays…' : '(none)'}</option>
                  {overlays.map((overlay) => (
                    <option key={overlay.overlayIdentifier} value={overlay.overlayIdentifier}>
                      {overlay.name}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>
          );
        })}
      </div>
    </div>
  );
}
