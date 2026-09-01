import type { CameraSummary } from '@smart-sentinel-eye/shared/api/cameras.api';
import type { PublishedOverlay } from '@smart-sentinel-eye/shared/api/overlays.api';
import { FormField } from '@smart-sentinel-eye/shared/ui/composites/FormField';
import { useFieldArray, type UseFormReturn } from 'react-hook-form';
import { buildCells, GRID_PRESETS, type GridDesignerValue } from './gridDesignerModel.js';

export interface GridDesignerProps {
  form: UseFormReturn<GridDesignerValue>;
  cameras: ReadonlyArray<CameraSummary>;
  /**
   * Every camera seen since the dialog opened, by identifier (spec 055).
   *
   * A tile whose assigned camera the current filter excludes must still show
   * it: options come from the matches, so without this the select renders
   * blank while the form still holds the value. An operator reads that as lost
   * and reassigns it -- the filter silently editing the layout.
   */
  knownCameras?: ReadonlyMap<string, CameraSummary>;
  overlays: ReadonlyArray<PublishedOverlay>;
  camerasLoading: boolean;
  overlaysLoading: boolean;
  /**
   * The camera list could not be retrieved. Distinct from an empty fab, and the
   * distinction is the point: an operator who cannot tell "no cameras here"
   * from "the request failed" goes looking for the wrong problem (spec 048
   * FR-003).
   */
  camerasFailed?: boolean;
  /** Whether a name fragment is in force, so an empty list can be told from an empty fab. */
  cameraFilterActive?: boolean;
  /**
   * Id of the dialog's truncation notice, when there is one. Every camera
   * select points at it so the notice is *announced* on focus rather than
   * merely painted somewhere on the page — a notice a screen-reader user never
   * hears satisfies a screenshot and nothing else.
   */
  cameraNoticeId?: string;
}

const SELECT_CLASS = 'w-full rounded-md border border-fg-muted/40 bg-bg-base px-3 py-2 text-fg-primary';

/**
 * The wall designer (spec 010, FR-010): a grid-size preset picker plus a dense
 * cell grid. Each cell binds a required camera + optional overlay; an empty
 * cell is a sparse tile dropped before POST/PATCH. Validation runs against the
 * frozen multi-tile Zod schema via the form's resolver, surfacing inline
 * per-cell and grid-level errors (ADR-0079).
 */
/**
 * What the empty option says, which is three different things (spec 048
 * FR-003). Until now the last two were indistinguishable — both rendered as a
 * dropdown with nothing in it, so a failed request read as a fab with no
 * cameras.
 */
function emptyCameraLabel(loading: boolean, failed: boolean, isEmpty: boolean, filtering: boolean): string {
  if (loading) return 'Loading cameras…';
  // Both conditions, because a failed refetch does not discard the cameras
  // already held: the query keeps the last fulfilled data, so "unavailable"
  // would otherwise sit at the top of a full dropdown.
  if (failed && isEmpty) return 'Camera list unavailable';
  // **A search that matched nothing is not an empty fab**, and this is the one
  // place an operator is told which — the placeholder is what a screen reader
  // announces when the picker takes focus. Saying "No cameras in this fab"
  // under a filter sends them to register a camera that already exists, and
  // registering a duplicate name is refused, so they end up stuck.
  if (isEmpty && filtering) return 'No camera matches your search';
  if (isEmpty) return 'No cameras in this fab';
  return '(empty cell)';
}

/**
 * How a camera is labelled in the picker.
 *
 * <p>
 * Names are unique only <b>within</b> a fab, and this picker spans every fab the
 * operator holds. Sorting by name puts any two that collide side by side, so an
 * operator with two fabs each holding a <c>Line-1-Entrance</c> saw two identical
 * adjacent options and no way to tell which wall they were building. The fab is
 * on the wire for exactly this.
 * </p>
 *
 * <p>
 * Qualified only when it has to be. Most operators hold one fab, and appending
 * it to all 250 options would be noise in the common case to serve the rare one.
 * </p>
 */
function cameraLabel(camera: CameraSummary, ambiguousNames: ReadonlySet<string>): string {
  return ambiguousNames.has(camera.name) ? `${camera.name} (${camera.fab})` : camera.name;
}

/** Names held by more than one camera, which is possible across fabs. */
function ambiguousNamesOf(cameras: ReadonlyArray<CameraSummary>): ReadonlySet<string> {
  const seen = new Set<string>();
  const twice = new Set<string>();
  for (const camera of cameras) {
    if (seen.has(camera.name)) twice.add(camera.name);
    seen.add(camera.name);
  }
  return twice;
}

