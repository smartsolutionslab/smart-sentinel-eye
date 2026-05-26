import clsx from 'clsx';
import type { ReactNode } from 'react';

export type SortDirection = 'asc' | 'desc';

export interface DataTableColumn<TRow, TSortKey extends string = string> {
  id: string;
  header: ReactNode;
  cell: (row: TRow) => ReactNode;
  sortKey?: TSortKey;
  className?: string;
}

export interface DataTableSort<TSortKey extends string = string> {
  field: TSortKey;
  direction: SortDirection;
}

export interface DataTableProps<TRow, TSortKey extends string = string> {
  columns: DataTableColumn<TRow, TSortKey>[];
  rows: TRow[];
  getRowKey: (row: TRow) => string;
  sort?: DataTableSort<TSortKey>;
  onSortChange?: (next: DataTableSort<TSortKey>) => void;
  emptyMessage?: ReactNode;
  caption?: string;
  isLoading?: boolean;
}

// Generic sortable table composite (ADR-0079). Columns may opt in to sorting
// by declaring a sortKey; clicking a sortable header toggles direction or
// switches the active column. Pagination is rendered by the caller because
// page controls vary by feature.
export function DataTable<TRow, TSortKey extends string = string>({
  columns,
  rows,
  getRowKey,
  sort,
  onSortChange,
  emptyMessage = 'Nothing to show yet.',
  caption,
  isLoading = false,
}: DataTableProps<TRow, TSortKey>) {
  return (
    <div className="overflow-x-auto rounded-md border border-fg-muted/30">
      <table className="w-full text-left text-sm">
        {caption !== undefined && <caption className="sr-only">{caption}</caption>}
        <thead className="bg-bg-elevated text-fg-muted">
          <tr>
            {columns.map((column) => (
              <HeaderCell
                key={column.id}
                column={column}
                sort={sort}
                onSortChange={onSortChange}
              />
            ))}
          </tr>
        </thead>
        <tbody>
          {isLoading ? (
            <tr>
              <td colSpan={columns.length} className="px-3 py-6 text-center text-fg-muted">
                Loading…
              </td>
            </tr>
          ) : rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="px-3 py-6 text-center text-fg-muted">
                {emptyMessage}
              </td>
            </tr>
          ) : (
            rows.map((row) => (
              <tr key={getRowKey(row)} className="border-t border-fg-muted/20">
                {columns.map((column) => (
                  <td key={column.id} className={clsx('px-3 py-2', column.className)}>
                    {column.cell(row)}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

interface HeaderCellProps<TRow, TSortKey extends string> {
  column: DataTableColumn<TRow, TSortKey>;
  sort?: DataTableSort<TSortKey>;
  onSortChange?: (next: DataTableSort<TSortKey>) => void;
}

function HeaderCell<TRow, TSortKey extends string>({
  column,
  sort,
  onSortChange,
}: HeaderCellProps<TRow, TSortKey>) {
  const sortable = column.sortKey !== undefined && onSortChange !== undefined;
  const isActive = sortable && sort?.field === column.sortKey;
  const ariaSort: 'ascending' | 'descending' | 'none' =
    isActive && sort !== undefined
      ? sort.direction === 'asc'
        ? 'ascending'
        : 'descending'
      : 'none';

  if (!sortable) {
    return (
      <th scope="col" className={clsx('px-3 py-2 font-medium', column.className)}>
        {column.header}
      </th>
    );
  }

  const next = (): DataTableSort<TSortKey> => {
    const field = column.sortKey as TSortKey;
    if (sort?.field !== field) {
      return { field, direction: 'asc' };
    }
    return { field, direction: sort.direction === 'asc' ? 'desc' : 'asc' };
  };

  return (
    <th scope="col" aria-sort={ariaSort} className={clsx('px-3 py-2 font-medium', column.className)}>
      <button
        type="button"
        onClick={() => onSortChange(next())}
        className="inline-flex items-center gap-1 hover:text-fg-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-active rounded"
      >
        <span>{column.header}</span>
        <SortIndicator active={isActive} direction={sort?.direction} />
      </button>
    </th>
  );
}

function SortIndicator({ active, direction }: { active: boolean; direction?: SortDirection }) {
  if (!active) {
    return <span aria-hidden="true">↕</span>;
  }
  return <span aria-hidden="true">{direction === 'asc' ? '↑' : '↓'}</span>;
}
