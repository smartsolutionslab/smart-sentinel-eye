import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import type { CSSProperties, ReactNode } from 'react';

export function EmptyCell({ style }: { style: CSSProperties }) {
  return (
    <div
      data-testid="layout-empty-cell"
      className="flex h-full w-full items-center justify-center rounded-md border border-dashed border-fg-muted/30 bg-bg-elevated/20 text-sm text-fg-muted"
      style={style}
    >
      Empty
    </div>
  );
}

/** Explicit CSS grid placement for a `GridItem`'s span (spec 262 FR-008). */
export function gridPlacementStyle(row: number, col: number, rowSpan: number, colSpan: number): CSSProperties {
  return {
    gridRow: `${row + 1} / span ${rowSpan}`,
    gridColumn: `${col + 1} / span ${colSpan}`,
  };
}

export function FullScreen({
  message,
  action,
  header,
}: {
  message: string;
  action?: ReactNode;
  /** A `WallHeader` to keep visible through this placeholder (see `wallHeader` in `LayoutGrid`). */
  header?: ReactNode;
}) {
  return (
    <main className="relative flex min-h-screen flex-col items-center justify-center gap-4 bg-bg-base p-8 text-center">
      {header}
      <p className="text-lg">{message}</p>
      {action}
    </main>
  );
}

/** The persistent title bar; `onBack` renders the "Back" button (CellPage only — a wall page offers no way to leave, PD-6). */
export function WallHeader({ title, onBack }: { title: string; onBack?: () => void }) {
  return (
    <header className="absolute left-0 right-0 top-0 z-10 flex items-center justify-between bg-black/50 px-6 py-3 text-fg-primary">
      <h1 className="text-lg font-medium">{title}</h1>
      {onBack !== undefined && (
        <Button variant="ghost" size="touch" onClick={onBack}>
          Back
        </Button>
      )}
    </header>
  );
}