export function GridDesigner({
  form,
  cameras,
  knownCameras,
  overlays,
  camerasLoading,
  overlaysLoading,
  camerasFailed = false,
  cameraFilterActive = false,
  cameraNoticeId,
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

  /**
   * What this tile currently holds. The DOM may not be able to show it yet —
   * the camera list arrives after mount on every real load.
   */
  const selectedCameraOf = (index: number): string => watch(`cells.${index}.cameraIdentifier`) ?? '';

  /**
   * The retained cameras this render will actually append (spec 055).
   *
   * <p>
   * Needed by name because <b>a retained option is qualified by the same rule as
   * any other</b>. Computing ambiguity over the matches alone missed them: an
   * operator holding two fabs, each with a <c>Line-1-Entrance</c>, with one on
   * each of two tiles, then typing a fragment matching neither, saw two
   * identical unqualified options — the exact confusion <c>cameraLabel</c>
   * exists to remove, let back in through the retention path.
   * </p>
   *
   * <p>
   * Gathered from the cells rather than from the whole retained map so nothing
   * is qualified on account of a camera that is not on screen.
   * </p>
   */
  const retainedInUse: CameraSummary[] = [];
  for (const cell of watch('cells') ?? []) {
    const held = cell?.cameraIdentifier ?? '';
    if (held === '' || cameras.some((camera) => camera.cameraIdentifier === held)) continue;
    const retained = knownCameras?.get(held);
    if (retained !== undefined && !retainedInUse.some((camera) => camera.cameraIdentifier === held)) {
      retainedInUse.push(retained);
    }
  }

  const ambiguousCameraNames = ambiguousNamesOf([...cameras, ...retainedInUse]);

  /**
   * The options for one tile: the current matches, plus the camera that tile
   * already holds if the filter excludes it (spec 055).
   *
   * <p>
   * Without the second part a filter blanks an assigned tile — the select's
   * value has no matching option, so it paints empty while the form still
   * carries the camera. The operator sees a tile they filled become empty and
   * fills it again, and the filter has quietly edited the layout.
   * </p>
   *
   * <p>
   * Appended rather than prepended so the matches stay in their sorted order
   * and the retained one is visibly the odd entry out.
   * </p>
   */
  const optionsFor = (selected: string): ReadonlyArray<CameraSummary> => {
    if (selected === '' || cameras.some((camera) => camera.cameraIdentifier === selected)) {
      return cameras;
    }

    const retained = knownCameras?.get(selected);

    return retained === undefined ? cameras : [...cameras, retained];
  };

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

      <div className="grid gap-3" style={{ gridTemplateColumns: `repeat(${grid.cols}, minmax(0, 1fr))` }}>
        {fields.map((cell, index) => {
          const cameraError = errors.cells?.[index]?.cameraIdentifier?.message;
          const overlayError = errors.cells?.[index]?.overlayIdentifier?.message;
          const position = `${cell.row + 1},${cell.col + 1}`;
          return (
            <div key={cell.id} className="flex flex-col gap-2 rounded-md border border-fg-muted/30 bg-bg-base p-3">
              <span className="text-xs font-medium text-fg-muted">Tile {position}</span>
              {/* row/col are not editable inputs — `useFieldArray` carries them
                  in the cell object so they survive submit without a registered
                  field (a hidden number input would deserialize to NaN). */}
              <FormField label="Camera" htmlFor={`tile-${index}-camera`} error={cameraError}>
                {/*
                  Controlled, and it has to be. Left uncontrolled, React Hook
                  Form writes the value once at mount — when the camera list has
                  not arrived and the only option is the placeholder — and the
                  browser drops a value with no matching option. Nothing
                  re-applies it: the field is uncontrolled and RHF's ref callback
                  short-circuits on a ref it has already seen.

                  The edit dialog therefore showed every populated tile as
                  "(empty cell)" on every real load, because the list always
                  arrives after mount. An operator would read that as an
                  unassigned wall, while saving re-sent the cameras it had not
                  shown them — form state kept what the DOM had lost.

                  Holding the value in a temporary option was tried first and is
                  not enough: it survives the mount and is lost the instant the
                  real options replace it.
                */}
                <select
                  id={`tile-${index}-camera`}
                  className={SELECT_CLASS}
                  aria-describedby={cameraNoticeId}
                  {...register(`cells.${index}.cameraIdentifier`)}
                  value={selectedCameraOf(index)}
                >
                  <option value="">{emptyCameraLabel(camerasLoading, camerasFailed, cameras.length === 0, cameraFilterActive)}</option>
                  {optionsFor(selectedCameraOf(index)).map((camera) => (
                    <option key={camera.cameraIdentifier} value={camera.cameraIdentifier}>
                      {cameraLabel(camera, ambiguousCameraNames)}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Overlay" htmlFor={`tile-${index}-overlay`} error={overlayError}>
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
