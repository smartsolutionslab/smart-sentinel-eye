import type { UseFormReturn } from 'react-hook-form';
import { SELECT_CLASS, type GridDesignerValue } from './gridDesignerModel.js';

export interface TileSpanFieldsProps {
  form: UseFormReturn<GridDesignerValue>;
  index: number;
  options: { rows: number[]; cols: number[] };
}

/**
 * The two native `Row span` / `Column span` selects for one populated tile
 * (spec 258 US3, ADR-0156). Native `<select>`s rather than drag handles: the
 * smallest surface, keyboard and screen-reader operable for free, mirroring
 * spec 228's move to native controls in this same component.
 */
export function TileSpanFields({ form, index, options }: TileSpanFieldsProps) {
  const { register } = form;

  return (
    <div className="flex gap-2">
      <label className="flex flex-1 flex-col gap-1 text-xs font-medium text-fg-muted">
        Row span
        <select
          id={`tile-${index}-row-span`}
          className={SELECT_CLASS}
          {...register(`cells.${index}.rowSpan`, { valueAsNumber: true })}
        >
          {options.rows.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      </label>
      <label className="flex flex-1 flex-col gap-1 text-xs font-medium text-fg-muted">
        Column span
        <select
          id={`tile-${index}-col-span`}
          className={SELECT_CLASS}
          {...register(`cells.${index}.colSpan`, { valueAsNumber: true })}
        >
          {options.cols.map((value) => (
            <option key={value} value={value}>
              {value}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}
