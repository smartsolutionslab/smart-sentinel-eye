import { useListWallsQuery } from '@smart-sentinel-eye/shared/api/walls.api';
import { useListLayoutsQuery } from '@smart-sentinel-eye/shared/api/layouts.api';
import { Button } from '@smart-sentinel-eye/shared/ui/primitives/Button';
import { Dialog } from '@smart-sentinel-eye/shared/ui/primitives/Dialog';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { WallForm } from './WallForm.js';

/**
 * Spec 258 US1: lists every wall in the operator's fabs and offers "New
 * wall" (a create dialog, mirroring `LayoutsPage`'s `LayoutEditorDialog`
 * convention). Tapping a row opens its detail page (`WallDetailPage`),
 * which owns the Next/Show switch controls — kept a separate component
 * per the spec's pinned split, not folded back into this one.
 */
export function WallsPage() {
  const [creating, setCreating] = useState(false);
  const navigate = useNavigate();
  const { data, isLoading, error, refetch } = useListWallsQuery();
  const { data: layoutsData } = useListLayoutsQuery('Published');

  const nameFor = (layoutIdentifier: string): string =>
    layoutsData?.published.find((layout) => layout.layoutIdentifier === layoutIdentifier)?.name ?? layoutIdentifier;

  const walls = data ?? [];

  return (
    <section className="p-6">
      <header className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Walls</h1>
        <Button onClick={() => setCreating(true)}>New wall</Button>
      </header>

      {error !== undefined && (
        <div
          role="alert"
          className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
        >
          Could not load walls.{' '}
          <button type="button" className="underline" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      )}

      {isLoading && <p className="text-sm text-fg-muted">Loading…</p>}
      {!isLoading && walls.length === 0 && <p className="text-sm text-fg-muted">No walls to show.</p>}

      <ul className="flex flex-col gap-2">
        {walls.map((wall) => (
          <li key={wall.wallIdentifier} className="rounded-md border border-fg-muted/30 bg-bg-elevated px-4 py-3">
            <button
              type="button"
              className="flex w-full flex-col items-start gap-1 text-left"
              onClick={() => navigate(`/walls/${wall.wallIdentifier}`)}
            >
              <h2 className="text-lg font-medium">{wall.name}</h2>
              <p className="text-xs text-fg-muted">Showing: {nameFor(wall.showing)}</p>
            </button>
          </li>
        ))}
      </ul>

      <Dialog
        open={creating}
        onOpenChange={setCreating}
        title="New wall"
        description="Name the wall and pick an ordered set of Published layouts as its scenes."
      >
        <WallForm
          onSaved={(wallIdentifier) => {
            setCreating(false);
            navigate(`/walls/${wallIdentifier}`);
          }}
        />
      </Dialog>
    </section>
  );
}
