import { ToggleChip } from '@smart-sentinel-eye/shared/ui/primitives/ToggleChip';

export interface StateFilterOption<T> {
  label: string;
  value: T;
}

export interface StateFilterProps<T> {
  options: ReadonlyArray<StateFilterOption<T>>;
  value: T;
  onChange: (value: T) => void;
  /** Used verbatim, not appended to: pages differ in their spacing classes. */
  className?: string;
}

// Renders the chips and reports a click. The default option, and whether the filter is applied
// to the query or to the rows, stay with the page.
export function StateFilter<T>({ options, value, onChange, className = 'flex gap-2' }: StateFilterProps<T>) {
  return (
    <div className={className} role="group" aria-label="Filter by state">
      {options.map((option) => (
        <ToggleChip key={option.label} pressed={option.value === value} onClick={() => onChange(option.value)}>
          {option.label}
        </ToggleChip>
      ))}
    </div>
  );
}

export function labelled<T extends string>(values: ReadonlyArray<T>): ReadonlyArray<StateFilterOption<T>> {
  return values.map((value) => ({ label: value, value }));
}
